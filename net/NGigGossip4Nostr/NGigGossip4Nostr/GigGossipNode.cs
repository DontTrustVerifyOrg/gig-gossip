using System;
using NGigGossip4Nostr;
using System.Diagnostics;
using NBitcoin.Protocol;
using NBitcoin.Secp256k1;
using System.Numerics;
using System.Reflection;
using System.Buffers.Text;
using System.Threading.Channels;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using NNostr.Client;
using CryptoToolkit;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.ConstrainedExecution;
using GigLNDWalletAPIClient;
using System.ComponentModel.DataAnnotations.Schema;
using System.Xml.Linq;
using GigGossipSettlerAPIClient;
using System.IO;
using static NBitcoin.Protocol.Behaviors.ChainBehavior;
using System.Collections.Concurrent;
using GigGossipFrames;
using System.Net.Http;

[Serializable]
public class InvoiceData
{
    public required bool IsNetworkInvoice { get; set; }
    public required string Invoice { get; set; }
    public required string PaymentHash { get; set; }
    public required int TotalSeconds { get; set; }
}

[Serializable]
public class PaymentData
{
    public required string Invoice { get; set; }
    public required string PaymentHash { get; set; }
}


public interface IGigGossipNodeEvents
{
    public void OnNewResponse(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload, string replyInvoice, PayReq decodedReplyInvoice, string networkInvoice, PayReq decodedNetworkInvoice);
    public void OnResponseReady(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload, string key);
    public void OnResponseCancelled(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload);
    public void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, BroadcastFrame broadcastFrame);
    public void OnCancelBroadcast(GigGossipNode me, string peerPublicKey, CancelBroadcastFrame broadcastFrame);
    public void OnNetworkInvoiceAccepted(GigGossipNode me, InvoiceData iac);
    public void OnNetworkInvoiceCancelled(GigGossipNode me, InvoiceData iac);
    public void OnInvoiceAccepted(GigGossipNode me, InvoiceData iac);
    public void OnInvoiceCancelled(GigGossipNode me, InvoiceData iac);
    public void OnInvoiceSettled(GigGossipNode me, Uri serviceUri, string paymentHash, string preimage);
    public void OnPaymentStatusChange(GigGossipNode me, string status, PaymentData paydata);

    public void OnNewContact(GigGossipNode me, string pubkey);
    public void OnSettings(GigGossipNode me, string settings);
    public void OnEoseArrived(GigGossipNode me);
}

public class AcceptBroadcastResponse
{
    public required Uri SettlerServiceUri { get; set; }
    public required string [] Properties { get; set; }
    public required byte[] Message { get; set; }
    public required long Fee { get; set; }
}


public class AcceptBroadcastReturnValue
{
    public required Guid ReplierCertificateId { get; set; }
    public required PayReq DecodedReplyInvoice { get; set; }
}

public class GigGossipNode : NostrNode, IInvoiceStateUpdatesMonitorEvents, IPaymentStatusUpdatesMonitorEvents, ISettlerMonitorEvents
{
    protected long priceAmountForRouting;
    protected TimeSpan timestampTolerance;
    public TimeSpan InvoicePaymentTimeout;
    protected int fanout;
    public string Settings;

    private SemaphoreSlim alreadyBroadcastedSemaphore = new SemaphoreSlim(1, 1);

    private ConcurrentDictionary<Uri, GigStatusClient> settlerGigStatusClients ;
    private ConcurrentDictionary<Uri, PreimageRevealClient> settlerPreimageRevelClients ;

    protected ConcurrentDictionary<Uri, Guid> _walletToken;
    protected ConcurrentDictionary<Uri, Guid> _settlerToken;

    public ISettlerSelector SettlerSelector;
    public IGigLNDWalletSelector WalletSelector;

    protected InvoiceStateUpdatesMonitor _invoiceStateUpdatesMonitor;
    protected PaymentStatusUpdatesMonitor _paymentStatusUpdatesMonitor;
    protected SettlerMonitor _settlerMonitor;

    Dictionary<string, NostrContact> _contactList ;

    private IGigGossipNodeEvents gigGossipNodeEvents;
    private Uri defaultWalletUri;
    private Uri mySettlerUri;

    internal ThreadLocal<GigGossipNodeContext> nodeContext;

    ConcurrentDictionary<string, bool> messageLocks;

