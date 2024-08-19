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
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR.Client;
using NetworkClientToolkit;
using GoogleApi;
using ProtoBuf;
using System.Text.Json;

[ProtoContract]
public class InvoiceData : IProtoFrame
{
    [ProtoMember(1)]
    public required bool IsNetworkInvoice { get; set; }
    [ProtoMember(2)]
    public required string Invoice { get; set; }
    [ProtoMember(3)]
    public required string PaymentHash { get; set; }
    [ProtoMember(4)]
    public required int TotalSeconds { get; set; }
}

[ProtoContract]
public class PaymentData : IProtoFrame
{
    [ProtoMember(1)]
    public required string Invoice { get; set; }
    [ProtoMember(2)]
    public required string PaymentHash { get; set; }
}

public enum ServerConnectionSource
{
    NostrRelay = 0,
    SettlerAPI = 1,
    WalletAPI = 2,
}

public interface IGigGossipNodeEvents
{
    public void OnNewResponse(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload, string replyInvoice, PayReqRet decodedReplyInvoice, string networkInvoice, PayReqRet decodedNetworkInvoice);
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

    public void OnServerConnectionState(GigGossipNode me, ServerConnectionSource source, ServerConnectionState state, Uri uri);
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
    public required PayReqRet DecodedReplyInvoice { get; set; }
    public required string ReplyInvoiceHash { get; set; }
}


public class GigGossipNode : NostrNode, IInvoiceStateUpdatesMonitorEvents, IPaymentStatusUpdatesMonitorEvents, ISettlerMonitorEvents
{
    protected long priceAmountForRouting;
    protected TimeSpan timestampTolerance;
    public TimeSpan InvoicePaymentTimeout;
    protected int fanout;
    public string Settings;

    private SemaphoreSlim alreadyBroadcastedSemaphore = new SemaphoreSlim(1, 1);

    private ConcurrentDictionary<Uri, IGigStatusClient> settlerGigStatusClients ;
    private ConcurrentDictionary<Uri, IPreimageRevealClient> settlerPreimageRevelClients ;

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
    private Func<HttpClient> httpClientFactory;

    internal ThreadLocal<GigGossipNodeContext> nodeContext;

    ConcurrentDictionary<string, bool> messageLocks;
    public GigDebugLoggerAPIClient.LogWrapper<GigGossipNode> TRACE = GigDebugLoggerAPIClient.FlowLoggerFactory.Trace<GigGossipNode>();

