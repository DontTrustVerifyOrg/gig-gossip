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
using System.Security.Cryptography.X509Certificates;
using System.Runtime.ConstrainedExecution;
using GigLNDWalletAPIClient;
using System.ComponentModel.DataAnnotations.Schema;
using System.Xml.Linq;
using GigGossipSettlerAPIClient;
using System.IO;
using System.Collections.Concurrent;
using GigGossip;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR.Client;
using NetworkClientToolkit;
using GoogleApi;
using System.Text.Json;
using Nostr.Client.Messages;
using GoogleApi.Entities.Common.Enums;

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

public enum ServerConnectionSource
{
    NostrRelay = 0,
    SettlerAPI = 1,
    WalletAPI = 2,
}

public interface IGigGossipNodeEvents
{
    public void OnNewResponse(GigGossipNode me, JobReply replyPayload, string replyInvoice, PaymentRequestRecord decodedReplyInvoice, string networkInvoice, PaymentRequestRecord decodedNetworkInvoice);
    public void OnResponseReady(GigGossipNode me, JobReply replyPayload, string key);
    public void OnResponseCancelled(GigGossipNode me, JobReply replyPayload);
    public void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, BroadcastFrame broadcastFrame);
    public void OnCancelBroadcast(GigGossipNode me, string peerPublicKey, CancelBroadcastFrame broadcastFrame);
    public void OnNetworkInvoiceAccepted(GigGossipNode me, InvoiceData iac);
    public void OnNetworkInvoiceCancelled(GigGossipNode me, InvoiceData iac);
    public void OnNetworkInvoiceSettled(GigGossipNode me, InvoiceData iac);
    public void OnJobInvoiceAccepted(GigGossipNode me, InvoiceData iac);
    public void OnJobInvoiceCancelled(GigGossipNode me, InvoiceData iac);
    public void OnJobInvoiceSettled(GigGossipNode me, InvoiceData iac);
    public void OnPaymentStatusChange(GigGossipNode me, PaymentStatus status, PaymentData paydata);

    public void OnLNDInvoiceStateChanged(GigGossipNode me, InvoiceStateChange invoice);
    public void OnLNDPaymentStatusChanged(GigGossipNode me, PaymentStatusChanged payment);
    public void OnLNDNewTransaction(GigGossipNode me, NewTransactionFound newTransaction);
    public void OnLNDPayoutStateChanged(GigGossipNode me, PayoutStateChanged payout);

    public void OnServerConnectionState(GigGossipNode me, ServerConnectionSource source, ServerConnectionState state, Uri uri);
}

public class AcceptBroadcastResponse
{
    public required Uri SettlerServiceUri { get; set; }
    public required string [] Properties { get; set; }
    public required RideShareReply RideShareReply { get; set; }
    public required long Fee { get; set; }
}


public class AcceptBroadcastReturnValue
{
    public required Guid ReplierCertificateId { get; set; }
    public required PaymentRequestRecord DecodedReplyInvoice { get; set; }
    public required string ReplyInvoiceHash { get; set; }
}

public class DirectMessageEventArgs : EventArgs
{
    public required string EventId;
    public required string SenderPublicKey;
    public required LocationFrame LocationFrame { get; set; }
}

public class GigGossipNode : NostrNode, IInvoiceStateUpdatesMonitorEvents, IPaymentStatusUpdatesMonitorEvents, ISettlerMonitorEvents
{
    protected long priceAmountForRouting;
    protected TimeSpan timestampTolerance;
    public TimeSpan InvoicePaymentTimeout;
    protected int fanout;

    private SemaphoreSlim alreadyBroadcastedSemaphore = new SemaphoreSlim(1, 1);

    private ConcurrentDictionary<Uri, IGigStatusClient> settlerGigStatusClients ;
    private ConcurrentDictionary<Uri, IPreimageRevealClient> settlerPreimageRevelClients ;

    protected ConcurrentDictionary<Uri, Guid> _walletToken;
    protected ConcurrentDictionary<Uri, Guid> _settlerToken;

    public ISettlerSelector SettlerSelector;
    public IGigLNDWalletSelector WalletSelector;

    protected InvoiceStateUpdatesMonitor _invoiceStateUpdatesMonitor;
    protected PaymentStatusUpdatesMonitor _paymentStatusUpdatesMonitor;
    protected TransactionUpdatesMonitor _transactionUpdatesMonitor;
    protected PayoutStateUpdatesMonitor _payoutStateUpdatesMonitor;
    protected SettlerMonitor _settlerMonitor;

    Dictionary<string, NostrContact> _contactList ;

    private IGigGossipNodeEvents gigGossipNodeEvents;
    private Uri defaultWalletUri;
    private Uri mySettlerUri;
    private Func<HttpClient> httpClientFactory;

    internal GigGossipNodeDatabase NodeDb;

    ConcurrentDictionary<string, bool> messageLocks;
    public GigDebugLoggerAPIClient.LogWrapper<GigGossipNode> TRACE = GigDebugLoggerAPIClient.FlowLoggerFactory.Trace<GigGossipNode>();