    public GigGossipNode(string connectionString, ECPrivKey privKey, int chunkSize) : base(privKey, chunkSize,true)
    {
        RegisterFrameType<BroadcastFrame>();
        RegisterFrameType<CancelBroadcastFrame>();
        RegisterFrameType<ReplyFrame>();

        this.nodeContext = new ThreadLocal<GigGossipNodeContext>(() => new GigGossipNodeContext(connectionString.Replace("$HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))));
        nodeContext.Value.Database.EnsureCreated();
    }

    public async Task<string> MakeWalletAuthToken()
    {
        return Crypto.MakeSignedTimedToken(
            this.privateKey, DateTime.UtcNow,
            await _walletToken.GetOrAddAsync(defaultWalletUri, async (serviceUri) => WalletAPIResult.Get<Guid>(await WalletSelector.GetWalletClient(serviceUri).GetTokenAsync(this.PublicKey))));
    }

    public async Task<string> MakeSettlerAuthTokenAsync(Uri serviceUri)
    {
        return Crypto.MakeSignedTimedToken(
            this.privateKey, DateTime.UtcNow,
            await _settlerToken.GetOrAddAsync(serviceUri, async (serviceUri) => SettlerAPIResult.Get<Guid>(await SettlerSelector.GetSettlerClient(serviceUri).GetTokenAsync(this.PublicKey))));
    }

    public async Task StartAsync(bool traceEnabled, int fanout, long priceAmountForRouting, TimeSpan timestampTolerance, TimeSpan invoicePaymentTimeout,
        string[] nostrRelays, IGigGossipNodeEvents gigGossipNodeEvents, Func<HttpClient> httpClientFactory, Uri defaultWalletUri,Uri mySettlerUri, HttpMessageHandler? httpMessageHandler=null)
    {

        this.messageLocks = new();
        this._contactList = new();
        this._walletToken = new();
        this._settlerToken = new();

        this.settlerGigStatusClients = new();
        this.settlerPreimageRevelClients = new();

        this.fanout = fanout;
        this.priceAmountForRouting = priceAmountForRouting;
        this.timestampTolerance = timestampTolerance;
        this.InvoicePaymentTimeout = invoicePaymentTimeout;

        this.defaultWalletUri = defaultWalletUri;
        this.mySettlerUri = mySettlerUri;

        SettlerSelector = new SimpleSettlerSelector(httpClientFactory);

        try
        {
            await base.StartAsync(nostrRelays, new FlowLogger(traceEnabled, mySettlerUri, SettlerSelector, this.privateKey));
        }
        catch (Exception ex)
        {
            throw new Exception($"Base: {ex.Message}");
        }

        try
        {

            WalletSelector = new SimpleGigLNDWalletSelector(httpClientFactory);

            _invoiceStateUpdatesMonitor = new InvoiceStateUpdatesMonitor(this);
            _paymentStatusUpdatesMonitor = new PaymentStatusUpdatesMonitor(this);

            _settlerMonitor = new SettlerMonitor(this);

            LoadContactList();

            this.gigGossipNodeEvents = gigGossipNodeEvents;

            LoadMessageLocks();

            _settlerToken = new();
                await _invoiceStateUpdatesMonitor.StartAsync(httpMessageHandler);
                await _paymentStatusUpdatesMonitor.StartAsync(httpMessageHandler);
                await _settlerMonitor.StartAsync();

                await SayHelloAsync();
        }
        catch(Exception ex)
        {
            await FlowLogger.TraceExceptionAsync(ex);
            throw;
        }
    }

    public override async Task StopAsync()
    {
        await base.StopAsync();
        this._invoiceStateUpdatesMonitor.Stop();
        this._paymentStatusUpdatesMonitor.Stop();
        this._settlerMonitor.Stop();
    }

    public GigLNDWalletAPIClient.swaggerClient GetWalletClient()
    {
        return WalletSelector.GetWalletClient(defaultWalletUri);
    }

    public void ClearContacts()
    {
        lock (_contactList)
        {
            this.nodeContext.Value.DeleteObjectRange(
            from c in this.nodeContext.Value.NostrContacts where c.PublicKey == this.PublicKey select c);
            _contactList.Clear();
        }
    }

    public override void OnHello(string eventId, bool isNew, string senderPublicKey)
    {
        if (senderPublicKey != this.PublicKey)
            AddContact(senderPublicKey, "");
    }

    public override void OnSettings(string eventId, bool isNew, string settings)
    {
        Settings = settings;
        if (isNew)
            this.gigGossipNodeEvents.OnSettings(this, settings);
    }

    public override void OnEose()
    {
        this.gigGossipNodeEvents.OnEoseArrived(this);
    }

    public void AddContact(string contactPublicKey, string petname, string relay = "")
    {
        if (contactPublicKey == this.PublicKey)
            throw new GigGossipException(GigGossipNodeErrorCode.SelfConnection);
        var c = new NostrContact()
        {
            PublicKey = this.PublicKey,
            ContactPublicKey = contactPublicKey,
            Petname = petname,
            Relay = relay,
        };

        lock (_contactList)
        {
            if (!_contactList.ContainsKey(c.ContactPublicKey))
            {
                _contactList[c.ContactPublicKey] = c;
                this.nodeContext.Value.AddObject(c);
                this.gigGossipNodeEvents.OnNewContact(this, c.ContactPublicKey);
            }
        }
    }

    public async Task PublishContactListAsync()
    {
        Dictionary<string, NostrContact> cl;
        lock (_contactList)
        {
            cl = new Dictionary<string, NostrContact>(_contactList);
        }
        await this.PublishContactListAsync(cl);
    }

    public List<string> LoadContactList()
    {
        lock (_contactList)
        {
            var mycontacts = (from c in this.nodeContext.Value.NostrContacts where c.PublicKey == this.PublicKey select c);
            foreach (var c in mycontacts)
                _contactList[c.ContactPublicKey] = c;
            return _contactList.Keys.ToList();
        }
    }

    public List<string> GetContactList()
    {
        lock (_contactList)
        {
            return _contactList.Keys.ToList();
        }
    }

    public override void OnContactList(string eventId, bool isNew, Dictionary<string, NostrContact> contactList)
    {
        lock (_contactList)
        {
            List<NostrContact> toadd = new List<NostrContact>();
            foreach (var c in contactList.Values)
            {
                if (!_contactList.ContainsKey(c.ContactPublicKey))
                {
                    toadd.Add(c);
                    _contactList[c.ContactPublicKey] = c;
                    this.gigGossipNodeEvents.OnNewContact(this, c.ContactPublicKey);
                }
            }
            if (toadd.Count > 0)
                this.nodeContext.Value.AddObjectRange(toadd);
        }
    }



    public async Task<GigStatusClient> GetGigStatusClientAsync(Uri serviceUri)
    {
        return await settlerGigStatusClients.GetOrAddAsync(
            serviceUri,
            async (serviceUri) =>
            {
                var newClient = new GigStatusClient(SettlerSelector.GetSettlerClient(serviceUri));
                await newClient.ConnectAsync(await MakeSettlerAuthTokenAsync(serviceUri));
                return newClient;
            });
    }

    public async void DisposeGigStatusClient(Uri serviceUri)
    {
        GigStatusClient client;
        if (settlerGigStatusClients.TryRemove(serviceUri, out client))
            await client.Connection.DisposeAsync();
    }

    public async Task<PreimageRevealClient> GetPreimageRevealClientAsync(Uri serviceUri)
    {
        return await settlerPreimageRevelClients.GetOrAddAsync(
            serviceUri,
            async (serviceUri) =>
            {
                var newClient = new PreimageRevealClient(SettlerSelector.GetSettlerClient(serviceUri));
                await newClient.ConnectAsync(await MakeSettlerAuthTokenAsync(serviceUri));
                return newClient;
            });
    }

    public async void DisposePreimageRevealClient(Uri serviceUri)
    {
        PreimageRevealClient client;
        if (settlerPreimageRevelClients.TryRemove(serviceUri, out client))
            await client.Connection.DisposeAsync();
    }

    public List<string> GetBroadcastContactList(Guid signedrequestpayloadId, string? originatorPublicKey)
    {

        var rnd = new Random();
        var contacts = new HashSet<string>(GetContactList());
        var alreadyBroadcasted = (from inc in this.nodeContext.Value.BroadcastHistory where inc.PublicKey == this.PublicKey && inc.SignedRequestPayloadId == signedrequestpayloadId select inc.ContactPublicKey).ToList();
        contacts.ExceptWith(alreadyBroadcasted);
        if (originatorPublicKey != null)
            contacts.ExceptWith(new string[] { originatorPublicKey });
        if (contacts.Count == 0)
            return new List<string>();

        var retcontacts = (from r in contacts.AsEnumerable().OrderBy(x => rnd.Next()).Take(this.fanout) select new BroadcastHistoryRow() { ContactPublicKey = r, SignedRequestPayloadId = signedrequestpayloadId, PublicKey = this.PublicKey });
        this.nodeContext.Value.TryAddObjectRange(retcontacts);
        if (originatorPublicKey != null)
            this.nodeContext.Value.TryAddObject(new BroadcastHistoryRow() { ContactPublicKey = originatorPublicKey, SignedRequestPayloadId = signedrequestpayloadId, PublicKey = this.PublicKey });

        return (from r in retcontacts select r.ContactPublicKey).ToList();
    }


    public async Task BroadcastAsync(Certificate<RequestPayloadValue> requestPayload,
                        string? originatorPublicKey = null,
                        OnionRoute? backwardOnion = null)
    {

        var tobroadcast = GetBroadcastContactList(requestPayload.Id, originatorPublicKey);
        if (tobroadcast.Count == 0)
        {
            await FlowLogger.TraceInformationAsync("already broadcasted");
            return;
        }

        foreach (var peerPublicKey in tobroadcast)
        {
            BroadcastFrame broadcastFrame = new BroadcastFrame()
            {
                SignedRequestPayload = requestPayload,
                BackwardOnion = (backwardOnion ?? new OnionRoute()).Grow(
                    this.PublicKey,
                    peerPublicKey.AsECXOnlyPubKey())
            };
            await SendMessageAsync(peerPublicKey, broadcastFrame, true);
            await FlowLogger.NewMessageAsync(this.PublicKey, peerPublicKey, "broadcast");
        }
    }

    public List<string> GetBroadcastCancelContactList(Guid signedrequestpayloadId)
    {
        var alreadyBroadcastCanceled = (from inc in this.nodeContext.Value.BroadcastCancelHistory where inc.PublicKey == this.PublicKey && inc.SignedRequestPayloadId == signedrequestpayloadId select inc.ContactPublicKey).ToList();
        var alreadyBroadcasted = new HashSet<string>((from inc in this.nodeContext.Value.BroadcastHistory where inc.PublicKey == this.PublicKey && inc.SignedRequestPayloadId == signedrequestpayloadId select inc.ContactPublicKey));
        alreadyBroadcasted.ExceptWith(alreadyBroadcastCanceled);
        this.nodeContext.Value.AddObjectRange((from r in alreadyBroadcasted select new BroadcastCancelHistoryRow() { ContactPublicKey = r, SignedRequestPayloadId = signedrequestpayloadId, PublicKey = this.PublicKey }));
        return alreadyBroadcasted.ToList();
    }

    public async Task CancelBroadcastAsync(Certificate<CancelRequestPayloadValue> cancelRequestPayload)
    {
        var tobroadcast = GetBroadcastCancelContactList(cancelRequestPayload.Id);
        foreach (var peerPublicKey in tobroadcast)
        {
            CancelBroadcastFrame cancelBroadcastFrame = new CancelBroadcastFrame()
            {
                SignedCancelRequestPayload = cancelRequestPayload
            };
            await this.SendMessageAsync(peerPublicKey, cancelBroadcastFrame, true);
        }
    }

    public async Task OnCancelBroadcastFrameAsync(string messageId, string peerPublicKey, CancelBroadcastFrame cancelBroadcastFrame)
    {
        if (!await cancelBroadcastFrame.SignedCancelRequestPayload.VerifyAsync(SettlerSelector))
            return;

        gigGossipNodeEvents.OnCancelBroadcast(this, peerPublicKey, cancelBroadcastFrame);
        await CancelBroadcastAsync(cancelBroadcastFrame.SignedCancelRequestPayload);
    }

    public async Task OnBroadcastFrameAsync(string messageId, string peerPublicKey, BroadcastFrame broadcastFrame)
    {
        if (broadcastFrame.SignedRequestPayload.Value.Timestamp > DateTime.UtcNow)
            return;

        if (broadcastFrame.SignedRequestPayload.Value.Timestamp + this.timestampTolerance < DateTime.UtcNow)
            return;

        if (!await broadcastFrame.SignedRequestPayload.VerifyAsync(SettlerSelector))
            return;

        gigGossipNodeEvents.OnAcceptBroadcast(this, peerPublicKey, broadcastFrame);
    }

    public async Task BroadcastToPeersAsync(string peerPublicKey, BroadcastFrame broadcastFrame)
    {
        await this.BroadcastAsync(
            requestPayload: broadcastFrame.SignedRequestPayload,
            originatorPublicKey: peerPublicKey,
            backwardOnion: broadcastFrame.BackwardOnion);
    }


    public async Task<AcceptBroadcastReturnValue> AcceptBroadcastAsync(string peerPublicKey, BroadcastFrame broadcastFrame, AcceptBroadcastResponse acceptBroadcastResponse)
    {
        ReplyFrame responseFrame;
        PayReq decodedReplyInvoice;
        Guid replierCertificateId;

        await alreadyBroadcastedSemaphore.WaitAsync();
        try
        {
            var alreadyBroadcasted = (from abx in this.nodeContext.Value.AcceptedBroadcasts
                                      where abx.PublicKey == this.PublicKey
                                      && abx.SignedRequestPayloadId == broadcastFrame.SignedRequestPayload.Id
                                      && abx.SettlerServiceUri == acceptBroadcastResponse.SettlerServiceUri
                                      select abx).FirstOrDefault();

            if (alreadyBroadcasted == null)
            {
                await FlowLogger.NewMessageAsync(this.PublicKey, Encoding.Default.GetBytes(acceptBroadcastResponse.SettlerServiceUri.AbsoluteUri).AsHex(), "getSecret");
                var settlerClient = this.SettlerSelector.GetSettlerClient(acceptBroadcastResponse.SettlerServiceUri);
                var authToken = await MakeSettlerAuthTokenAsync(acceptBroadcastResponse.SettlerServiceUri);
                var replyPaymentHash = SettlerAPIResult.Get<string>(await settlerClient.GenerateReplyPaymentPreimageAsync(authToken, broadcastFrame.SignedRequestPayload.Id.ToString(), this.PublicKey));
                var replyInvoice = WalletAPIResult.Get<InvoiceRet>(await GetWalletClient().AddHodlInvoiceAsync(await MakeWalletAuthToken(), acceptBroadcastResponse.Fee, replyPaymentHash, "", (long)InvoicePaymentTimeout.TotalSeconds)).PaymentRequest;
                await FlowLogger.NewMessageAsync(Encoding.Default.GetBytes(acceptBroadcastResponse.SettlerServiceUri.AbsoluteUri).AsHex(), replyPaymentHash, "hash");
                await FlowLogger.NewMessageAsync(this.PublicKey, replyPaymentHash, "create");
                decodedReplyInvoice = WalletAPIResult.Get<PayReq>(await GetWalletClient().DecodeInvoiceAsync(await MakeWalletAuthToken(), replyInvoice));
                await this._invoiceStateUpdatesMonitor.MonitorInvoiceAsync(
                    replyPaymentHash,
                    Crypto.SerializeObject(new InvoiceData()
                    {
                        IsNetworkInvoice = false,
                        Invoice = replyInvoice,
                        PaymentHash = decodedReplyInvoice.PaymentHash,
                        TotalSeconds = (int)InvoicePaymentTimeout.TotalSeconds
                    }));

                await this._settlerMonitor.MonitorPreimageAsync(
                    acceptBroadcastResponse.SettlerServiceUri,
                    replyPaymentHash);
                var signedRequestPayloadSerialized = Crypto.SerializeObject(broadcastFrame.SignedRequestPayload);
                var settr = SettlerAPIResult.Get<string>(await settlerClient.GenerateSettlementTrustAsync(authToken,
                    string.Join(",",acceptBroadcastResponse.Properties),
                    replyInvoice,
                    new FileParameter(new MemoryStream(acceptBroadcastResponse.Message)),
                    new FileParameter(new MemoryStream(signedRequestPayloadSerialized))));
                var settlementTrust = Crypto.DeserializeObject<SettlementTrust>(Convert.FromBase64String(settr));
                var signedSettlementPromise = settlementTrust.SettlementPromise;
                var networkInvoice = settlementTrust.NetworkInvoice;
                var decodedNetworkInvoice = WalletAPIResult.Get<PayReq>(await GetWalletClient().DecodeInvoiceAsync(await MakeWalletAuthToken(), networkInvoice));
                await FlowLogger.NewMessageAsync(Encoding.Default.GetBytes(acceptBroadcastResponse.SettlerServiceUri.AbsoluteUri).AsHex(), decodedNetworkInvoice.PaymentHash, "create");
                var encryptedReplyPayload = settlementTrust.EncryptedReplyPayload;

                replierCertificateId = settlementTrust.ReplierCertificateId;

                this.nodeContext.Value.AddObject(new AcceptedBroadcastRow()
                {
                    PublicKey = this.PublicKey,
                    SignedRequestPayloadId = broadcastFrame.SignedRequestPayload.Id,
                    SettlerServiceUri = acceptBroadcastResponse.SettlerServiceUri,
                    EncryptedReplyPayload = encryptedReplyPayload,
                    NetworkInvoice = networkInvoice,
                    SignedSettlementPromise = Crypto.SerializeObject(signedSettlementPromise),
                    ReplyInvoice = replyInvoice,
                    DecodedNetworkInvoice = Crypto.SerializeObject(decodedNetworkInvoice),
                    DecodedReplyInvoice = Crypto.SerializeObject(decodedReplyInvoice),
                    ReplyInvoiceHash = replyPaymentHash,
                    Cancelled = false,
                    ReplierCertificateId = settlementTrust.ReplierCertificateId,
                });

                await FlowLogger.NewMessageAsync(Encoding.Default.GetBytes(acceptBroadcastResponse.SettlerServiceUri.AbsoluteUri).AsHex(), broadcastFrame.SignedRequestPayload.Id.ToString() + "_" + this.PublicKey, "create");
                await FlowLogger.NewMessageAsync(this.PublicKey, broadcastFrame.SignedRequestPayload.Id.ToString() + "_" + this.PublicKey, "encrypts");

                responseFrame = new ReplyFrame()
                {
                    EncryptedReplyPayload = encryptedReplyPayload,
                    SignedSettlementPromise = signedSettlementPromise,
                    ForwardOnion = broadcastFrame.BackwardOnion,
                    NetworkInvoice = networkInvoice
                };
            }
            else
            {
                decodedReplyInvoice = Crypto.DeserializeObject<PayReq>(alreadyBroadcasted.DecodedReplyInvoice);
                replierCertificateId = alreadyBroadcasted.ReplierCertificateId;
                responseFrame = new ReplyFrame()
                {
                    EncryptedReplyPayload = alreadyBroadcasted.EncryptedReplyPayload,
                    SignedSettlementPromise = Crypto.DeserializeObject<SettlementPromise>(alreadyBroadcasted.SignedSettlementPromise),
                    ForwardOnion = broadcastFrame.BackwardOnion,
                    NetworkInvoice = alreadyBroadcasted.NetworkInvoice
                };
            }
        }
        finally
        {
            alreadyBroadcastedSemaphore.Release();
        }

        await this.OnResponseFrameAsync(null, peerPublicKey, responseFrame, newResponse: true);

        return new AcceptBroadcastReturnValue()
        {
            ReplierCertificateId = replierCertificateId,
            DecodedReplyInvoice = decodedReplyInvoice,
        };
    }

    public async Task OnResponseFrameAsync(string messageId, string peerPublicKey, ReplyFrame responseFrame, bool newResponse = false)
    {
        await alreadyBroadcastedSemaphore.WaitAsync();
        try
        {
            var decodedNetworkInvoice = WalletAPIResult.Get<PayReq>(await GetWalletClient().DecodeInvoiceAsync(await MakeWalletAuthToken(), responseFrame.NetworkInvoice));
            if (responseFrame.ForwardOnion.IsEmpty())
            {
                await FlowLogger.NewMessageAsync(peerPublicKey, this.PublicKey, "reply");
                var settlerPubKey = await SettlerSelector.GetPubKeyAsync(responseFrame.SignedSettlementPromise.RequestersServiceUri);
                var replyPayload = await responseFrame.DecryptAndVerifyAsync(privateKey, settlerPubKey, this.SettlerSelector);
                if (replyPayload == null)
                {
                    await FlowLogger.TraceErrorAsync("reply payload mismatch");
                    return;
                }
                await _settlerMonitor.MonitorGigStatusAsync(responseFrame.SignedSettlementPromise.ServiceUri, replyPayload.Value.SignedRequestPayload.Id, replyPayload.Id, Crypto.SerializeObject(replyPayload));

                var decodedReplyInvoice = WalletAPIResult.Get<PayReq>(await GetWalletClient().DecodeInvoiceAsync(await MakeWalletAuthToken(), replyPayload.Value.ReplyInvoice));

                await this._invoiceStateUpdatesMonitor.MonitorInvoiceAsync(
                    decodedReplyInvoice.PaymentHash,
                    Crypto.SerializeObject(new InvoiceData()
                    {
                        IsNetworkInvoice = false,
                        Invoice = replyPayload.Value.ReplyInvoice,
                        PaymentHash = decodedReplyInvoice.PaymentHash,
                        TotalSeconds = (int)InvoicePaymentTimeout.TotalSeconds
                    }));

                this.nodeContext.Value.AddObject(
                    new ReplyPayloadRow()
                    {
                        ReplyId = Guid.NewGuid(),
                        PublicKey = this.PublicKey,
                        SignedRequestPayloadId = replyPayload.Value.SignedRequestPayload.Id,
                        ReplierCertificateId = replyPayload.Id,
                        ReplyInvoice = replyPayload.Value.ReplyInvoice,
                        DecodedReplyInvoice = Crypto.SerializeObject(decodedReplyInvoice),
                        NetworkInvoice = responseFrame.NetworkInvoice,
                        DecodedNetworkInvoice = Crypto.SerializeObject(decodedNetworkInvoice),
                        TheReplyPayload = Crypto.SerializeObject(replyPayload)
                    });

                gigGossipNodeEvents.OnNewResponse(this, replyPayload, replyPayload.Value.ReplyInvoice, decodedReplyInvoice, responseFrame.NetworkInvoice, decodedNetworkInvoice);
            }
            else
            {
                var topLayerPublicKey = responseFrame.ForwardOnion.Peel(privateKey);
                if (!await responseFrame.SignedSettlementPromise.VerifyAsync(responseFrame.EncryptedReplyPayload, this.SettlerSelector))
                {
                    return;
                }

                if (!newResponse)
                {
                    await FlowLogger.NewReplyAsync(peerPublicKey, this.PublicKey, "reply");
                    var settlerClient = this.SettlerSelector.GetSettlerClient(responseFrame.SignedSettlementPromise.ServiceUri);
                    var settok = await MakeSettlerAuthTokenAsync(responseFrame.SignedSettlementPromise.ServiceUri);

                    if (!SettlerAPIResult.Get<bool>(await settlerClient.ValidateRelatedPaymentHashesAsync(settok,
                        responseFrame.SignedSettlementPromise.NetworkPaymentHash.AsHex(),
                        decodedNetworkInvoice.PaymentHash)))
                    {
                        return;
                    }

                    var relatedNetworkPaymentHash = SettlerAPIResult.Get<string>(await settlerClient.GenerateRelatedPreimageAsync(
                        settok,
                        decodedNetworkInvoice.PaymentHash));

                    var networkInvoice = WalletAPIResult.Get<InvoiceRet>(await GetWalletClient().AddHodlInvoiceAsync(
                        await this.MakeWalletAuthToken(),
                        decodedNetworkInvoice.NumSatoshis + this.priceAmountForRouting,
                        relatedNetworkPaymentHash, "", (long)InvoicePaymentTimeout.TotalSeconds));
                    await FlowLogger.NewMessageAsync(this.PublicKey, relatedNetworkPaymentHash, "create");
                    await this._invoiceStateUpdatesMonitor.MonitorInvoiceAsync(
                        relatedNetworkPaymentHash,
                        Crypto.SerializeObject(new InvoiceData()
                        {
                            IsNetworkInvoice = true,
                            Invoice = responseFrame.NetworkInvoice,
                            PaymentHash = decodedNetworkInvoice.PaymentHash,
                            TotalSeconds = (int)InvoicePaymentTimeout.TotalSeconds
                        }));
                    await this._settlerMonitor.MonitorPreimageAsync(
                        responseFrame.SignedSettlementPromise.ServiceUri,
                        relatedNetworkPaymentHash);
                    responseFrame = responseFrame.DeepCopy();
                    responseFrame.NetworkInvoice = networkInvoice.PaymentRequest;
                }
                await SendMessageAsync(topLayerPublicKey, responseFrame, false, DateTime.UtcNow + InvoicePaymentTimeout);
            }
        }
        finally
        {
            alreadyBroadcastedSemaphore.Release();
        }
    }

    public List<ReplyPayloadRow> GetReplyPayloads()
    {
        alreadyBroadcastedSemaphore.Wait();
        try
        {
            return (from rp in this.nodeContext.Value.ReplyPayloads where rp.PublicKey == this.PublicKey select rp).ToList();
        }
        finally
        {
            alreadyBroadcastedSemaphore.Release();
        }
    }

    public List<ReplyPayloadRow> GetReplyPayloads(Guid signedrequestpayloadId)
    {
        alreadyBroadcastedSemaphore.Wait();
        try
        {
            return (from rp in this.nodeContext.Value.ReplyPayloads where rp.PublicKey == this.PublicKey && rp.SignedRequestPayloadId == signedrequestpayloadId select rp).ToList();
        }
        finally
        {
            alreadyBroadcastedSemaphore.Release();
        }
    }

    public List<AcceptedBroadcastRow> GetAcceptedNotCancelledBroadcasts()
    {
        alreadyBroadcastedSemaphore.Wait();
        try
        {
            return (from rp in this.nodeContext.Value.AcceptedBroadcasts where rp.PublicKey == this.PublicKey && !rp.Cancelled select rp).ToList();
        }
        finally
        {
            alreadyBroadcastedSemaphore.Release();
        }
    }

    public void MarkBroadcastAsCancelled(AcceptedBroadcastRow brd)
    {
        alreadyBroadcastedSemaphore.Wait();
        try
        {
            brd.Cancelled = true;
            this.nodeContext.Value.SaveObject(brd);
        }
        finally
        {
            alreadyBroadcastedSemaphore.Release();
        }
    }


    public AcceptedBroadcastRow GetAcceptedBroadcastsByReqestPayloadId(Guid signedrequestpayloadId)
    {
        alreadyBroadcastedSemaphore.Wait();
        try
        {
            return (from rp in this.nodeContext.Value.AcceptedBroadcasts where rp.PublicKey == this.PublicKey && rp.SignedRequestPayloadId == signedrequestpayloadId select rp).FirstOrDefault();
        }
        finally
        {
            alreadyBroadcastedSemaphore.Release();
        }
    }

    public AcceptedBroadcastRow GetAcceptedBroadcastsByReplyInvoiceHash(string replyInvoiceHash)
    {
        alreadyBroadcastedSemaphore.Wait();
        try
        {
            return (from rp in this.nodeContext.Value.AcceptedBroadcasts where rp.PublicKey == this.PublicKey && rp.ReplyInvoiceHash == replyInvoiceHash select rp).FirstOrDefault();
        }
        finally
        {
            alreadyBroadcastedSemaphore.Release();
        }
    }

    public void OnInvoiceStateChange(string state, byte[] data)
    {
        var iac = Crypto.DeserializeObject<InvoiceData>(data);
        FlowLogger.NewNote(iac.PaymentHash, state);
        if (iac.IsNetworkInvoice)
        {
            if (state == "Accepted")
                this.gigGossipNodeEvents.OnNetworkInvoiceAccepted(this, iac);
            else if (state == "Cancelled")
                this.gigGossipNodeEvents.OnNetworkInvoiceCancelled(this, iac);
        }
        else
        {
            if (state == "Accepted")
                this.gigGossipNodeEvents.OnInvoiceAccepted(this, iac);
            else if (state == "Cancelled")
                this.gigGossipNodeEvents.OnInvoiceCancelled(this, iac);
        }
    }

    public async Task<GigLNDWalletAPIErrorCode> PayNetworkInvoiceAsync(InvoiceData iac)
    {
        if (_paymentStatusUpdatesMonitor.IsPaymentMonitored(iac.PaymentHash))
            return GigLNDWalletAPIErrorCode.AlreadyPayed;
        await _paymentStatusUpdatesMonitor.MonitorPaymentAsync(iac.PaymentHash, Crypto.SerializeObject(
            new PaymentData()
            {
                Invoice = iac.Invoice,
                PaymentHash = iac.PaymentHash
            }));
        var paymentStatus = WalletAPIResult.Status(await GetWalletClient().SendPaymentAsync(
            await MakeWalletAuthToken(), iac.Invoice, iac.TotalSeconds
            ));
        if (paymentStatus != GigLNDWalletAPIErrorCode.Ok)
        {
            await _paymentStatusUpdatesMonitor.StopPaymentMonitoringAsync(iac.PaymentHash);
        }
        return paymentStatus;
    }

    public async void OnPaymentStatusChange(string status, byte[] data)
    {
        var pay = Crypto.DeserializeObject<PaymentData>(data);
        this.gigGossipNodeEvents.OnPaymentStatusChange(this, status, pay);
        await FlowLogger.NewMessageAsync(this.PublicKey, pay.PaymentHash, "pay_" + status);
    }

    public async Task<bool> OnPreimageRevealedAsync(Uri serviceUri, string phash, string preimage)
    {
        try
        {
            WalletAPIResult.Check(await GetWalletClient().SettleInvoiceAsync(
                await MakeWalletAuthToken(),
                preimage
                ));
            await FlowLogger.NewMessageAsync(Encoding.Default.GetBytes(serviceUri.AbsoluteUri).AsHex(), phash, "revealed");
            await FlowLogger.NewMessageAsync(this.PublicKey, phash, "settled");
            gigGossipNodeEvents.OnInvoiceSettled(this, serviceUri, phash, preimage);
            return true; 
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    public void OnSymmetricKeyRevealed(byte[] data, string key)
    {
        var replyPayload = Crypto.DeserializeObject<Certificate<ReplyPayloadValue>>(data);
        gigGossipNodeEvents.OnResponseReady(this, replyPayload, key);
    }

    public void OnGigCancelled(byte[] data)
    {
        var replyPayload = Crypto.DeserializeObject<Certificate<ReplyPayloadValue>>(data);
        gigGossipNodeEvents.OnResponseCancelled(this, replyPayload);
    }

    public async Task<GigLNDWalletAPIErrorCode> AcceptResponseAsync(Certificate<ReplyPayloadValue> replyPayload, string replyInvoice, PayReq decodedReplyInvoice, string networkInvoice, PayReq decodedNetworkInvoice)
    {
        if (!_paymentStatusUpdatesMonitor.IsPaymentMonitored(decodedNetworkInvoice.PaymentHash))
        {
            await _paymentStatusUpdatesMonitor.MonitorPaymentAsync(decodedNetworkInvoice.PaymentHash, Crypto.SerializeObject(
            new PaymentData()
            {
                Invoice = networkInvoice,
                PaymentHash = decodedNetworkInvoice.PaymentHash
            }));
            var paymentStatus = WalletAPIResult.Status(await GetWalletClient().SendPaymentAsync(await MakeWalletAuthToken(), networkInvoice, (int)this.InvoicePaymentTimeout.TotalSeconds));
            if(paymentStatus!= GigLNDWalletAPIErrorCode.Ok)
            {
                await _paymentStatusUpdatesMonitor.StopPaymentMonitoringAsync(decodedNetworkInvoice.PaymentHash);
                return paymentStatus;
            }
        }

        if (!_paymentStatusUpdatesMonitor.IsPaymentMonitored(decodedReplyInvoice.PaymentHash))
        {
            await _paymentStatusUpdatesMonitor.MonitorPaymentAsync(decodedReplyInvoice.PaymentHash, Crypto.SerializeObject(
            new PaymentData()
            {
                Invoice = replyInvoice,
                PaymentHash = decodedReplyInvoice.PaymentHash
            }));
            var paymentStatus = WalletAPIResult.Status(await GetWalletClient().SendPaymentAsync(await MakeWalletAuthToken(), replyInvoice, (int)this.InvoicePaymentTimeout.TotalSeconds));
            if (paymentStatus != GigLNDWalletAPIErrorCode.Ok)
            {
                await _paymentStatusUpdatesMonitor.StopPaymentMonitoringAsync(decodedNetworkInvoice.PaymentHash);
                return paymentStatus;
            }
        }
        return GigLNDWalletAPIErrorCode.Ok;
    }

    private void LoadMessageLocks()
    {
        messageLocks = new ConcurrentDictionary<string, bool>(from m in this.nodeContext.Value.MessagesDone where m.PublicKey == this.PublicKey select KeyValuePair.Create(m.MessageId, true));
    }

    public override bool OpenMessage(string id)
    {
        return messageLocks.TryAdd(id, true);
    }

    public override void CommitMessage(string id)
    {
        this.nodeContext.Value.AddObject(new MessageDoneRow() { MessageId = id, PublicKey = this.PublicKey });
    }

    public override void AbortMessage(string id)
    {
        messageLocks.TryRemove(id, out _);
    }

    public override async Task OnMessageAsync(string messageId, bool isNew, string senderPublicKey, object frame)
    {
        if (frame is BroadcastFrame)
        {
            await OnBroadcastFrameAsync(messageId, senderPublicKey, (BroadcastFrame)frame);
        }
        else if (frame is CancelBroadcastFrame)
        {
            await OnCancelBroadcastFrameAsync(messageId, senderPublicKey, (CancelBroadcastFrame)frame);
        }
        else if (frame is ReplyFrame)
        {
            await OnResponseFrameAsync(messageId, senderPublicKey, (ReplyFrame)frame);
        }
        else
        {
            throw new GigGossipException(GigGossipNodeErrorCode.FrameTypeNotRegistered);
        }

    }

    public async Task<BroadcastTopicResponse> BroadcastTopicAsync<T>(T topic, string[] properties)
    {
        var settler = SettlerSelector.GetSettlerClient(mySettlerUri);
        var token = await MakeSettlerAuthTokenAsync(mySettlerUri);
        var topicByte = Crypto.SerializeObject(topic!);
        var response = SettlerAPIResult.Get<string>(await settler.GenerateRequestPayloadAsync(token, string.Join(",",properties), new FileParameter(new MemoryStream(topicByte))));
        var base64Response = Convert.FromBase64String(response);
        var broadcastTopicResponse = Crypto.DeserializeObject<BroadcastTopicResponse>(base64Response);

        await BroadcastAsync(broadcastTopicResponse!.SignedRequestPayload);

        return broadcastTopicResponse;
    }


}
