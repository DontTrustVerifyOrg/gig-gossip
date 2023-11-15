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
    public void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, BroadcastFrame broadcastFrame);
    public void OnCancelBroadcast(GigGossipNode me, string peerPublicKey, CancelBroadcastFrame broadcastFrame);
    public void OnNetworkInvoiceAccepted(GigGossipNode me, InvoiceData iac);
    public void OnNetworkInvoiceCancelled(GigGossipNode me, InvoiceData iac);
    public void OnInvoiceAccepted(GigGossipNode me, InvoiceData iac);
    public void OnInvoiceCancelled(GigGossipNode me, InvoiceData iac);
    public void OnInvoiceSettled(GigGossipNode me, Uri serviceUri, string paymentHash, string preimage);
    public void OnPaymentStatusChange(GigGossipNode me, string status, PaymentData paydata);
}

public class AcceptBroadcastResponse
{
    public required Uri SettlerServiceUri { get; set; }
    public required string [] Properties { get; set; }
    public required byte[] Message { get; set; }
    public required long Fee { get; set; }
}

public class GigGossipNode : NostrNode, ILNDWalletMonitorEvents, ISettlerMonitorEvents
{
    protected long priceAmountForRouting;
    protected TimeSpan broadcastConditionsTimeout;
    protected string broadcastConditionsPowScheme;
    protected int broadcastConditionsPowComplexity;
    protected TimeSpan timestampTolerance;
    protected TimeSpan invoicePaymentTimeout;
    protected int fanout;
    private SemaphoreSlim alreadyBroadcastedSemaphore = new SemaphoreSlim(1, 1);

    public GigLNDWalletAPIClient.swaggerClient LNDWalletClient;
    private ConcurrentDictionary<Uri, SymmetricKeyRevealClient> settlerSymmetricKeyRevelClients = new();
    private ConcurrentDictionary<Uri, PreimageRevealClient> settlerPreimageRevelClients = new();
    protected Guid _walletToken;
    protected ConcurrentDictionary<Uri, Guid> _settlerToken;

    public ISettlerSelector SettlerSelector;
    protected LNDWalletMonitor _lndWalletMonitor;
    protected SettlerMonitor _settlerMonitor;

    private IGigGossipNodeEvents gigGossipNodeEvents;

    internal ThreadLocal<GigGossipNodeContext> nodeContext;

    public InvoiceStateUpdatesClient InvoiceStateUpdatesClient;
    public PaymentStatusUpdatesClient PaymentStatusUpdatesClient;