    public GigGossipNode(string connectionString, ECPrivKey privKey, int chunkSize, IRetryPolicy retryPolicy, Func<HttpClient> httpClientFactory, bool traceEnabled, Uri myLoggerUri) : base(privKey, chunkSize,true, retryPolicy)
    {
        RegisterFrameType<BroadcastFrame>();
        RegisterFrameType<CancelBroadcastFrame>();
        RegisterFrameType<ReplyFrame>();

        this.OnServerConnectionState += GigGossipNode_OnServerConnectionState;

        this.nodeContext = new ThreadLocal<GigGossipNodeContext>(() => new GigGossipNodeContext(connectionString.Replace("$HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))));
        nodeContext.Value.Database.EnsureCreated();

        this.httpClientFactory = httpClientFactory;

        WalletSelector = new SimpleGigLNDWalletSelector(httpClientFactory, RetryPolicy);
        SettlerSelector = new SimpleSettlerSelector(httpClientFactory, RetryPolicy);
        _settlerToken = new();

    }

    public async Task<string> MakeWalletAuthToken()
    {
        return Crypto.MakeSignedTimedToken(
            this.privateKey, DateTime.UtcNow,
            await _walletToken.GetOrAddAsync(defaultWalletUri, async (serviceUri) => WalletAPIResult.Get<Guid>(await WalletSelector.GetWalletClient(serviceUri).GetTokenAsync(this.PublicKey,CancellationTokenSource.Token))));
    }

    public async Task<string> MakeSettlerAuthTokenAsync(Uri serviceUri)
    {
        return Crypto.MakeSignedTimedToken(
            this.privateKey, DateTime.UtcNow,
            await _settlerToken.GetOrAddAsync(serviceUri, async (serviceUri) => SettlerAPIResult.Get<Guid>(await SettlerSelector.GetSettlerClient(serviceUri).GetTokenAsync(this.PublicKey,CancellationTokenSource.Token))));
    }

    public async Task StartAsync(int fanout, long priceAmountForRouting, TimeSpan timestampTolerance, TimeSpan invoicePaymentTimeout,
        string[] nostrRelays, IGigGossipNodeEvents gigGossipNodeEvents, Uri defaultWalletUri,  Uri mySettlerUri)
    {
        using var TL = TRACE.Log().Args(fanout, priceAmountForRouting, timestampTolerance, invoicePaymentTimeout, nostrRelays, gigGossipNodeEvents, defaultWalletUri,  mySettlerUri);
        try
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

            this.gigGossipNodeEvents = gigGossipNodeEvents;

            await base.StartAsync(nostrRelays);

            _invoiceStateUpdatesMonitor = new InvoiceStateUpdatesMonitor(this);
            _paymentStatusUpdatesMonitor = new PaymentStatusUpdatesMonitor(this);

            _settlerMonitor = new SettlerMonitor(this);

            LoadContactList();

            LoadMessageLocks();

            await _invoiceStateUpdatesMonitor.StartAsync();
            await _paymentStatusUpdatesMonitor.StartAsync();
            await _settlerMonitor.StartAsync();

            await SayHelloAsync();
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    private void GigGossipNode_OnServerConnectionState(object? sender, ServerConnectionStateEventArgs e)
    {
        FireOnServerConnectionState(ServerConnectionSource.NostrRelay, e.State, e.Uri);
    }

    public override async Task StopAsync()
    {
        using var TL = TRACE.Log();
        try
        {
            await base.StopAsync();
            if (this._invoiceStateUpdatesMonitor != null)
                this._invoiceStateUpdatesMonitor.Stop();
            if (this._paymentStatusUpdatesMonitor != null)
                this._paymentStatusUpdatesMonitor.Stop();
            if(this._settlerMonitor!=null)
                this._settlerMonitor.Stop();
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public IWalletAPI GetWalletClient()
    {
        return WalletSelector.GetWalletClient(defaultWalletUri);
    }

    public void ClearContacts()
    {
        using var TL = TRACE.Log();
        try
        {
            lock (_contactList)
            {
                this.nodeContext.Value.RemoveObjectRange(
                from c in this.nodeContext.Value.NostrContacts where c.PublicKey == this.PublicKey select c);
                _contactList.Clear();
            }
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public override void OnHello(string eventId, string senderPublicKey)
    {
        using var TL = TRACE.Log().Args(eventId, senderPublicKey);
        try
        {
            if (senderPublicKey != this.PublicKey)
                AddContact(senderPublicKey, "");
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public override void OnSettings(string eventId, string settings)
    {
        using var TL = TRACE.Log().Args(eventId, settings);  
        try
        {
            Settings = settings;
            this.gigGossipNodeEvents.OnSettings(this, settings);
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void FireOnServerConnectionState(ServerConnectionSource source, ServerConnectionState state, Uri uri)
    {
        using var TL = TRACE.Log().Args(source, state, uri);
        try
        {
            this.gigGossipNodeEvents.OnServerConnectionState(this, source, state, uri);
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void AddContact(string contactPublicKey, string petname, string relay = "")
    {
        using var TL = TRACE.Log().Args(contactPublicKey, petname, relay);
        try
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
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public List<string> LoadContactList()
    {
        using var TL = TRACE.Log();
        try
        {
            lock (_contactList)
            {
                var mycontacts = (from c in this.nodeContext.Value.NostrContacts where c.PublicKey == this.PublicKey select c);
                foreach (var c in mycontacts)
                    _contactList[c.ContactPublicKey] = c;
                return _contactList.Keys.ToList();
            }
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public List<string> GetContactList()
    {
        using var TL = TRACE.Log();
        try
        {
            lock (_contactList)
            {
                return _contactList.Keys.ToList();
            }
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<IGigStatusClient> GetGigStatusClientAsync(Uri serviceUri)
    {
        using var TL = TRACE.Log().Args(serviceUri);
        try
        {
            return TL.Ret(
                await settlerGigStatusClients.GetOrAddAsync(
                serviceUri,
                async (serviceUri) =>
                {
                    var newClient = SettlerSelector.GetSettlerClient(serviceUri).CreateGigStatusClient();
                    await newClient.ConnectAsync(await MakeSettlerAuthTokenAsync(serviceUri), CancellationToken.None);
                    return newClient;
                })
            );
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async void DisposeGigStatusClient(Uri serviceUri)
    {
        using var TL = TRACE.Log().Args(serviceUri);
        try
        {
            IGigStatusClient client;
            if (settlerGigStatusClients.TryRemove(serviceUri, out client))
                await client.DisposeAsync();
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<IPreimageRevealClient> GetPreimageRevealClientAsync(Uri serviceUri)
    {
        using var TL = TRACE.Log().Args(serviceUri);
        try
        {
            return TL.Ret(
                await settlerPreimageRevelClients.GetOrAddAsync(
                serviceUri,
                async (serviceUri) =>
                {
                    var newClient = SettlerSelector.GetSettlerClient(serviceUri).CreatePreimageRevealClient();
                    await newClient.ConnectAsync(await MakeSettlerAuthTokenAsync(serviceUri), CancellationToken.None);
                    return newClient;
                })
            );
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async void DisposePreimageRevealClient(Uri serviceUri)
    {
        using var TL = TRACE.Log().Args(serviceUri);
        try
        {
            IPreimageRevealClient client;
            if (settlerPreimageRevelClients.TryRemove(serviceUri, out client))
                await client.DisposeAsync();
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public List<string> GetBroadcastContactList(Guid signedrequestpayloadId, string? originatorPublicKey)
    {
        using var TL = TRACE.Log().Args(signedrequestpayloadId, originatorPublicKey);
        try
        {
            var rnd = new Random();
            var contacts = new HashSet<string>(GetContactList());
            var alreadyBroadcasted = (from inc in this.nodeContext.Value.BroadcastHistory where inc.PublicKey == this.PublicKey && inc.SignedRequestPayloadId == signedrequestpayloadId select inc.ContactPublicKey).ToList();
            contacts.ExceptWith(alreadyBroadcasted);
            if (originatorPublicKey != null)
                contacts.ExceptWith(new string[] { originatorPublicKey });
            if (contacts.Count == 0)
            {
                TL.Info("empty broadcast list");   
                return TL.Ret(new List<string>());
            }

            var retcontacts = (from r in contacts.AsEnumerable().OrderBy(x => rnd.Next()).Take(this.fanout) select new BroadcastHistoryRow() { ContactPublicKey = r, SignedRequestPayloadId = signedrequestpayloadId, PublicKey = this.PublicKey });
            this.nodeContext.Value.TryAddObjectRange(retcontacts);
            if (originatorPublicKey != null)
                this.nodeContext.Value.TryAddObject(new BroadcastHistoryRow() { ContactPublicKey = originatorPublicKey, SignedRequestPayloadId = signedrequestpayloadId, PublicKey = this.PublicKey });

            return TL.Ret( 
                (from r in retcontacts select r.ContactPublicKey).ToList()
            );
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }


    public async Task BroadcastAsync(Certificate<RequestPayloadValue> requestPayload,
                        string? originatorPublicKey = null,
                        OnionRoute? backwardOnion = null)
    {
        using var TL = TRACE.Log().Args(requestPayload, originatorPublicKey, backwardOnion);
        try
        {

            var tobroadcast = GetBroadcastContactList(requestPayload.Id, originatorPublicKey);
            if (tobroadcast.Count == 0)
            {
                TL.Info("empty broadcast list (already broadcasted)");
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
                TL.NewMessage(this.PublicKey, peerPublicKey, "broadcast");
            }
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public List<string> GetBroadcastCancelContactList(Guid signedrequestpayloadId)
    {
        using var TL = TRACE.Log().Args(signedrequestpayloadId);
        try
        {
            var alreadyBroadcastCanceled = (from inc in this.nodeContext.Value.BroadcastCancelHistory where inc.PublicKey == this.PublicKey && inc.SignedRequestPayloadId == signedrequestpayloadId select inc.ContactPublicKey).ToList();
            var alreadyBroadcasted = new HashSet<string>((from inc in this.nodeContext.Value.BroadcastHistory where inc.PublicKey == this.PublicKey && inc.SignedRequestPayloadId == signedrequestpayloadId select inc.ContactPublicKey));
            alreadyBroadcasted.ExceptWith(alreadyBroadcastCanceled);
            this.nodeContext.Value.AddObjectRange((from r in alreadyBroadcasted select new BroadcastCancelHistoryRow() { ContactPublicKey = r, SignedRequestPayloadId = signedrequestpayloadId, PublicKey = this.PublicKey }));
            return TL.Ret(
                alreadyBroadcasted.ToList()
            );
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task CancelBroadcastAsync(Certificate<CancelRequestPayloadValue> cancelRequestPayload)
    {
        using var TL = TRACE.Log().Args(cancelRequestPayload);
        try
        {
            var tobroadcast = GetBroadcastCancelContactList(cancelRequestPayload.Id);
            foreach (var peerPublicKey in tobroadcast)
            {
                CancelBroadcastFrame cancelBroadcastFrame = new CancelBroadcastFrame()
                {
                    SignedCancelRequestPayload = cancelRequestPayload
                };
                await this.SendMessageAsync(peerPublicKey, cancelBroadcastFrame, false, DateTime.UtcNow.AddMinutes(2));
            }
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task OnCancelBroadcastFrameAsync(string messageId, string peerPublicKey, CancelBroadcastFrame cancelBroadcastFrame)
    {
        using var TL = TRACE.Log().Args(messageId, peerPublicKey, cancelBroadcastFrame);
        try
        {
            if (!await cancelBroadcastFrame.SignedCancelRequestPayload.VerifyAsync(SettlerSelector, CancellationTokenSource.Token))
            {
                TL.Warning("cancel request payload mismatch");
                return;
            }

            gigGossipNodeEvents.OnCancelBroadcast(this, peerPublicKey, cancelBroadcastFrame);
            await CancelBroadcastAsync(cancelBroadcastFrame.SignedCancelRequestPayload);
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task OnBroadcastFrameAsync(string messageId, string peerPublicKey, BroadcastFrame broadcastFrame)
    {
        using var TL = TRACE.Log().Args(messageId, peerPublicKey, broadcastFrame);  
        try
        {
            if (broadcastFrame.SignedRequestPayload.Value.Timestamp > DateTime.UtcNow)
            {
                TL.Warning("future timestamp");
                return;
            }

            if (broadcastFrame.SignedRequestPayload.Value.Timestamp + this.timestampTolerance < DateTime.UtcNow)
            {
                TL.Warning("timestamp too old");
                return;
            }

            if (!await broadcastFrame.SignedRequestPayload.VerifyAsync(SettlerSelector, CancellationTokenSource.Token))
            {
                TL.Warning("request payload mismatch");
                return;
            }

            gigGossipNodeEvents.OnAcceptBroadcast(this, peerPublicKey, broadcastFrame);
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task BroadcastToPeersAsync(string peerPublicKey, BroadcastFrame broadcastFrame)
    {
        await this.BroadcastAsync(
            requestPayload: broadcastFrame.SignedRequestPayload,
            originatorPublicKey: peerPublicKey,
            backwardOnion: broadcastFrame.BackwardOnion);
    }


    public async Task<AcceptBroadcastReturnValue> AcceptBroadcastAsync(string peerPublicKey, BroadcastFrame broadcastFrame, AcceptBroadcastResponse acceptBroadcastResponse, Func<AcceptBroadcastReturnValue, Task> preSend, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(peerPublicKey, broadcastFrame, acceptBroadcastResponse, preSend);
        try
        {

            ReplyFrame responseFrame;
            PayReqRet decodedReplyInvoice;
            Guid replierCertificateId;
            string replyPaymentHash;

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
                    TL.NewMessage(this.PublicKey, Encoding.Default.GetBytes(acceptBroadcastResponse.SettlerServiceUri.AbsoluteUri).AsHex(), "getSecret");
                    var settlerClient = this.SettlerSelector.GetSettlerClient(acceptBroadcastResponse.SettlerServiceUri);
                    var authToken = await MakeSettlerAuthTokenAsync(acceptBroadcastResponse.SettlerServiceUri);
                    replyPaymentHash = SettlerAPIResult.Get<string>(await settlerClient.GenerateReplyPaymentPreimageAsync(authToken, broadcastFrame.SignedRequestPayload.Id, this.PublicKey, CancellationTokenSource.Token));
                    var replyInvoice = WalletAPIResult.Get<InvoiceRet>(await GetWalletClient().AddHodlInvoiceAsync(await MakeWalletAuthToken(), acceptBroadcastResponse.Fee, replyPaymentHash, "", (long)InvoicePaymentTimeout.TotalSeconds, cancellationToken)).PaymentRequest;
                    TL.NewMessage(Encoding.Default.GetBytes(acceptBroadcastResponse.SettlerServiceUri.AbsoluteUri).AsHex(), replyPaymentHash, "hash");
                    TL.NewMessage(this.PublicKey, replyPaymentHash, "create");
                    decodedReplyInvoice = WalletAPIResult.Get<PayReqRet>(await GetWalletClient().DecodeInvoiceAsync(await MakeWalletAuthToken(), replyInvoice, cancellationToken));
                    await this._invoiceStateUpdatesMonitor.MonitorInvoiceAsync(
                        replyPaymentHash,
                        Crypto.BinarySerializeObject(new InvoiceData()
                        {
                            IsNetworkInvoice = false,
                            Invoice = replyInvoice,
                            PaymentHash = decodedReplyInvoice.PaymentHash,
                            TotalSeconds = (int)InvoicePaymentTimeout.TotalSeconds
                        }));

                    await this._settlerMonitor.MonitorPreimageAsync(
                        acceptBroadcastResponse.SettlerServiceUri,
                        replyPaymentHash);
                    var signedRequestPayloadSerialized = Crypto.BinarySerializeObject(broadcastFrame.SignedRequestPayload);
                    var settr = SettlerAPIResult.Get<string>(await settlerClient.GenerateSettlementTrustAsync(authToken,
                        string.Join(",",acceptBroadcastResponse.Properties),
                        replyInvoice,
                        new FileParameter(new MemoryStream(acceptBroadcastResponse.Message)),
                        new FileParameter(new MemoryStream(signedRequestPayloadSerialized)),
                        CancellationTokenSource.Token
                        ));
                    var settlementTrust = Crypto.BinaryDeserializeObject<SettlementTrust>(Convert.FromBase64String(settr));
                    var signedSettlementPromise = settlementTrust.SettlementPromise;
                    var networkInvoice = settlementTrust.NetworkInvoice;
                    var decodedNetworkInvoice = WalletAPIResult.Get<PayReqRet>(await GetWalletClient().DecodeInvoiceAsync(await MakeWalletAuthToken(), networkInvoice, cancellationToken));
                    TL.NewMessage(Encoding.Default.GetBytes(acceptBroadcastResponse.SettlerServiceUri.AbsoluteUri).AsHex(), decodedNetworkInvoice.PaymentHash, "create");
                    var encryptedReplyPayload = settlementTrust.EncryptedReplyPayload;

                    replierCertificateId = settlementTrust.ReplierCertificateId;

                    this.nodeContext.Value.AddObject(new AcceptedBroadcastRow()
                    {
                        PublicKey = this.PublicKey,
                        SignedRequestPayloadId = broadcastFrame.SignedRequestPayload.Id,
                        SettlerServiceUri = acceptBroadcastResponse.SettlerServiceUri,
                        EncryptedReplyPayload = encryptedReplyPayload,
                        NetworkInvoice = networkInvoice,
                        SignedSettlementPromise = Crypto.BinarySerializeObject(signedSettlementPromise),
                        ReplyInvoice = replyInvoice,
                        DecodedNetworkInvoice = JsonSerializer.SerializeToUtf8Bytes(decodedNetworkInvoice),
                        DecodedReplyInvoice = JsonSerializer.SerializeToUtf8Bytes(decodedReplyInvoice),
                        ReplyInvoiceHash = replyPaymentHash,
                        Cancelled = false,
                        ReplierCertificateId = settlementTrust.ReplierCertificateId,
                    });

                    TL.NewMessage(Encoding.Default.GetBytes(acceptBroadcastResponse.SettlerServiceUri.AbsoluteUri).AsHex(), broadcastFrame.SignedRequestPayload.Id.ToString() + "_" + this.PublicKey, "create");
                    TL.NewMessage(this.PublicKey, broadcastFrame.SignedRequestPayload.Id.ToString() + "_" + this.PublicKey, "encrypts");

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
                    replyPaymentHash = alreadyBroadcasted.ReplyInvoiceHash;
                    decodedReplyInvoice = JsonSerializer.Deserialize<PayReqRet>(new MemoryStream(alreadyBroadcasted.DecodedReplyInvoice));
                    replierCertificateId = alreadyBroadcasted.ReplierCertificateId;
                    responseFrame = new ReplyFrame()
                    {
                        EncryptedReplyPayload = alreadyBroadcasted.EncryptedReplyPayload,
                        SignedSettlementPromise = Crypto.BinaryDeserializeObject<SettlementPromise>(alreadyBroadcasted.SignedSettlementPromise),
                        ForwardOnion = broadcastFrame.BackwardOnion,
                        NetworkInvoice = alreadyBroadcasted.NetworkInvoice
                    };
                }
            }
            finally
            {
                alreadyBroadcastedSemaphore.Release();
            }
            var ret = new AcceptBroadcastReturnValue()
            {
                ReplierCertificateId = replierCertificateId,
                DecodedReplyInvoice = decodedReplyInvoice,
                ReplyInvoiceHash = replyPaymentHash,
            };

            await preSend(ret);

            await OnResponseFrameAsync(null, peerPublicKey, responseFrame, newResponse: true, cancellationToken);

            return TL.Ret(ret);
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
   }

    public async Task OnResponseFrameAsync(string messageId, string peerPublicKey, ReplyFrame responseFrame, bool newResponse, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(messageId, peerPublicKey, responseFrame, newResponse);
        try
        {
            await alreadyBroadcastedSemaphore.WaitAsync();
            try
            {
                var decodedNetworkInvoice = WalletAPIResult.Get<PayReqRet>(await GetWalletClient().DecodeInvoiceAsync(await MakeWalletAuthToken(), responseFrame.NetworkInvoice, cancellationToken));
                if (responseFrame.ForwardOnion.IsEmpty())
                {
                    TL.NewMessage(peerPublicKey, this.PublicKey, "reply");
                    var settlerPubKey = await SettlerSelector.GetPubKeyAsync(responseFrame.SignedSettlementPromise.RequestersServiceUri, CancellationTokenSource.Token);
                    var replyPayload = await responseFrame.DecryptAndVerifyAsync(privateKey, settlerPubKey, this.SettlerSelector, CancellationTokenSource.Token);
                    if (replyPayload == null)
                    {
                        TL.Warning("reply payload mismatch");
                        return;
                    }
                    await _settlerMonitor.MonitorGigStatusAsync(
                        responseFrame.SignedSettlementPromise.ServiceUri,
                        replyPayload.Value.SignedRequestPayload.Id,
                        replyPayload.Id,
                        Crypto.BinarySerializeObject(replyPayload));

                    var decodedReplyInvoice = WalletAPIResult.Get<PayReqRet>(await GetWalletClient().DecodeInvoiceAsync(await MakeWalletAuthToken(), replyPayload.Value.ReplyInvoice, cancellationToken));

                    await this._invoiceStateUpdatesMonitor.MonitorInvoiceAsync(
                        decodedReplyInvoice.PaymentHash,
                        Crypto.BinarySerializeObject(new InvoiceData()
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
                            DecodedReplyInvoice = JsonSerializer.SerializeToUtf8Bytes(decodedReplyInvoice),
                            NetworkInvoice = responseFrame.NetworkInvoice,
                            DecodedNetworkInvoice = JsonSerializer.SerializeToUtf8Bytes(decodedNetworkInvoice),
                            TheReplyPayload = Crypto.BinarySerializeObject(replyPayload)
                        });

                    gigGossipNodeEvents.OnNewResponse(this, replyPayload, replyPayload.Value.ReplyInvoice, decodedReplyInvoice, responseFrame.NetworkInvoice, decodedNetworkInvoice);
                }
                else
                {
                    var topLayerPublicKey = responseFrame.ForwardOnion.Peel(privateKey);
                    if (!await responseFrame.SignedSettlementPromise.VerifyAsync(responseFrame.EncryptedReplyPayload, this.SettlerSelector, CancellationTokenSource.Token))
                    {
                        TL.Warning("settlement promise mismatch");
                        return;
                    }

                    if (!newResponse)
                    {
                        TL.NewReply(peerPublicKey, this.PublicKey, "reply");
                        var settlerClient = this.SettlerSelector.GetSettlerClient(responseFrame.SignedSettlementPromise.ServiceUri);
                        var settok = await MakeSettlerAuthTokenAsync(responseFrame.SignedSettlementPromise.ServiceUri);

                        if (!SettlerAPIResult.Get<bool>(await settlerClient.ValidateRelatedPaymentHashesAsync(settok,
                            responseFrame.SignedSettlementPromise.NetworkPaymentHash.AsHex(),
                            decodedNetworkInvoice.PaymentHash,
                            CancellationTokenSource.Token)))
                        {
                            TL.Warning("related payment hashes mismatch");
                            return;
                        }

                        var relatedNetworkPaymentHash = SettlerAPIResult.Get<string>(await settlerClient.GenerateRelatedPreimageAsync(
                            settok,
                            decodedNetworkInvoice.PaymentHash,
                            CancellationTokenSource.Token));

                        var networkInvoice = WalletAPIResult.Get<InvoiceRet>(await GetWalletClient().AddHodlInvoiceAsync(
                            await this.MakeWalletAuthToken(),
                            decodedNetworkInvoice.ValueSat + this.priceAmountForRouting,
                            relatedNetworkPaymentHash, "", (long)InvoicePaymentTimeout.TotalSeconds, cancellationToken));
                        TL.NewMessage(this.PublicKey, relatedNetworkPaymentHash, "create");
                        await this._invoiceStateUpdatesMonitor.MonitorInvoiceAsync(
                            relatedNetworkPaymentHash,
                            Crypto.BinarySerializeObject(new InvoiceData()
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
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public List<ReplyPayloadRow> GetReplyPayloads()
    {
        using var TL = TRACE.Log();
        try
        {
            alreadyBroadcastedSemaphore.Wait();
            try
            {
                return TL.Ret(
                    (from rp in this.nodeContext.Value.ReplyPayloads where rp.PublicKey == this.PublicKey select rp).ToList()
                );
            }
            finally
            {
                alreadyBroadcastedSemaphore.Release();
            }
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public List<ReplyPayloadRow> GetReplyPayloads(Guid signedrequestpayloadId)
    {
        using var TL = TRACE.Log().Args(signedrequestpayloadId);
        try
        {
            alreadyBroadcastedSemaphore.Wait();
            try
            {
                return TL.Ret(
                     (from rp in this.nodeContext.Value.ReplyPayloads where rp.PublicKey == this.PublicKey && rp.SignedRequestPayloadId == signedrequestpayloadId select rp).ToList()
                );
            }
            finally
            {
                alreadyBroadcastedSemaphore.Release();
            }
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public List<AcceptedBroadcastRow> GetAcceptedNotCancelledBroadcasts()
    {
        using var TL = TRACE.Log();
        try
        {
            alreadyBroadcastedSemaphore.Wait();
            try
            {
                return TL.Ret(
                    (from rp in this.nodeContext.Value.AcceptedBroadcasts where rp.PublicKey == this.PublicKey && !rp.Cancelled select rp).ToList()
                );
            }
            finally
            {
                alreadyBroadcastedSemaphore.Release();
            }
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void MarkBroadcastAsCancelled(AcceptedBroadcastRow brd)
    {
        using var TL = TRACE.Log().Args(brd);
        try
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
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }


    public AcceptedBroadcastRow GetAcceptedBroadcastsByReqestPayloadId(Guid signedrequestpayloadId)
    {
        using var TL = TRACE.Log().Args(signedrequestpayloadId);
        try
        {
            alreadyBroadcastedSemaphore.Wait();
            try
            {
                return TL.Ret(
                     (from rp in this.nodeContext.Value.AcceptedBroadcasts where rp.PublicKey == this.PublicKey && rp.SignedRequestPayloadId == signedrequestpayloadId select rp).FirstOrDefault()
                );
            }
            finally
            {
                alreadyBroadcastedSemaphore.Release();
            }
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public AcceptedBroadcastRow GetAcceptedBroadcastsByReplyInvoiceHash(string replyInvoiceHash)
    {
        using var TL = TRACE.Log().Args(replyInvoiceHash);
        try
        {
            alreadyBroadcastedSemaphore.Wait();
            try
            {
                return TL.Ret(
                    (from rp in this.nodeContext.Value.AcceptedBroadcasts where rp.PublicKey == this.PublicKey && rp.ReplyInvoiceHash == replyInvoiceHash select rp).FirstOrDefault()
                );
            }
            finally
            {
                alreadyBroadcastedSemaphore.Release();
            }
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnInvoiceStateChange(string state, byte[] data)
    {
        using var TL = TRACE.Log().Args(state, data);
        try
        {
            var iac = Crypto.BinaryDeserializeObject<InvoiceData>(data);
            TL.NewNote(iac.PaymentHash, state);
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
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GigLNDWalletAPIErrorCode> PayNetworkInvoiceAsync(InvoiceData iac, long feelimit, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(iac, feelimit);
        try
        {
            if (_paymentStatusUpdatesMonitor.IsPaymentMonitored(iac.PaymentHash))
            {
                TL.Warning("already payed");
                return TL.Ret(GigLNDWalletAPIErrorCode.AlreadyPayed);
            }

            await _paymentStatusUpdatesMonitor.MonitorPaymentAsync(iac.PaymentHash, Crypto.BinarySerializeObject(
                new PaymentData()
                {
                    Invoice = iac.Invoice,
                    PaymentHash = iac.PaymentHash
                }));
            var paymentStatus = WalletAPIResult.Status(await GetWalletClient().SendPaymentAsync(
                await MakeWalletAuthToken(), iac.Invoice, iac.TotalSeconds, feelimit, cancellationToken
                ));
            if (paymentStatus != GigLNDWalletAPIErrorCode.Ok)
            {
                await _paymentStatusUpdatesMonitor.StopPaymentMonitoringAsync(iac.PaymentHash);
            }
            return TL.Ret(paymentStatus);
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async void OnPaymentStatusChange(string status, byte[] data)
    {
        using var TL = TRACE.Log().Args(status, data);
        try
        {
            var pay = Crypto.BinaryDeserializeObject<PaymentData>(data);
            this.gigGossipNodeEvents.OnPaymentStatusChange(this, status, pay);
            TL.NewMessage(this.PublicKey, pay.PaymentHash, "pay_" + status);
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<bool> OnPreimageRevealedAsync(Uri serviceUri, string phash, string preimage,CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(serviceUri, phash, preimage);
        try
        {
            WalletAPIResult.Check(await GetWalletClient().SettleInvoiceAsync(
                await MakeWalletAuthToken(),
                preimage,
                cancellationToken
                ));
            TL.NewMessage(Encoding.Default.GetBytes(serviceUri.AbsoluteUri).AsHex(), phash, "revealed");
            TL.NewMessage(this.PublicKey, phash, "settled");
            gigGossipNodeEvents.OnInvoiceSettled(this, serviceUri, phash, preimage);
            return TL.Ret(true); 
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            return TL.Ret(false);
        }
    }

    public void OnSymmetricKeyRevealed(byte[] data, string key)
    {
        using var TL = TRACE.Log().Args(data, key);
        try
        {
            var replyPayload = Crypto.BinaryDeserializeObject<Certificate<ReplyPayloadValue>>(data);
            gigGossipNodeEvents.OnResponseReady(this, replyPayload, key);
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnGigCancelled(byte[] data)
    {
        using var TL = TRACE.Log().Args(data);
        try
        {
            var replyPayload = Crypto.BinaryDeserializeObject<Certificate<ReplyPayloadValue>>(data);
            gigGossipNodeEvents.OnResponseCancelled(this, replyPayload);
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GigLNDWalletAPIErrorCode> AcceptResponseAsync(Certificate<ReplyPayloadValue> replyPayload, string replyInvoice, PayReqRet decodedReplyInvoice, string networkInvoice, PayReqRet decodedNetworkInvoice, long feelimit, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(replyPayload, replyInvoice, decodedReplyInvoice, networkInvoice, decodedNetworkInvoice, feelimit);
        try
        {
            var ballance = WalletAPIResult.Get<long>(await GetWalletClient().GetBalanceAsync(await MakeWalletAuthToken(), cancellationToken));
            if (ballance < decodedReplyInvoice.ValueSat + decodedNetworkInvoice.ValueSat + 2 * feelimit)
            {
                TL.Info("not enough funds");
                return TL.Ret(GigLNDWalletAPIErrorCode.NotEnoughFunds);
            }

            if (!_paymentStatusUpdatesMonitor.IsPaymentMonitored(decodedNetworkInvoice.PaymentHash))
            {
                await _paymentStatusUpdatesMonitor.MonitorPaymentAsync(decodedNetworkInvoice.PaymentHash, Crypto.BinarySerializeObject(
                new PaymentData()
                {
                    Invoice = networkInvoice,
                    PaymentHash = decodedNetworkInvoice.PaymentHash
                }));
                var paymentStatus = WalletAPIResult.Status(await GetWalletClient().SendPaymentAsync(await MakeWalletAuthToken(), networkInvoice, (int)this.InvoicePaymentTimeout.TotalSeconds, feelimit,cancellationToken));
                if(paymentStatus!= GigLNDWalletAPIErrorCode.Ok)
                {
                    await _paymentStatusUpdatesMonitor.StopPaymentMonitoringAsync(decodedNetworkInvoice.PaymentHash);
                    TL.Warning("network invoice payment failed");
                    return TL.Ret(paymentStatus);
                }
            }

            if (!_paymentStatusUpdatesMonitor.IsPaymentMonitored(decodedReplyInvoice.PaymentHash))
            {
                await _paymentStatusUpdatesMonitor.MonitorPaymentAsync(decodedReplyInvoice.PaymentHash, Crypto.BinarySerializeObject(
                new PaymentData()
                {
                    Invoice = replyInvoice,
                    PaymentHash = decodedReplyInvoice.PaymentHash
                }));
                var paymentStatus = WalletAPIResult.Status(await GetWalletClient().SendPaymentAsync(await MakeWalletAuthToken(), replyInvoice, (int)this.InvoicePaymentTimeout.TotalSeconds, feelimit,cancellationToken));
                if (paymentStatus != GigLNDWalletAPIErrorCode.Ok)
                {
                    await _paymentStatusUpdatesMonitor.StopPaymentMonitoringAsync(decodedNetworkInvoice.PaymentHash);
                    TL.Warning("reply invoice payment failed");
                    return TL.Ret(paymentStatus);
                }
            }
            return TL.Ret(GigLNDWalletAPIErrorCode.Ok);
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    private void LoadMessageLocks()
    {
        using var TL = TRACE.Log();
        try
        {
            messageLocks = new ConcurrentDictionary<string, bool>(from m in this.nodeContext.Value.MessagesDone where m.PublicKey == this.PublicKey select KeyValuePair.Create(m.MessageId, true));
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public override bool OpenMessage(string id)
    {
        using var TL = TRACE.Log().Args(id);
        try
        {
            return TL.Ret(messageLocks.TryAdd(id, true));
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public override bool CommitMessage(string id)
    {
        using var TL = TRACE.Log().Args(id);
        try
        {
            return TL.Ret(
                this.nodeContext.Value.TryAddObject(new MessageDoneRow() { MessageId = id, PublicKey = this.PublicKey })
            );
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public override bool AbortMessage(string id)
    {
        using var TL = TRACE.Log().Args(id);
        try
        {
            return TL.Ret(messageLocks.TryRemove(id, out _));
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public override async Task OnMessageAsync(string messageId, string senderPublicKey, object frame)
    {
        using var TL = TRACE.Log().Args(messageId, senderPublicKey, frame);
        try
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
                await OnResponseFrameAsync(messageId, senderPublicKey, (ReplyFrame)frame, false, CancellationTokenSource.Token);
            }
            else
            {
                throw new GigGossipException(GigGossipNodeErrorCode.FrameTypeNotRegistered);
            }
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<BroadcastTopicResponse> BroadcastTopicAsync<T>(T topic, string[] properties, Func<BroadcastTopicResponse,Task> preSend) where T:IProtoFrame
    {
        using var TL = TRACE.Log().Args(topic, properties, preSend);
        try
        {
            var settler = SettlerSelector.GetSettlerClient(mySettlerUri);
            var token = await MakeSettlerAuthTokenAsync(mySettlerUri);
            var topicByte = Crypto.BinarySerializeObject(topic!);
            var response = SettlerAPIResult.Get<string>(await settler.GenerateRequestPayloadAsync(token, string.Join(",",properties), new FileParameter(new MemoryStream(topicByte)), CancellationTokenSource.Token));
            var base64Response = Convert.FromBase64String(response);
            var broadcastTopicResponse = Crypto.BinaryDeserializeObject<BroadcastTopicResponse>(base64Response);

            await preSend(broadcastTopicResponse);

            await BroadcastAsync(broadcastTopicResponse!.SignedRequestPayload);

            return TL.Ret(broadcastTopicResponse);
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }


}