    public GigGossipNode(string connectionString, ECPrivKey privKey, int chunkSize, IRetryPolicy retryPolicy, Func<HttpClient> httpClientFactory, bool traceEnabled, Uri myLoggerUri) : base(privKey, chunkSize,true, retryPolicy)
    {
        this.OnServerConnectionState += GigGossipNode_OnServerConnectionState;

        this.NodeDb = new GigGossipNodeDatabase(connectionString);
        this.httpClientFactory = httpClientFactory;

        WalletSelector = new SimpleGigLNDWalletSelector(httpClientFactory, RetryPolicy);
        SettlerSelector = new SimpleSettlerSelector(httpClientFactory, RetryPolicy);
        _settlerToken = new();

    }

    public async Task<string> MakeWalletAuthToken()
    {
        return AuthToken.Create(
            this.privateKey, DateTime.UtcNow,
            await _walletToken.GetOrAddAsync(defaultWalletUri, async (serviceUri) => WalletAPIResult.Get<Guid>(await WalletSelector.GetWalletClient(serviceUri).GetTokenAsync(this.PublicKey,CancellationTokenSource.Token))));
    }

    public async Task<string> MakeSettlerAuthTokenAsync(Uri serviceUri)
    {
        return AuthToken.Create(
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
            _transactionUpdatesMonitor = new TransactionUpdatesMonitor(this);
            _payoutStateUpdatesMonitor = new PayoutStateUpdatesMonitor(this);

            _settlerMonitor = new SettlerMonitor(this);

            LoadContactList();

            LoadMessageLocks();

            await _invoiceStateUpdatesMonitor.StartAsync();
            await _paymentStatusUpdatesMonitor.StartAsync();
            await _transactionUpdatesMonitor.StartAsync();
            await _payoutStateUpdatesMonitor.StartAsync();
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
        using var TL = TRACE.Log();
        try
        {
            FireOnServerConnectionState(ServerConnectionSource.NostrRelay, e.State, e.Uri);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
        }
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
            if (this._transactionUpdatesMonitor != null)
                this._transactionUpdatesMonitor.Stop();
            if (this._payoutStateUpdatesMonitor != null)
                _payoutStateUpdatesMonitor.Stop();
            if (this._settlerMonitor!=null)
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
                lock (NodeDb.Context)
                {
                    NodeDb.Context.RemoveObjectRange(
                    from c in NodeDb.Context.NostrContacts where c.PublicKey == this.PublicKey select c);
                    _contactList.Clear();
                }
            }
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public override void OnHello(string senderPublicKey, DateTime createdAt)
    {
        using var TL = TRACE.Log().Args(senderPublicKey);
        try
        {
            if (senderPublicKey != this.PublicKey)
                UpdateContact(senderPublicKey, createdAt);
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

    public void UpdateContact(string contactPublicKey, DateTime createdAt)
    {
        using var TL = TRACE.Log().Args(contactPublicKey);
        try
        {
            if (contactPublicKey == this.PublicKey)
                throw new GigGossipException(GigGossipNodeErrorCode.SelfConnection);
            var c = new NostrContact()
            {
                PublicKey = this.PublicKey,
                ContactPublicKey = contactPublicKey,
                LastSeen = createdAt,
            };

            lock (_contactList)
            {
                _contactList[c.ContactPublicKey] = c;
                lock (NodeDb.Context)
                    if (!NodeDb.Context.TryAddObject(c))
                        NodeDb.Context.SaveObject(c);
            }
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void LoadContactList()
    {
        using var TL = TRACE.Log();
        try
        {
            lock (_contactList)
            {
                lock (NodeDb.Context)
                {
                    var mycontacts = (from c in NodeDb.Context.NostrContacts where c.PublicKey == this.PublicKey select c);
                    foreach (var c in mycontacts)
                        _contactList[c.ContactPublicKey] = c;
                }
            }
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public DateTime? ContactLastSeen(string pubkey)
    {
        using var TL = TRACE.Log();
        try
        {
            lock (_contactList)
            {
                if (_contactList.ContainsKey(pubkey))
                    return TL.Ret(_contactList[pubkey].LastSeen);
                else
                    return TL.Ret<DateTime?>(null);
            }
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public List<string> GetContactList(int activeBeforeAtLeastHours)
    {
        using var TL = TRACE.Log();
        try
        {
            lock (_contactList)
            {
                return TL.Ret((from c in _contactList.Values where c.LastSeen >= DateTime.UtcNow.AddHours(-activeBeforeAtLeastHours) select c.ContactPublicKey).ToList());
            }
        }
        catch (Exception ex)
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
            var contacts = new HashSet<string>(GetContactList(24));
            lock (NodeDb.Context)
            {
                var alreadyBroadcasted = (from inc in NodeDb.Context.BroadcastHistory where inc.PublicKey == this.PublicKey && inc.SignedRequestPayloadId == signedrequestpayloadId select inc.ContactPublicKey).ToList();
                contacts.ExceptWith(alreadyBroadcasted);
            }
            if (originatorPublicKey != null)
                contacts.ExceptWith(new string[] { originatorPublicKey });
            if (contacts.Count == 0)
            {
                TL.Info("empty broadcast list");   
                return TL.Ret(new List<string>());
            }

            var retcontacts = (from r in contacts.AsEnumerable().OrderBy(x => rnd.Next()).Take(this.fanout) select new BroadcastHistoryRow() { ContactPublicKey = r, SignedRequestPayloadId = signedrequestpayloadId, PublicKey = this.PublicKey });
            NodeDb.Context.TryAddObjectRange(retcontacts);
            if (originatorPublicKey != null)
                NodeDb.Context.TryAddObject(new BroadcastHistoryRow() { ContactPublicKey = originatorPublicKey, SignedRequestPayloadId = signedrequestpayloadId, PublicKey = this.PublicKey });

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


    public async Task BroadcastAsync(JobRequest requestPayload,
                        string? originatorPublicKey = null,
                        Onion? backwardOnion = null)
    {
        using var TL = TRACE.Log().Args(requestPayload, originatorPublicKey, backwardOnion);
        try
        {

            var tobroadcast = GetBroadcastContactList(requestPayload.Header.JobRequestId.AsGuid(), originatorPublicKey);
            if (tobroadcast.Count == 0)
            {
                TL.Info("empty broadcast list (already broadcasted)");
                return;
            }

            foreach (var peerPublicKey in tobroadcast)
            {
                BroadcastFrame broadcastFrame = new BroadcastFrame()
                {
                    JobRequest = requestPayload,
                    BackwardOnion = (backwardOnion ?? Onion.GetEmpty()).Grow(
                        this.PublicKey,
                        peerPublicKey.AsECXOnlyPubKey())
                };
                await SendMessageAsync(peerPublicKey, new Frame { Broadcast = broadcastFrame }, true);
                TL.NewMessage(this.PublicKey, peerPublicKey, "broadcast");
            }
        }
        catch (Exception ex)
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
            lock (NodeDb.Context)
            {
                var alreadyBroadcastCanceled = (from inc in NodeDb.Context.BroadcastCancelHistory where inc.PublicKey == this.PublicKey && inc.SignedRequestPayloadId == signedrequestpayloadId select inc.ContactPublicKey).ToList();
                var alreadyBroadcasted = new HashSet<string>((from inc in NodeDb.Context.BroadcastHistory where inc.PublicKey == this.PublicKey && inc.SignedRequestPayloadId == signedrequestpayloadId select inc.ContactPublicKey));
                alreadyBroadcasted.ExceptWith(alreadyBroadcastCanceled);
                NodeDb.Context.AddObjectRange((from r in alreadyBroadcasted select new BroadcastCancelHistoryRow() { ContactPublicKey = r, SignedRequestPayloadId = signedrequestpayloadId, PublicKey = this.PublicKey }));
                return TL.Ret(
                    alreadyBroadcasted.ToList()
                );
            }
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task CancelBroadcastAsync(CancelJobRequest cancelRequestPayload)
    {
        using var TL = TRACE.Log().Args(cancelRequestPayload);
        try
        {
            var tobroadcast = GetBroadcastCancelContactList(cancelRequestPayload.Header.JobRequestId.AsGuid());
            foreach (var peerPublicKey in tobroadcast)
            {
                CancelBroadcastFrame cancelBroadcastFrame = new CancelBroadcastFrame()
                {
                    CancelJobRequest = cancelRequestPayload
                };
                await this.SendMessageAsync(peerPublicKey, new Frame { CancelBroadcast = cancelBroadcastFrame }, false, DateTime.UtcNow.AddMinutes(2));
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
            if (!await cancelBroadcastFrame.CancelJobRequest.VerifyAsync(SettlerSelector, CancellationTokenSource.Token))
            {
                TL.Warning("cancel request payload mismatch");
                return;
            }

            gigGossipNodeEvents.OnCancelBroadcast(this, peerPublicKey, cancelBroadcastFrame);
            await CancelBroadcastAsync(cancelBroadcastFrame.CancelJobRequest);
        }
        catch (Exception ex)
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
            var requestPayloadValue = broadcastFrame.JobRequest;
            if (requestPayloadValue.Header.Timestamp.AsUtcDateTime() > DateTime.UtcNow)
            {
                TL.Warning("future timestamp");
                return;
            }

            if (requestPayloadValue.Header.Timestamp.AsUtcDateTime() + this.timestampTolerance < DateTime.UtcNow)
            {
                TL.Warning("timestamp too old");
                return;
            }

            if (!await broadcastFrame.JobRequest.VerifyAsync(SettlerSelector, CancellationTokenSource.Token))
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
            requestPayload: broadcastFrame.JobRequest,
            originatorPublicKey: peerPublicKey,
            backwardOnion: broadcastFrame.BackwardOnion);
    }


    public async Task<AcceptBroadcastReturnValue> AcceptBroadcastAsync(string peerPublicKey, BroadcastFrame broadcastFrame, AcceptBroadcastResponse acceptBroadcastResponse, Func<AcceptBroadcastReturnValue, Task> preSend, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(peerPublicKey, broadcastFrame, acceptBroadcastResponse, preSend);
        try
        {

            ReplyFrame responseFrame;
            PaymentRequestRecord decodedReplyInvoice;
            Guid replierCertificateId;
            string replyPaymentHash;

            await alreadyBroadcastedSemaphore.WaitAsync();
            try
            {
                AcceptedBroadcastRow alreadyBroadcasted;
                lock (NodeDb.Context)
                {
                    alreadyBroadcasted = (from abx in NodeDb.Context.AcceptedBroadcasts
                                          where abx.PublicKey == this.PublicKey
                                          && abx.SignedRequestPayloadId == broadcastFrame.JobRequest.Header.JobRequestId.AsGuid()
                                          && abx.SettlerServiceUri == acceptBroadcastResponse.SettlerServiceUri
                                          select abx).FirstOrDefault();
                }

                if (alreadyBroadcasted == null)
                {
                    TL.NewMessage(this.PublicKey, Encoding.Default.GetBytes(acceptBroadcastResponse.SettlerServiceUri.AbsoluteUri).AsHex(), "getSecret");
                    var settlerClient = this.SettlerSelector.GetSettlerClient(acceptBroadcastResponse.SettlerServiceUri);
                    var authToken = await MakeSettlerAuthTokenAsync(acceptBroadcastResponse.SettlerServiceUri);
                    replyPaymentHash = SettlerAPIResult.Get<string>(await settlerClient.GenerateReplyPaymentPreimageAsync(authToken, broadcastFrame.JobRequest.Header.JobRequestId.AsGuid(), this.PublicKey, CancellationTokenSource.Token));
                    var replyInvoice = WalletAPIResult.Get<InvoiceRecord>(await GetWalletClient().AddHodlInvoiceAsync(await MakeWalletAuthToken(), acceptBroadcastResponse.Fee, replyPaymentHash, "", (long)InvoicePaymentTimeout.TotalSeconds, cancellationToken)).PaymentRequest;
                    TL.NewMessage(Encoding.Default.GetBytes(acceptBroadcastResponse.SettlerServiceUri.AbsoluteUri).AsHex(), replyPaymentHash, "hash");
                    TL.NewMessage(this.PublicKey, replyPaymentHash, "create");
                    decodedReplyInvoice = WalletAPIResult.Get<PaymentRequestRecord>(await GetWalletClient().DecodeInvoiceAsync(await MakeWalletAuthToken(), replyInvoice, cancellationToken));
                    await this._invoiceStateUpdatesMonitor.MonitorInvoiceAsync(
                        replyPaymentHash,
                        JsonSerializer.SerializeToUtf8Bytes(new InvoiceData()
                        {
                            IsNetworkInvoice = false,
                            Invoice = replyInvoice,
                            PaymentHash = decodedReplyInvoice.PaymentHash,
                            TotalSeconds = (int)InvoicePaymentTimeout.TotalSeconds
                        }));

                    await this._settlerMonitor.MonitorPreimageAsync(
                        acceptBroadcastResponse.SettlerServiceUri,
                        replyPaymentHash);
                    var signedRequestPayloadSerialized = Crypto.BinarySerializeObject(broadcastFrame.JobRequest);
                    var settr = SettlerAPIResult.Get<string>(await settlerClient.GenerateSettlementTrustAsync(authToken,
                        string.Join(",",acceptBroadcastResponse.Properties),
                        replyInvoice,
                        new FileParameter(new MemoryStream(Crypto.BinarySerializeObject(new Reply { RideShare = acceptBroadcastResponse.RideShareReply }))),
                        new FileParameter(new MemoryStream(signedRequestPayloadSerialized)),
                        CancellationTokenSource.Token
                        ));
                    var settlementTrust = Crypto.BinaryDeserializeObject<SettlementTrust>(Convert.FromBase64String(settr));
                    var signedSettlementPromise = settlementTrust.SettlementPromise;
                    var networkInvoice = settlementTrust.NetworkPaymentRequest.Value;
                    var decodedNetworkInvoice = WalletAPIResult.Get<PaymentRequestRecord>(await GetWalletClient().DecodeInvoiceAsync(await MakeWalletAuthToken(), networkInvoice, cancellationToken));
                    TL.NewMessage(Encoding.Default.GetBytes(acceptBroadcastResponse.SettlerServiceUri.AbsoluteUri).AsHex(), decodedNetworkInvoice.PaymentHash, "create");
                    var encryptedReplyPayload = settlementTrust.EncryptedJobReply.Value.ToArray();

                    replierCertificateId = settlementTrust.JobReplyId.AsGuid();

                    NodeDb.Context.AddObject(new AcceptedBroadcastRow()
                    {
                        PublicKey = this.PublicKey,
                        BroadcastFrame = Crypto.BinarySerializeObject(broadcastFrame),
                        SignedRequestPayloadId = broadcastFrame.JobRequest.Header.JobRequestId.AsGuid(),
                        SettlerServiceUri = acceptBroadcastResponse.SettlerServiceUri,
                        EncryptedReplyPayload = encryptedReplyPayload,
                        NetworkPaymentRequest = networkInvoice,
                        SignedSettlementPromise = Crypto.BinarySerializeObject(signedSettlementPromise),
                        ReplyInvoice = replyInvoice,
                        DecodedNetworkInvoice = JsonSerializer.SerializeToUtf8Bytes(decodedNetworkInvoice),
                        DecodedReplyInvoice = JsonSerializer.SerializeToUtf8Bytes(decodedReplyInvoice),
                        ReplyInvoiceHash = replyPaymentHash,
                        Cancelled = false,
                        ReplierCertificateId = settlementTrust.JobReplyId.AsGuid(),
                    });

                    TL.NewMessage(Encoding.Default.GetBytes(acceptBroadcastResponse.SettlerServiceUri.AbsoluteUri).AsHex(), broadcastFrame.JobRequest.Header.JobRequestId.ToString() + "_" + this.PublicKey, "create");
                    TL.NewMessage(this.PublicKey, broadcastFrame.JobRequest.Header.JobRequestId.ToString() + "_" + this.PublicKey, "encrypts");

                    responseFrame = new ReplyFrame()
                    {
                        EncryptedJobReply = new EncryptedData { Value = encryptedReplyPayload.AsByteString() },
                        SettlementPromise = signedSettlementPromise,
                        ForwardOnion = broadcastFrame.BackwardOnion,
                        NetworkPaymentRequest = new PaymentRequest { Value = networkInvoice },
                    };
                }
                else
                {
                    replyPaymentHash = alreadyBroadcasted.ReplyInvoiceHash;
                    decodedReplyInvoice = JsonSerializer.Deserialize<PaymentRequestRecord>(new MemoryStream(alreadyBroadcasted.DecodedReplyInvoice));
                    replierCertificateId = alreadyBroadcasted.ReplierCertificateId;
                    responseFrame = new ReplyFrame()
                    {
                        EncryptedJobReply = new EncryptedData { Value = alreadyBroadcasted.EncryptedReplyPayload.AsByteString() },
                        SettlementPromise = Crypto.BinaryDeserializeObject<SettlementPromise>(alreadyBroadcasted.SignedSettlementPromise),
                        ForwardOnion = broadcastFrame.BackwardOnion,
                        NetworkPaymentRequest = new PaymentRequest { Value = alreadyBroadcasted.NetworkPaymentRequest },
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
                var decodedNetworkInvoice = WalletAPIResult.Get<PaymentRequestRecord>(await GetWalletClient().DecodeInvoiceAsync(await MakeWalletAuthToken(), responseFrame.NetworkPaymentRequest.Value, cancellationToken));
                if (responseFrame.ForwardOnion.IsEmpty())
                {
                    TL.NewMessage(peerPublicKey, this.PublicKey, "reply");
                    var settlerPubKey = await SettlerSelector.GetPubKeyAsync(responseFrame.SettlementPromise.Header.TheirSecurityCenterUri.AsUri(), CancellationTokenSource.Token);
                    var replyPayload = await responseFrame.DecryptAndVerifyAsync(privateKey, settlerPubKey, this.SettlerSelector, CancellationTokenSource.Token);
                    if (replyPayload == null)
                    {
                        TL.Warning("reply payload mismatch");
                        return;
                    }
                    await _settlerMonitor.MonitorGigStatusAsync(
                        responseFrame.SettlementPromise.Header.MySecurityCenterUri.AsUri(),
                        replyPayload.Header.JobRequest.Header.JobRequestId.AsGuid(),
                        replyPayload.Header.JobReplyId.AsGuid(),
                        Crypto.BinarySerializeObject(replyPayload));

                    var decodedReplyInvoice = WalletAPIResult.Get<PaymentRequestRecord>(await GetWalletClient().DecodeInvoiceAsync(await MakeWalletAuthToken(), replyPayload.Header.JobPaymentRequest.Value, cancellationToken));

                    await this._invoiceStateUpdatesMonitor.MonitorInvoiceAsync(
                        decodedReplyInvoice.PaymentHash,
                        JsonSerializer.SerializeToUtf8Bytes(new InvoiceData()
                        {
                            IsNetworkInvoice = false,
                            Invoice = replyPayload.Header.JobPaymentRequest.Value,
                            PaymentHash = decodedReplyInvoice.PaymentHash,
                            TotalSeconds = (int)InvoicePaymentTimeout.TotalSeconds
                        }));

                    lock (NodeDb.Context)
                    {
                        NodeDb.Context.AddObject(
                        new ReplyPayloadRow()
                        {
                            ReplyId = Guid.NewGuid(),
                            PublicKey = this.PublicKey,
                            SignedRequestPayloadId = replyPayload.Header.JobRequest.Header.JobRequestId.AsGuid(),
                            ReplierCertificateId = replyPayload.Header.JobReplyId.AsGuid(),
                            ReplyInvoice = replyPayload.Header.JobPaymentRequest.Value,
                            DecodedReplyInvoice = JsonSerializer.SerializeToUtf8Bytes(decodedReplyInvoice),
                            NetworkPaymentRequest = responseFrame.NetworkPaymentRequest.Value,
                            DecodedNetworkInvoice = JsonSerializer.SerializeToUtf8Bytes(decodedNetworkInvoice),
                            TheReplyPayload = Crypto.BinarySerializeObject(replyPayload)
                        });
                    }

                    gigGossipNodeEvents.OnNewResponse(this, replyPayload, replyPayload.Header.JobPaymentRequest.Value, decodedReplyInvoice, responseFrame.NetworkPaymentRequest.Value, decodedNetworkInvoice);
                }
                else
                {
                    var topLayerPublicKey = responseFrame.ForwardOnion.Peel(privateKey);
                    if (!await responseFrame.SettlementPromise.VerifyAsync(responseFrame.EncryptedJobReply.Value.ToArray(), this.SettlerSelector, responseFrame.SettlementPromise.Signature, CancellationTokenSource.Token))
                    {
                        TL.Warning("settlement promise mismatch");
                        return;
                    }

                    if (!newResponse)
                    {
                        TL.NewReply(peerPublicKey, this.PublicKey, "reply");
                        var settlerClient = this.SettlerSelector.GetSettlerClient(responseFrame.SettlementPromise.Header.MySecurityCenterUri.AsUri());
                        var settok = await MakeSettlerAuthTokenAsync(responseFrame.SettlementPromise.Header.MySecurityCenterUri.AsUri());

                        if (!SettlerAPIResult.Get<bool>(await settlerClient.ValidateRelatedPaymentHashesAsync(settok,
                            responseFrame.SettlementPromise.Header.NetworkPaymentHash.Value.ToArray().AsHex(),
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

                        var networkInvoice = WalletAPIResult.Get<InvoiceRecord>(await GetWalletClient().AddHodlInvoiceAsync(
                            await this.MakeWalletAuthToken(),
                            decodedNetworkInvoice.Satoshis + this.priceAmountForRouting,
                            relatedNetworkPaymentHash, "", (long)InvoicePaymentTimeout.TotalSeconds, cancellationToken));
                        TL.NewMessage(this.PublicKey, relatedNetworkPaymentHash, "create");
                        await this._invoiceStateUpdatesMonitor.MonitorInvoiceAsync(
                            relatedNetworkPaymentHash,
                            JsonSerializer.SerializeToUtf8Bytes(new InvoiceData()
                            {
                                IsNetworkInvoice = true,
                                Invoice = responseFrame.NetworkPaymentRequest.Value,
                                PaymentHash = decodedNetworkInvoice.PaymentHash,
                                TotalSeconds = (int)InvoicePaymentTimeout.TotalSeconds
                            }));
                        await this._settlerMonitor.MonitorPreimageAsync(
                            responseFrame.SettlementPromise.Header.MySecurityCenterUri.AsUri(),
                            relatedNetworkPaymentHash);
                        responseFrame = responseFrame.Clone();
                        responseFrame.NetworkPaymentRequest = new PaymentRequest { Value = networkInvoice.PaymentRequest };
                    }
                    await SendMessageAsync(topLayerPublicKey, new Frame { Reply = responseFrame }, false, DateTime.UtcNow + InvoicePaymentTimeout);
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
                lock (NodeDb.Context)
                {
                    return TL.Ret(
                        (from rp in NodeDb.Context.ReplyPayloads where rp.PublicKey == this.PublicKey select rp).ToList()
                    );
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

    public List<ReplyPayloadRow> GetReplyPayloads(Guid signedrequestpayloadId)
    {
        using var TL = TRACE.Log().Args(signedrequestpayloadId);
        try
        {
            alreadyBroadcastedSemaphore.Wait();
            try
            {
                lock (NodeDb.Context)
                {
                    return TL.Ret(
                         (from rp in NodeDb.Context.ReplyPayloads where rp.PublicKey == this.PublicKey && rp.SignedRequestPayloadId == signedrequestpayloadId select rp).ToList()
                    );
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

    public List<AcceptedBroadcastRow> GetAcceptedNotCancelledBroadcasts()
    {
        using var TL = TRACE.Log();
        try
        {
            alreadyBroadcastedSemaphore.Wait();
            try
            {
                lock (NodeDb.Context)
                {
                    return TL.Ret(
                        (from rp in NodeDb.Context.AcceptedBroadcasts where rp.PublicKey == this.PublicKey && !rp.Cancelled select rp).ToList()
                    );
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

    public void MarkBroadcastAsCancelled(AcceptedBroadcastRow brd)
    {
        using var TL = TRACE.Log().Args(brd);
        try
        {
            alreadyBroadcastedSemaphore.Wait();
            try
            {
                brd.Cancelled = true;
                lock (NodeDb.Context)
                {
                    NodeDb.Context.SaveObject(brd);
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


    public AcceptedBroadcastRow GetAcceptedBroadcastsByReqestPayloadId(Guid signedrequestpayloadId)
    {
        using var TL = TRACE.Log().Args(signedrequestpayloadId);
        try
        {
            alreadyBroadcastedSemaphore.Wait();
            try
            {
                lock (NodeDb.Context)
                {
                    return TL.Ret(
                         (from rp in NodeDb.Context.AcceptedBroadcasts where rp.PublicKey == this.PublicKey && rp.SignedRequestPayloadId == signedrequestpayloadId select rp).FirstOrDefault()
                    );
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

    public AcceptedBroadcastRow GetAcceptedBroadcastsByReplyInvoiceHash(string replyInvoiceHash)
    {
        using var TL = TRACE.Log().Args(replyInvoiceHash);
        try
        {
            alreadyBroadcastedSemaphore.Wait();
            try
            {
                lock (NodeDb.Context)
                {
                    return TL.Ret(
                        (from rp in NodeDb.Context.AcceptedBroadcasts where rp.PublicKey == this.PublicKey && rp.ReplyInvoiceHash == replyInvoiceHash select rp).FirstOrDefault()
                    );
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

    public async Task OnInvoiceStateChangeAsync(InvoiceState state, byte[] data)
    {
        using var TL = TRACE.Log().Args(state, data);
        try
        {
            var iac = JsonSerializer.Deserialize<InvoiceData>(data);
            TL.NewNote(iac.PaymentHash, state.ToString());
            if (iac.IsNetworkInvoice)
            {
                if (state == InvoiceState.Accepted)
                    this.gigGossipNodeEvents.OnNetworkInvoiceAccepted(this, iac);
                else if (state == InvoiceState.Cancelled)
                    this.gigGossipNodeEvents.OnNetworkInvoiceCancelled(this, iac);
                else if(state == InvoiceState.Settled)
                    gigGossipNodeEvents.OnNetworkInvoiceSettled(this, iac);
            }
            else
            {
                if (state == InvoiceState.Accepted)
                    this.gigGossipNodeEvents.OnJobInvoiceAccepted(this, iac);
                else if (state == InvoiceState.Cancelled)
                    this.gigGossipNodeEvents.OnJobInvoiceCancelled(this, iac);
                else if (state == InvoiceState.Settled)
                    gigGossipNodeEvents.OnJobInvoiceSettled(this, iac);
            }
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }



    public async void OnPaymentStatusChange(PaymentStatus status, byte[] data)
    {
        using var TL = TRACE.Log().Args(status, data);
        try
        {
            var pay = JsonSerializer.Deserialize<PaymentData>(data);
            this.gigGossipNodeEvents.OnPaymentStatusChange(this, status, pay);
            TL.NewMessage(this.PublicKey, pay.PaymentHash, "pay_" + status);
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async void OnLNDNewTransaction(NewTransactionFound newtrans)
    {
        using var TL = TRACE.Log().Args(newtrans);
        try
        {
            this.gigGossipNodeEvents.OnLNDNewTransaction(this, newtrans);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async void OnLNDInvoiceStateChanged(InvoiceStateChange invoice)
    {
        using var TL = TRACE.Log().Args(invoice);
        try
        {
            this.gigGossipNodeEvents.OnLNDInvoiceStateChanged(this, invoice);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async void OnLNDPaymentStatusChanged(PaymentStatusChanged payment)
    {
        using var TL = TRACE.Log().Args(payment);
        try
        {
            this.gigGossipNodeEvents.OnLNDPaymentStatusChanged(this, payment);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async void OnLNDPayoutStateChanged(PayoutStateChanged payout)
    {
        using var TL = TRACE.Log().Args(payout);
        try
        {
            this.gigGossipNodeEvents.OnLNDPayoutStateChanged(this, payout);
        }
        catch (Exception ex)
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
            var replyPayload = Crypto.BinaryDeserializeObject<JobReply>(data);
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
            var replyPayload = Crypto.BinaryDeserializeObject<JobReply>(data);
            gigGossipNodeEvents.OnResponseCancelled(this, replyPayload);
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<LNDWalletErrorCode> MonitorInvoiceAsync(string invoice, string paymentHash, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(invoice, paymentHash);
        try
        {
            if (_paymentStatusUpdatesMonitor.IsPaymentMonitored(paymentHash))
                return TL.Ret(LNDWalletErrorCode.AlreadyPayed);

            await _paymentStatusUpdatesMonitor.MonitorPaymentAsync(paymentHash, JsonSerializer.SerializeToUtf8Bytes(
            new PaymentData()
            {
                Invoice = invoice,
                PaymentHash = paymentHash
            }));
            return TL.Ret(LNDWalletErrorCode.Ok);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }


    public async Task StopMonitoringInvoiceAsync(string paymentHash, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(paymentHash);
        try
        {
            await _paymentStatusUpdatesMonitor.StopPaymentMonitoringAsync(paymentHash);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }


    public async Task<LNDWalletErrorCode> PayInvoiceAsync(string invoice, string paymentHash, long feelimit, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(invoice, paymentHash, feelimit);
        try
        {
            if (_paymentStatusUpdatesMonitor.IsPaymentMonitored(paymentHash))
                return TL.Ret(LNDWalletErrorCode.AlreadyPayed);

            await _paymentStatusUpdatesMonitor.MonitorPaymentAsync(paymentHash, JsonSerializer.SerializeToUtf8Bytes(
            new PaymentData()
            {
                Invoice = invoice,
                PaymentHash = paymentHash
            }));
            var paymentStatus = WalletAPIResult.Status(await GetWalletClient().SendPaymentAsync(await MakeWalletAuthToken(), invoice, (int)this.InvoicePaymentTimeout.TotalSeconds, feelimit, cancellationToken));
            if (paymentStatus != LNDWalletErrorCode.Ok)
            {
                await _paymentStatusUpdatesMonitor.StopPaymentMonitoringAsync(paymentHash);
                TL.Error("invoice payment failed " + paymentStatus.ToString());
                return TL.Ret(paymentStatus);
            }
            else
                return TL.Ret(LNDWalletErrorCode.Ok);
        }
        catch (Exception ex)
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
            lock (NodeDb.Context)
            {
                messageLocks = new ConcurrentDictionary<string, bool>(from m in NodeDb.Context.MessageTransactions where m.PublicKey == this.PublicKey select KeyValuePair.Create(m.MessageId, true));
            }
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

    public override bool CommitMessage(string id, int kind, DateTime createdAt)
    {
        using var TL = TRACE.Log().Args(id);
        try
        {
            lock (NodeDb.Context)
            {
                return TL.Ret(
                NodeDb.Context.TryAddObject(new MessageTransactionRow()
                    {
                        MessageId = id,
                        PublicKey = this.PublicKey,
                        CreatedAt = createdAt,
                        EventKind = kind
                    })
                );
            }
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public override DateTime? GetLastMessageCreatedAt(int kind, int secondsBefore)
    {
        lock (NodeDb.Context)
        {
            var t = (from m in NodeDb.Context.MessageTransactions
                     where m.PublicKey == this.PublicKey && m.EventKind == kind
                     orderby m.CreatedAt descending
                     select m).FirstOrDefault();

            if (t == null)
                return null;
            return t.CreatedAt.AddSeconds(-secondsBefore);
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



    public event EventHandler<DirectMessageEventArgs> OnDirectMessage;

    public override async Task OnMessageAsync(string messageId, string senderPublicKey, Frame frame)
    {
        using var TL = TRACE.Log().Args(messageId, senderPublicKey, frame);
        try
        {
            switch(frame.ValueCase)
            {
                case Frame.ValueOneofCase.Broadcast:
                    await OnBroadcastFrameAsync(messageId, senderPublicKey, frame.Broadcast);
                    break;
                case Frame.ValueOneofCase.CancelBroadcast:
                    await OnCancelBroadcastFrameAsync(messageId, senderPublicKey, frame.CancelBroadcast);
                    break;
                case Frame.ValueOneofCase.Reply:
                    await OnResponseFrameAsync(messageId, senderPublicKey, frame.Reply, false, CancellationTokenSource.Token);
                    break;
                case Frame.ValueOneofCase.Location:
                    OnDirectMessage?.Invoke(this, new DirectMessageEventArgs()
                    {
                        EventId = messageId,
                        SenderPublicKey = senderPublicKey,
                        LocationFrame = frame.Location,
                    });
                    break;
                default:
                    break;
            }
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<BroadcastRequest> BroadcastTopicAsync<T>(T topic, string[] properties, Func<BroadcastRequest, Task> preSend) where T:Google.Protobuf.IMessage<T>
    {
        using var TL = TRACE.Log().Args(topic, properties, preSend);
        try
        {
            var settler = SettlerSelector.GetSettlerClient(mySettlerUri);
            var token = await MakeSettlerAuthTokenAsync(mySettlerUri);
            var topicByte = Crypto.BinarySerializeObject(topic!);
            var baaw64response = SettlerAPIResult.Get<string>(await settler.GenerateRequestPayloadAsync(token, string.Join(",",properties), new FileParameter(new MemoryStream(topicByte)), CancellationTokenSource.Token));
            var response = Convert.FromBase64String(baaw64response);
            var broadcastTopicResponse = Crypto.BinaryDeserializeObject<BroadcastRequest>(response);

            await preSend(broadcastTopicResponse);

            await BroadcastAsync(broadcastTopicResponse.JobRequest);

            return TL.Ret(broadcastTopicResponse);
        }
        catch(Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }


}