    public GigGossipNode(string connectionString, ECPrivKey privKey, int chunkSize, bool deleteDb = false) : base(privKey, chunkSize)
    {
        RegisterFrameType<BroadcastFrame>();
        RegisterFrameType<CancelBroadcastFrame>();
        RegisterFrameType<ReplyFrame>();

        this.nodeContext = new ThreadLocal<GigGossipNodeContext>(() => new GigGossipNodeContext(connectionString.Replace("$HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))));
        if (deleteDb)
            nodeContext.Value.Database.EnsureDeleted();
        nodeContext.Value.Database.EnsureCreated();
    }

    public void Init(int fanout, long priceAmountForRouting, TimeSpan broadcastConditionsTimeout, string broadcastConditionsPowScheme,
                           int broadcastConditionsPowComplexity, TimeSpan timestampTolerance, TimeSpan invoicePaymentTimeout,
                           GigLNDWalletAPIClient.swaggerClient lndWalletClient, HttpClient? httpClient = null)
    {
        this.fanout = fanout;
        this.priceAmountForRouting = priceAmountForRouting;
        this.broadcastConditionsTimeout = broadcastConditionsTimeout;
        this.broadcastConditionsPowScheme = broadcastConditionsPowScheme;
        this.broadcastConditionsPowComplexity = broadcastConditionsPowComplexity;
        this.timestampTolerance = timestampTolerance;
        this.invoicePaymentTimeout = invoicePaymentTimeout;

        LNDWalletClient = lndWalletClient;

        SettlerSelector = new SimpleSettlerSelector(httpClient);

        _lndWalletMonitor = new LNDWalletMonitor(this);
        _settlerMonitor = new SettlerMonitor(this);

        LoadContactList();
    }

    public async Task StartAsync(string[] nostrRelays, IGigGossipNodeEvents gigGossipNodeEvents, HttpMessageHandler? httpMessageHandler = null)
    {
        this.gigGossipNodeEvents = gigGossipNodeEvents;

        _walletToken = await LNDWalletClient.GetTokenAsync(this.PublicKey);
        var token = MakeWalletAuthToken();

        InvoiceStateUpdatesClient = new InvoiceStateUpdatesClient(this.LNDWalletClient, httpMessageHandler);
        await InvoiceStateUpdatesClient.ConnectAsync(token);

        PaymentStatusUpdatesClient = new PaymentStatusUpdatesClient(this.LNDWalletClient, httpMessageHandler);
        await PaymentStatusUpdatesClient.ConnectAsync(token);

        await _lndWalletMonitor.StartAsync();

        _settlerToken = new();
        await _settlerMonitor.StartAsync();

        await base.StartAsync(nostrRelays);
    }

    public override void Stop()
    {
        base.Stop();
        this._lndWalletMonitor.Stop();
        this._settlerMonitor.Stop();
    }


    Dictionary<string, NostrContact> _contactList = new();

    public void ClearContacts()
    {
        lock (_contactList)
        {
            this.nodeContext.Value.DeleteObjectRange(
            from c in this.nodeContext.Value.NostrContacts where c.PublicKey == this.PublicKey select c);
            _contactList.Clear();
        }
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

    public void LoadContactList()
    {
        lock (_contactList)
        {
            var mycontacts = (from c in this.nodeContext.Value.NostrContacts where c.PublicKey == this.PublicKey select c);
            foreach (var c in mycontacts)
                _contactList[c.ContactPublicKey] = c;
        }
    }

    public List<string> GetContacts()
    {
        lock (_contactList)
        {
            return _contactList.Keys.ToList();
        }
    }

    public override void OnContactList(string eventId, Dictionary<string, NostrContact> contactList)
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
                }
            }
            if (toadd.Count > 0)
                this.nodeContext.Value.AddObjectRange(toadd);
        }
    }

    public string MakeWalletAuthToken()
    {
        return Crypto.MakeSignedTimedToken(this.privateKey, DateTime.UtcNow, this._walletToken);
    }

    public async Task<string> MakeSettlerAuthTokenAsync(Uri serviceUri)
    {
        return Crypto.MakeSignedTimedToken(
            this.privateKey, DateTime.UtcNow,
            await _settlerToken.GetOrAddAsync(serviceUri, async (serviceUri) => await SettlerSelector.GetSettlerClient(serviceUri).GetTokenAsync(this.PublicKey)));
    }

    public async Task<SymmetricKeyRevealClient> GetSymmetricKeyRevealClientAsync(Uri serviceUri)
    {
        return await settlerSymmetricKeyRevelClients.GetOrAddAsync(
            serviceUri,
            async (serviceUri) =>
            {
                var newClient = new SymmetricKeyRevealClient(SettlerSelector.GetSettlerClient(serviceUri));
                await newClient.ConnectAsync(await MakeSettlerAuthTokenAsync(serviceUri));
                return newClient;
            });
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

    public List<string> GetBroadcastContactList(Guid payloadId, string? originatorPublicKey)
    {
        var rnd = new Random();
        var contacts = new HashSet<string>(GetContacts());
        var alreadyBroadcasted = (from inc in this.nodeContext.Value.BroadcastHistory where inc.PublicKey == this.PublicKey && inc.PayloadId == payloadId select inc.ContactPublicKey).ToList();
        contacts.ExceptWith(alreadyBroadcasted);
        if (originatorPublicKey != null)
            contacts.ExceptWith(new string[] { originatorPublicKey });
        if (contacts.Count == 0)
            return new List<string>();

        var retcontacts = (from r in contacts.AsEnumerable().OrderBy(x => rnd.Next()).Take(this.fanout) select new BroadcastHistoryRow() { ContactPublicKey = r, PayloadId = payloadId, PublicKey = this.PublicKey });
        this.nodeContext.Value.AddObjectRange(retcontacts);
        if (originatorPublicKey != null)
            this.nodeContext.Value.AddObject(new BroadcastHistoryRow() { ContactPublicKey = originatorPublicKey, PayloadId = payloadId, PublicKey = this.PublicKey });

        return (from r in retcontacts select r.ContactPublicKey).ToList();
    }


    public async Task BroadcastAsync(Certificate<RequestPayloadValue> requestPayload,
                        string? originatorPublicKey = null,
                        OnionRoute? backwardOnion = null)
    {

        var tobroadcast = GetBroadcastContactList(requestPayload.Value.PayloadId, originatorPublicKey);
        if (tobroadcast.Count == 0)
        {
            Trace.TraceInformation("already broadcasted");
            return;
        }

        foreach (var peerPublicKey in tobroadcast)
        {
            BroadcastFrame powBroadcastFrame = new BroadcastFrame()
            {
                SignedRequestPayload = requestPayload,
                BackwardOnion = (backwardOnion ?? new OnionRoute()).Grow(
                    this.PublicKey,
                    peerPublicKey.AsECXOnlyPubKey())
            };
            await SendMessageAsync(peerPublicKey, powBroadcastFrame, true);
            FlowLogger.NewMessage(this.PublicKey, peerPublicKey, "broadcast");
        }
    }

    public List<string> GetBroadcastCancelContactList(Guid payloadId)
    {
        var alreadyBroadcastCanceled = (from inc in this.nodeContext.Value.BroadcastCancelHistory where inc.PublicKey == this.PublicKey && inc.PayloadId == payloadId select inc.ContactPublicKey).ToList();
        var alreadyBroadcasted = new HashSet<string>((from inc in this.nodeContext.Value.BroadcastHistory where inc.PublicKey == this.PublicKey && inc.PayloadId == payloadId select inc.ContactPublicKey));
        alreadyBroadcasted.ExceptWith(alreadyBroadcastCanceled);
        this.nodeContext.Value.AddObjectRange((from r in alreadyBroadcasted select new BroadcastCancelHistoryRow() { ContactPublicKey = r, PayloadId = payloadId, PublicKey = this.PublicKey }));
        return alreadyBroadcasted.ToList();
    }

    public async Task CancelBroadcastAsync(Certificate<CancelRequestPayloadValue> cancelRequestPayload)
    {
        var tobroadcast = GetBroadcastCancelContactList(cancelRequestPayload.Value.PayloadId);
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
        {
            MarkMessageAsDone(messageId);
            return;
        }

        gigGossipNodeEvents.OnCancelBroadcast(this, peerPublicKey, cancelBroadcastFrame);
        await CancelBroadcastAsync(cancelBroadcastFrame.SignedCancelRequestPayload);

        MarkMessageAsDone(messageId);
    }

    public async Task OnPOWBroadcastFrameAsync(string messageId, string peerPublicKey, BroadcastFrame powBroadcastFrame)
    {
        if (powBroadcastFrame.SignedRequestPayload.Value.Timestamp > DateTime.UtcNow)
        {
            MarkMessageAsDone(messageId);
            return;
        }

        if (powBroadcastFrame.SignedRequestPayload.Value.Timestamp + this.timestampTolerance < DateTime.UtcNow)
        {
            MarkMessageAsDone(messageId);
            return;
        }

        if (!await powBroadcastFrame.SignedRequestPayload.VerifyAsync(SettlerSelector))
        {
            MarkMessageAsDone(messageId);
            return;
        }

        gigGossipNodeEvents.OnAcceptBroadcast(this, peerPublicKey, powBroadcastFrame);
        MarkMessageAsDone(messageId);
    }

    public async Task BroadcastToPeersAsync(string peerPublicKey, BroadcastFrame powBroadcastFrame)
    {
        await this.BroadcastAsync(
            requestPayload: powBroadcastFrame.SignedRequestPayload,
            originatorPublicKey: peerPublicKey,
            backwardOnion: powBroadcastFrame.BackwardOnion);
    }

    public async Task AcceptBroadcastAsync(string peerPublicKey, BroadcastFrame powBroadcastFrame, AcceptBroadcastResponse acceptBroadcastResponse)
    {
        ReplyFrame responseFrame;
        await alreadyBroadcastedSemaphore.WaitAsync();
        try
        {
            var alreadyBroadcasted = (from abx in this.nodeContext.Value.AcceptedBroadcasts
                                      where abx.PublicKey == this.PublicKey
                                      && abx.PayloadId == powBroadcastFrame.SignedRequestPayload.Value.PayloadId
                                      && abx.SettlerServiceUri == acceptBroadcastResponse.SettlerServiceUri
                                      select abx).FirstOrDefault();

            if (alreadyBroadcasted == null)
            {
                FlowLogger.NewMessage(this.PublicKey, Encoding.Default.GetBytes(acceptBroadcastResponse.SettlerServiceUri.AbsoluteUri).AsHex(), "getSecret");
                var settlerClient = this.SettlerSelector.GetSettlerClient(acceptBroadcastResponse.SettlerServiceUri);
                var authToken = await MakeSettlerAuthTokenAsync(acceptBroadcastResponse.SettlerServiceUri);
                var replyPaymentHash = await settlerClient.GenerateReplyPaymentPreimageAsync(authToken, powBroadcastFrame.SignedRequestPayload.Value.PayloadId.ToString(), this.PublicKey);
                var replyInvoice = (await LNDWalletClient.AddHodlInvoiceAsync(MakeWalletAuthToken(), acceptBroadcastResponse.Fee, replyPaymentHash, "", (long)invoicePaymentTimeout.TotalSeconds)).PaymentRequest;
                FlowLogger.SetupParticipantWithAutoAlias(replyPaymentHash, "I", false);
                FlowLogger.NewMessage(Encoding.Default.GetBytes(acceptBroadcastResponse.SettlerServiceUri.AbsoluteUri).AsHex(), replyPaymentHash, "hash");
                FlowLogger.NewMessage(this.PublicKey, replyPaymentHash, "create");
                await this._settlerMonitor.MonitorPreimageAsync(
                    acceptBroadcastResponse.SettlerServiceUri,
                    replyPaymentHash);
                var signedRequestPayloadSerialized = Crypto.SerializeObject(powBroadcastFrame.SignedRequestPayload);
                var settr = await settlerClient.GenerateSettlementTrustAsync(authToken, acceptBroadcastResponse.Properties, Convert.ToBase64String(acceptBroadcastResponse.Message), replyInvoice, Convert.ToBase64String(signedRequestPayloadSerialized));
                var settlementTrust = Crypto.DeserializeObject<SettlementTrust>(Convert.FromBase64String(settr));

                var signedSettlementPromise = settlementTrust.SettlementPromise;
                var networkInvoice = settlementTrust.NetworkInvoice;
                var decodedNetworkInvoice = await LNDWalletClient.DecodeInvoiceAsync(MakeWalletAuthToken(), networkInvoice);
                FlowLogger.SetupParticipantWithAutoAlias(decodedNetworkInvoice.PaymentHash, "I", false);
                FlowLogger.NewMessage(Encoding.Default.GetBytes(acceptBroadcastResponse.SettlerServiceUri.AbsoluteUri).AsHex(), decodedNetworkInvoice.PaymentHash, "create");
                var encryptedReplyPayload = settlementTrust.EncryptedReplyPayload;

                this.nodeContext.Value.AddObject(new AcceptedBroadcastRow()
                {
                    PublicKey = this.PublicKey,
                    PayloadId = powBroadcastFrame.SignedRequestPayload.Value.PayloadId,
                    SettlerServiceUri = acceptBroadcastResponse.SettlerServiceUri,
                    EncryptedReplyPayload = encryptedReplyPayload,
                    NetworkInvoice = networkInvoice,
                    SignedSettlementPromise = Crypto.SerializeObject(signedSettlementPromise)
                });

                FlowLogger.SetupParticipantWithAutoAlias(powBroadcastFrame.SignedRequestPayload.Value.PayloadId.ToString() + "_" + this.PublicKey, "K", false);
                FlowLogger.NewMessage(Encoding.Default.GetBytes(acceptBroadcastResponse.SettlerServiceUri.AbsoluteUri).AsHex(), powBroadcastFrame.SignedRequestPayload.Value.PayloadId.ToString() + "_" + this.PublicKey, "create");
                FlowLogger.NewMessage(this.PublicKey, powBroadcastFrame.SignedRequestPayload.Value.PayloadId.ToString() + "_" + this.PublicKey, "encrypts");

                responseFrame = new ReplyFrame()
                {
                    EncryptedReplyPayload = encryptedReplyPayload,
                    SignedSettlementPromise = signedSettlementPromise,
                    ForwardOnion = powBroadcastFrame.BackwardOnion,
                    NetworkInvoice = networkInvoice
                };
            }
            else
            {
                responseFrame = new ReplyFrame()
                {
                    EncryptedReplyPayload = alreadyBroadcasted.EncryptedReplyPayload,
                    SignedSettlementPromise = Crypto.DeserializeObject<SettlementPromise>(alreadyBroadcasted.SignedSettlementPromise),
                    ForwardOnion = powBroadcastFrame.BackwardOnion,
                    NetworkInvoice = alreadyBroadcasted.NetworkInvoice
                };
            }
        }
        finally
        {
            alreadyBroadcastedSemaphore.Release();
        }

        await this.OnResponseFrameAsync(null, peerPublicKey, responseFrame, newResponse: true);
    }

    public async Task OnResponseFrameAsync(string messageId, string peerPublicKey, ReplyFrame responseFrame, bool newResponse = false)
    {

        var decodedNetworkInvoice = await LNDWalletClient.DecodeInvoiceAsync(MakeWalletAuthToken(), responseFrame.NetworkInvoice);
        if (responseFrame.ForwardOnion.IsEmpty())
        {
            FlowLogger.NewMessage(peerPublicKey, this.PublicKey, "reply");
            var settlerPubKey = await SettlerSelector.GetPubKeyAsync(responseFrame.SignedSettlementPromise.RequestersServiceUri);
            var replyPayload = await responseFrame.DecryptAndVerifyAsync(privateKey, settlerPubKey, this.SettlerSelector);
            if (replyPayload == null)
            {
                Trace.TraceError("reply payload mismatch");
                if (messageId != null) MarkMessageAsDone(messageId);
                return;
            }
            var payloadId = replyPayload.Value.SignedRequestPayload.Value.PayloadId;

            await _settlerMonitor.MonitorSymmetricKeyAsync(responseFrame.SignedSettlementPromise.ServiceUri, replyPayload.Value.SignedRequestPayload.Id, payloadId, replyPayload.Id, Crypto.SerializeObject(replyPayload));

            var decodedReplyInvoice = await LNDWalletClient.DecodeInvoiceAsync(MakeWalletAuthToken(), replyPayload.Value.ReplyInvoice);

            await this._lndWalletMonitor.MonitorInvoiceAsync(
                decodedReplyInvoice.PaymentHash,
                Crypto.SerializeObject(new InvoiceData()
                {
                    IsNetworkInvoice = false,
                    Invoice = replyPayload.Value.ReplyInvoice,
                    PaymentHash = decodedReplyInvoice.PaymentHash,
                    TotalSeconds = (int)invoicePaymentTimeout.TotalSeconds
                }));

            this.nodeContext.Value.AddObject(
                new ReplyPayloadRow()
                {
                    ReplyId = Guid.NewGuid(),
                    PublicKey = this.PublicKey,
                    PayloadId = payloadId,
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
                if (messageId != null) MarkMessageAsDone(messageId);
                return;
            }
            if (!newResponse)
            {
                FlowLogger.NewReply(peerPublicKey, this.PublicKey, "reply");
                var settlerClient = this.SettlerSelector.GetSettlerClient(responseFrame.SignedSettlementPromise.ServiceUri);
                var settok = await MakeSettlerAuthTokenAsync(responseFrame.SignedSettlementPromise.ServiceUri);

                if (!await settlerClient.ValidateRelatedPaymentHashesAsync(settok,
                    responseFrame.SignedSettlementPromise.NetworkPaymentHash.AsHex(),
                    decodedNetworkInvoice.PaymentHash))
                {
                    if (messageId != null) MarkMessageAsDone(messageId);
                    return;
                }

                var relatedNetworkPaymentHash = await settlerClient.GenerateRelatedPreimageAsync(
                    settok,
                    decodedNetworkInvoice.PaymentHash);

                var networkInvoice = await LNDWalletClient.AddHodlInvoiceAsync(
                    this.MakeWalletAuthToken(),
                    decodedNetworkInvoice.NumSatoshis + this.priceAmountForRouting,
                    relatedNetworkPaymentHash, "", (long)invoicePaymentTimeout.TotalSeconds);
                FlowLogger.SetupParticipantWithAutoAlias(relatedNetworkPaymentHash, "I", false);
                FlowLogger.NewMessage(this.PublicKey, relatedNetworkPaymentHash, "create");
                await this._lndWalletMonitor.MonitorInvoiceAsync(
                    relatedNetworkPaymentHash,
                    Crypto.SerializeObject(new InvoiceData()
                    {
                        IsNetworkInvoice = true,
                        Invoice = responseFrame.NetworkInvoice,
                        PaymentHash = decodedNetworkInvoice.PaymentHash,
                        TotalSeconds = (int)invoicePaymentTimeout.TotalSeconds
                    }));
                await this._settlerMonitor.MonitorPreimageAsync(
                    responseFrame.SignedSettlementPromise.ServiceUri,
                    relatedNetworkPaymentHash);
                responseFrame = responseFrame.DeepCopy();
                responseFrame.NetworkInvoice = networkInvoice.PaymentRequest;
            }
            await SendMessageAsync(topLayerPublicKey, responseFrame, false, DateTime.UtcNow + invoicePaymentTimeout);
        }
        if (messageId != null) MarkMessageAsDone(messageId);

    }

    public IQueryable<ReplyPayloadRow> GetReplyPayloads(Guid payloadId)
    {
        return (from rp in this.nodeContext.Value.ReplyPayloads where rp.PublicKey == this.PublicKey && rp.PayloadId == payloadId select rp);
    }

    public IQueryable<AcceptedBroadcastRow> GetAcceptedBroadcasts(Guid payloadId)
    {
        return (from rp in this.nodeContext.Value.AcceptedBroadcasts where rp.PublicKey == this.PublicKey && rp.PayloadId == payloadId select rp);
    }


    public void OnInvoiceStateChange(string state, byte[] data)
    {
        var iac = Crypto.DeserializeObject<InvoiceData>(data);
        FlowLogger.NewEvent(iac.PaymentHash, state);
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

    public async Task PayNetworkInvoiceAsync(InvoiceData iac)
    {
        if (_lndWalletMonitor.IsPaymentMonitored(iac.PaymentHash))
            return;
        await _lndWalletMonitor.MonitorPaymentAsync(iac.PaymentHash, Crypto.SerializeObject(
            new PaymentData()
            {
                Invoice = iac.Invoice,
                PaymentHash = iac.PaymentHash
            }));
        await LNDWalletClient.SendPaymentAsync(
            MakeWalletAuthToken(), iac.Invoice, iac.TotalSeconds
            );
    }

    public void OnPaymentStatusChange(string status, byte[] data)
    {
        var pay = Crypto.DeserializeObject<PaymentData>(data);
        this.gigGossipNodeEvents.OnPaymentStatusChange(this, status, pay);
        FlowLogger.NewMessage(this.PublicKey, pay.PaymentHash, "pay_" + status);
    }

    public async Task OnPreimageRevealedAsync(Uri serviceUri, string phash, string preimage)
    {
        try
        {
            await LNDWalletClient.SettleInvoiceAsync(
                MakeWalletAuthToken(),
                preimage
                );
            FlowLogger.NewMessage(Encoding.Default.GetBytes(serviceUri.AbsoluteUri).AsHex(), phash, "revealed");
            FlowLogger.NewMessage(this.PublicKey, phash, "settled");
            gigGossipNodeEvents.OnInvoiceSettled(this, serviceUri, phash, preimage);
        }
        catch (Exception ex)
        {//invoice was not accepted or was cancelled
         //            Trace.TraceError(ex.ToString());
            Trace.TraceInformation("Invoice cannot be settled");
        }
    }

    public void OnSymmetricKeyRevealed(byte[] data, string key)
    {
        var replyPayload = Crypto.DeserializeObject<Certificate<ReplyPayloadValue>>(data);
        gigGossipNodeEvents.OnResponseReady(this, replyPayload, key);
    }

    public async Task AcceptResponseAsync(Certificate<ReplyPayloadValue> replyPayload, string replyInvoice, PayReq decodedReplyInvoice, string networkInvoice, PayReq decodedNetworkInvoice)
    {
        if (!_lndWalletMonitor.IsPaymentMonitored(decodedNetworkInvoice.PaymentHash))
        {
            await _lndWalletMonitor.MonitorPaymentAsync(decodedNetworkInvoice.PaymentHash, Crypto.SerializeObject(
            new PaymentData()
            {
                Invoice = networkInvoice,
                PaymentHash = decodedNetworkInvoice.PaymentHash
            }));
            await LNDWalletClient.SendPaymentAsync(MakeWalletAuthToken(), networkInvoice, (int)this.invoicePaymentTimeout.TotalSeconds);
        }

        if (!_lndWalletMonitor.IsPaymentMonitored(decodedReplyInvoice.PaymentHash))
        {
            await _lndWalletMonitor.MonitorPaymentAsync(decodedReplyInvoice.PaymentHash, Crypto.SerializeObject(
            new PaymentData()
            {
                Invoice = replyInvoice,
                PaymentHash = decodedReplyInvoice.PaymentHash
            }));
            await LNDWalletClient.SendPaymentAsync(MakeWalletAuthToken(), replyInvoice, (int)this.invoicePaymentTimeout.TotalSeconds);
        }
    }

    public bool IsMessageDone(string messageId)
    {
        return (from m in this.nodeContext.Value.MessagesDone where m.MessageId == messageId && m.PublicKey == this.PublicKey select m).FirstOrDefault() != null;
    }

    public void MarkMessageAsDone(string messageId)
    {
        this.nodeContext.Value.AddObject(new MessageDoneRow() { MessageId = messageId, PublicKey = this.PublicKey });
    }

    public override async Task OnMessageAsync(string messageId, string senderPublicKey, object frame)
    {
        if (IsMessageDone(messageId))
            return; //Already Processed

        if (frame is BroadcastFrame)
        {
            await OnPOWBroadcastFrameAsync(messageId, senderPublicKey, (BroadcastFrame)frame);
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

    public async Task<BroadcastTopicResponse> BroadcastTopicAsync<T>(T topic, Uri mysettlerUri, string[] properties)
    {
        var t = Crypto.DeserializeObject<BroadcastTopicResponse>(
            Convert.FromBase64String(
                await this.SettlerSelector.GetSettlerClient(mysettlerUri)
                    .GenerateRequestPayloadAsync(await MakeSettlerAuthTokenAsync(mysettlerUri),
                        properties, Convert.ToBase64String(Crypto.SerializeObject(topic)))));
        await this.BroadcastAsync(t.SignedRequestPayload);
        return t;
    }

}
