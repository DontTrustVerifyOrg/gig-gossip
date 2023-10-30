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

[Serializable]
public class InvoiceAcceptedData
{
    public required string NetworkInvoice { get; set; }
    public required string NetworkInvoicePaymentHash { get; set; }
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
    public void OnNewResponse(GigGossipNode me, ReplyPayload replyPayload, string replyInvoice, PayReq decodedReplyInvoice, string networkInvoice, PayReq decodedNetworkInvoice);
    public void OnResponseReady(GigGossipNode me, ReplyPayload replyPayload, string key);
    public void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, POWBroadcastFrame broadcastFrame);
    public void OnNetworkInvoiceAccepted(GigGossipNode me, InvoiceAcceptedData iac);
    public void OnInvoiceSettled(GigGossipNode me, Uri serviceUri, string paymentHash, string preimage);
    public void OnPaymentStatusChange(GigGossipNode me, string status, PaymentData paydata);
}

public class AcceptBroadcastResponse
{
    public required Uri SettlerServiceUri { get; set; }
    public required Certificate MyCertificate { get; set; }
    public required byte[] Message { get; set; }
    public required long Fee { get; set; }
}

public interface ISettlerSelector : ICertificationAuthorityAccessor
{
    GigGossipSettlerAPIClient.swaggerClient GetSettlerClient(Uri ServiceUri);
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

    public GigLNDWalletAPIClient.swaggerClient LNDWalletClient;
    Dictionary<Uri, SymmetricKeyRevealClient> settlerSymmetricKeyRevelClients = new();
    Dictionary<Uri, PreimageRevealClient> settlerPreimageRevelClients = new();
    protected Guid _walletToken;
    protected Dictionary<Uri, Guid> _settlerToken;

    public ISettlerSelector SettlerSelector;
    protected LNDWalletMonitor _lndWalletMonitor;
    protected SettlerMonitor _settlerMonitor;

    IGigGossipNodeEvents gigGossipNodeEvents;

    internal ThreadLocal<GigGossipNodeContext> nodeContext;

    public InvoiceStateUpdatesClient InvoiceStateUpdatesClient;
    public PaymentStatusUpdatesClient PaymentStatusUpdatesClient;

    public GigGossipNode(string connectionString, ECPrivKey privKey, int chunkSize, bool deleteDb = false) : base(privKey, chunkSize)
    {
        RegisterFrameType<AskForBroadcastFrame>();
        RegisterFrameType<POWBroadcastConditionsFrame>();
        RegisterFrameType<POWBroadcastFrame>();
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

    public void AddContact(string contactPublicKey, string petname, string relay="")
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
            if(toadd.Count>0)
                this.nodeContext.Value.AddObjectRange(toadd);
        }
    }

    public async Task LoadCertificatesAsync(Uri settler)
    {
        var settlerClient = this.SettlerSelector.GetSettlerClient(settler);
        var authToken = await MakeSettlerAuthTokenAsync(settler);
        var mycerts = new HashSet<Guid>(
            await settlerClient.ListCertificatesAsync(
             authToken, this.PublicKey
            ));

        mycerts.ExceptWith(from c in this.nodeContext.Value.UserCertificates where c.PublicKey == this.PublicKey select c.CertificateId);

        foreach (var cid in mycerts)
        {
            var scert = await settlerClient.GetCertificateAsync(
                 authToken, this.PublicKey,
                cid.ToString()
                );
            this.nodeContext.Value.AddObject(
                new UserCertificate()
                {
                    PublicKey = this.PublicKey,
                    CertificateId = cid,
                    TheCertificate = scert
                });
        }
    }

    public string MakeWalletAuthToken()
    {
        return Crypto.MakeSignedTimedToken(this.privateKey, DateTime.Now, this._walletToken);
    }

    public async Task<string> MakeSettlerAuthTokenAsync(Uri serviceUri)
    {
        Guid? token = null;
        lock (this._settlerToken)
        {
            if (this._settlerToken.ContainsKey(serviceUri))
                token = this._settlerToken[serviceUri];
        }
        if (token == null)
        {
            token = await SettlerSelector.GetSettlerClient(serviceUri).GetTokenAsync(this.PublicKey);
            lock (this._settlerToken)
            {
                this._settlerToken[serviceUri] = token.Value;
            }
        }
        return Crypto.MakeSignedTimedToken(this.privateKey, DateTime.Now, token.Value);
    }

    public async Task<SymmetricKeyRevealClient> GetSymmetricKeyRevealClientAsync(Uri serviceUri)
    {
        SymmetricKeyRevealClient newClient=null;
        lock (settlerSymmetricKeyRevelClients)
        {
            if (!settlerSymmetricKeyRevelClients.ContainsKey(serviceUri))
            {
                newClient = new SymmetricKeyRevealClient(SettlerSelector.GetSettlerClient(serviceUri));
                settlerSymmetricKeyRevelClients[serviceUri] = newClient;
            }
            if(newClient==null)
                return settlerSymmetricKeyRevelClients[serviceUri];
        }
        await newClient.ConnectAsync(await MakeSettlerAuthTokenAsync(serviceUri));
        return newClient;
    }

    public async Task<PreimageRevealClient> GetPreimageRevealClientAsync(Uri serviceUri)
    {
        PreimageRevealClient newClient = null;
        lock (settlerPreimageRevelClients)
        {
            if (!settlerPreimageRevelClients.ContainsKey(serviceUri))
            {
                newClient = new PreimageRevealClient(SettlerSelector.GetSettlerClient(serviceUri));
                settlerPreimageRevelClients[serviceUri] = newClient;
            }
            if (newClient == null)
                return settlerPreimageRevelClients[serviceUri];
        }
        await newClient.ConnectAsync(await MakeSettlerAuthTokenAsync(serviceUri));
        return newClient;
    }

    public List<string> GetBroadcastContactList(Guid payloadId , string? originatorPublicKey)
    {
        var rnd = new Random();
        var contacts = new HashSet<string>(GetContacts());
        var alreadyBroadcasted = (from inc in this.nodeContext.Value.BroadcastHistory where inc.PublicKey == this.PublicKey && inc.PayloadId == payloadId select inc.ContactPublicKey).ToList();
        contacts.ExceptWith(alreadyBroadcasted);
        if (contacts.Count == 0)
            return new List<string>();

        var retcontacts= (from r in contacts.AsEnumerable().OrderBy(x => rnd.Next()).Take(this.fanout) select new BroadcastHistoryRow() { ContactPublicKey=r, PayloadId=payloadId, PublicKey=this.PublicKey});
        this.nodeContext.Value.AddObjectRange(retcontacts);
        if(originatorPublicKey!=null)
        {
            var ogi = (from inc in this.nodeContext.Value.BroadcastHistory where inc.PublicKey == this.PublicKey && inc.PayloadId == payloadId && inc.ContactPublicKey == originatorPublicKey select inc).FirstOrDefault();
            if (ogi == null)
                this.nodeContext.Value.AddObject(new BroadcastHistoryRow() { ContactPublicKey = originatorPublicKey, PayloadId = payloadId, PublicKey = this.PublicKey });
        }
        return (from r in retcontacts select r.ContactPublicKey).ToList();
    }


    public async Task BroadcastAsync(RequestPayload requestPayload,
                        string? originatorPublicKey = null,
                        OnionRoute? backwardOnion = null)
    {

        var tobroadcast = GetBroadcastContactList(requestPayload.PayloadId,originatorPublicKey);
        if (tobroadcast.Count==0)
        {
            Trace.TraceInformation("already broadcasted");
            return;
        }

        foreach (var peerPublicKey in tobroadcast)
        {
            if (peerPublicKey == originatorPublicKey)
                continue;

            AskForBroadcastFrame askForBroadcastFrame = new AskForBroadcastFrame()
            {
                SignedRequestPayload = requestPayload,
                AskId = Guid.NewGuid()
            };

            BroadcastPayload broadcastPayload = new BroadcastPayload()
            {
                SignedRequestPayload = requestPayload,
                BackwardOnion = (backwardOnion ?? new OnionRoute()).Grow(
                    this.PublicKey,
                    peerPublicKey.AsECXOnlyPubKey()),
                Timestamp = null
            };

            if ((from b in this.nodeContext.Value.BroadcastPayloadsByAskId
                 where b.PublicKey == this.PublicKey && b.AskId == askForBroadcastFrame.AskId
                 select b).FirstOrDefault() != null)
                return;

            await this.SendMessageAsync(peerPublicKey, askForBroadcastFrame,true);

            this.nodeContext.Value.AddObject(
                new BroadcastPayloadRow()
                {
                    PublicKey = this.PublicKey,
                    AskId = askForBroadcastFrame.AskId,
                    TheBroadcastPayload = Crypto.SerializeObject(broadcastPayload)
                });
        }
    }

    public async Task OnAskForBroadcastFrameAsync(string messageId, string peerPublicKey, AskForBroadcastFrame askForBroadcastFrame)
    {
        POWBroadcastConditionsFrame powBroadcastConditionsFrame = new POWBroadcastConditionsFrame()
        {
            AskId = askForBroadcastFrame.AskId,
            ValidTill = DateTime.Now.Add(this.broadcastConditionsTimeout),
            WorkRequest = new WorkRequest()
            {
                PowScheme = this.broadcastConditionsPowScheme,
                PowTarget = ProofOfWork.PowTargetFromComplexity(this.broadcastConditionsPowScheme, this.broadcastConditionsPowComplexity)
            },
            TimestampTolerance = this.timestampTolerance
        };

        this.nodeContext.Value.AddObject(
            new POWBroadcastConditionsFrameRow()
            {
                PublicKey = this.PublicKey,
                AskId = powBroadcastConditionsFrame.AskId,
                ThePOWBroadcastConditionsFrame = Crypto.SerializeObject(powBroadcastConditionsFrame)
            });

        await SendMessageAsync(peerPublicKey, powBroadcastConditionsFrame,true);
        MarkMessageAsDone(messageId);
    }

    public async Task OnPOWBroadcastConditionsFrameAsync(string messageId, string peerPublicKey, POWBroadcastConditionsFrame powBroadcastConditionsFrame)
    {
        if (DateTime.Now <= powBroadcastConditionsFrame.ValidTill)
        {
            var brow = (from b in this.nodeContext.Value.BroadcastPayloadsByAskId
                        where b.PublicKey == this.PublicKey && b.AskId == powBroadcastConditionsFrame.AskId
                        select b).FirstOrDefault();

            if (brow != null)
            {
                BroadcastPayload broadcastPayload = Crypto.DeserializeObject<BroadcastPayload>(brow.TheBroadcastPayload);
                broadcastPayload.SetTimestamp(DateTime.Now);
                var pow = powBroadcastConditionsFrame.WorkRequest.ComputeProof(broadcastPayload);    // This will depend on your computeProof method implementation
                POWBroadcastFrame powBroadcastFrame = new POWBroadcastFrame()
                {
                    AskId = powBroadcastConditionsFrame.AskId,
                    BroadcastPayload = broadcastPayload,
                    ProofOfWork = pow
                };
                await SendMessageAsync(peerPublicKey, powBroadcastFrame,true);
                FlowLogger.NewMessage(this.PublicKey, peerPublicKey, "broadcast");
            }
        }
        MarkMessageAsDone(messageId);
    }


    public async Task OnPOWBroadcastFrameAsync(string messageId, string peerPublicKey, POWBroadcastFrame powBroadcastFrame)
    {
        var brow = (from b in this.nodeContext.Value.POWBroadcastConditionsFrameRowByAskId
                    where b.PublicKey == this.PublicKey && b.AskId == powBroadcastFrame.AskId
                    select b).FirstOrDefault();

        if (brow == null)
        {
            MarkMessageAsDone(messageId);
            return;
        }

        var myPowBroadcastConditionFrame = Crypto.DeserializeObject<POWBroadcastConditionsFrame>(brow.ThePOWBroadcastConditionsFrame);

        if (powBroadcastFrame.ProofOfWork.PowScheme != myPowBroadcastConditionFrame.WorkRequest.PowScheme)
        {
            MarkMessageAsDone(messageId);
            return;
        }

        if (powBroadcastFrame.ProofOfWork.PowTarget != myPowBroadcastConditionFrame.WorkRequest.PowTarget)
        {
            MarkMessageAsDone(messageId);
            return;
        }

        if (powBroadcastFrame.BroadcastPayload.Timestamp > DateTime.Now)
        {
            MarkMessageAsDone(messageId);
            return;
        }

        if (powBroadcastFrame.BroadcastPayload.Timestamp + myPowBroadcastConditionFrame.TimestampTolerance < DateTime.Now)
        {
            MarkMessageAsDone(messageId);
            return;
        }

        if (!await powBroadcastFrame.VerifyAsync(SettlerSelector))
        {
            MarkMessageAsDone(messageId);
            return;
        }

        gigGossipNodeEvents.OnAcceptBroadcast(this, peerPublicKey, powBroadcastFrame);
        MarkMessageAsDone(messageId);
    }

    public async Task BroadcastToPeersAsync(string peerPublicKey, POWBroadcastFrame powBroadcastFrame)
    {
        await this.BroadcastAsync(
            requestPayload: powBroadcastFrame.BroadcastPayload.SignedRequestPayload,
            originatorPublicKey: peerPublicKey,
            backwardOnion: powBroadcastFrame.BroadcastPayload.BackwardOnion);
    }

    public async Task AcceptBroadcastAsync(string peerPublicKey, POWBroadcastFrame powBroadcastFrame, AcceptBroadcastResponse acceptBroadcastResponse)
    {
        var abror = (from abx in this.nodeContext.Value.AcceptedBroadcasts
                       where abx.PublicKey == this.PublicKey
                       && abx.ReplierPublicKey == this.PublicKey
                       && abx.PayloadId == powBroadcastFrame.BroadcastPayload.SignedRequestPayload.PayloadId
                       && abx.SettlerServiceUri == acceptBroadcastResponse.SettlerServiceUri
                       select abx).FirstOrDefault();

        ReplyFrame responseFrame;
        if (abror == null)
        {
            FlowLogger.NewMessage(this.PublicKey, Encoding.Default.GetBytes(acceptBroadcastResponse.SettlerServiceUri.AbsoluteUri).AsHex(), "getSecret");
            var settlerClient = this.SettlerSelector.GetSettlerClient(acceptBroadcastResponse.SettlerServiceUri);
            var authToken = await MakeSettlerAuthTokenAsync(acceptBroadcastResponse.SettlerServiceUri);
            var replyPaymentHash = await settlerClient.GenerateReplyPaymentPreimageAsync(authToken, powBroadcastFrame.BroadcastPayload.SignedRequestPayload.PayloadId.ToString(),this.PublicKey);
            var replyInvoice =  (await LNDWalletClient.AddHodlInvoiceAsync(MakeWalletAuthToken(), acceptBroadcastResponse.Fee, replyPaymentHash, "", (long)invoicePaymentTimeout.TotalSeconds)).PaymentRequest;
            FlowLogger.SetupParticipantWithAutoAlias(replyPaymentHash, "I",false);
            FlowLogger.NewMessage(Encoding.Default.GetBytes(acceptBroadcastResponse.SettlerServiceUri.AbsoluteUri).AsHex(), replyPaymentHash, "hash");
            FlowLogger.NewMessage(this.PublicKey, replyPaymentHash, "create");
            await this._settlerMonitor.MonitorPreimageAsync(
                acceptBroadcastResponse.SettlerServiceUri,
                replyPaymentHash);
            var signedRequestPayloadSerialized = Crypto.SerializeObject(powBroadcastFrame.BroadcastPayload.SignedRequestPayload);
            var replierCertificateSerialized = Crypto.SerializeObject(acceptBroadcastResponse.MyCertificate);
            var settr = await settlerClient.GenerateSettlementTrustAsync(authToken, Convert.ToBase64String(acceptBroadcastResponse.Message), replyInvoice, Convert.ToBase64String(signedRequestPayloadSerialized), Convert.ToBase64String(replierCertificateSerialized));
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
                ReplierPublicKey = this.PublicKey,
                PayloadId = powBroadcastFrame.BroadcastPayload.SignedRequestPayload.PayloadId,
                SettlerServiceUri = acceptBroadcastResponse.SettlerServiceUri,
                EncryptedReplyPayload = encryptedReplyPayload,
                NetworkInvoice = networkInvoice,
                SignedSettlementPromise = Crypto.SerializeObject(signedSettlementPromise)
            });

            FlowLogger.SetupParticipantWithAutoAlias(powBroadcastFrame.BroadcastPayload.SignedRequestPayload.PayloadId.ToString()+"_"+this.PublicKey, "K", false);
            FlowLogger.NewMessage(Encoding.Default.GetBytes(acceptBroadcastResponse.SettlerServiceUri.AbsoluteUri).AsHex(), powBroadcastFrame.BroadcastPayload.SignedRequestPayload.PayloadId.ToString() + "_" + this.PublicKey, "create");
            FlowLogger.NewMessage(this.PublicKey, powBroadcastFrame.BroadcastPayload.SignedRequestPayload.PayloadId.ToString() + "_" + this.PublicKey, "encrypts");

            responseFrame = new ReplyFrame()
            {
                EncryptedReplyPayload = encryptedReplyPayload,
                SignedSettlementPromise = signedSettlementPromise,
                ForwardOnion = powBroadcastFrame.BroadcastPayload.BackwardOnion,
                NetworkInvoice = networkInvoice
            };
        }
        else
        {
            responseFrame = new ReplyFrame()
            {
                EncryptedReplyPayload = abror.EncryptedReplyPayload,
                SignedSettlementPromise = Crypto.DeserializeObject<SettlementPromise>(abror.SignedSettlementPromise),
                ForwardOnion = powBroadcastFrame.BroadcastPayload.BackwardOnion,
                NetworkInvoice = abror.NetworkInvoice
            };
        }

        await this.OnResponseFrameAsync(null, peerPublicKey, responseFrame, newResponse: true);
    }

    public async Task OnResponseFrameAsync(string messageId, string peerPublicKey, ReplyFrame responseFrame, bool newResponse = false)
    {
        var decodedNetworkInvoice = await LNDWalletClient.DecodeInvoiceAsync(MakeWalletAuthToken(), responseFrame.NetworkInvoice);
        if (responseFrame.ForwardOnion.IsEmpty())
        {
            FlowLogger.NewMessage(peerPublicKey, this.PublicKey, "reply");
            ReplyPayload replyPayload = await responseFrame.DecryptAndVerifyAsync(privateKey, await SettlerSelector.GetPubKeyAsync(responseFrame.SignedSettlementPromise.ServiceUri), this.SettlerSelector);
            if (replyPayload == null)
            {
                Trace.TraceError("reply payload mismatch");
                if (messageId != null) MarkMessageAsDone(messageId);
                return;
            }
            var payloadId = replyPayload.SignedRequestPayload.PayloadId;

            await _settlerMonitor.MonitorSymmetricKeyAsync(responseFrame.SignedSettlementPromise.ServiceUri, payloadId, replyPayload.ReplierCertificate.PublicKey, Crypto.SerializeObject(replyPayload));

            var decodedReplyInvoice = await LNDWalletClient.DecodeInvoiceAsync(MakeWalletAuthToken(), replyPayload.ReplyInvoice);

            this.nodeContext.Value.AddObject(
                new ReplyPayloadRow()
                {
                    ReplyId = Guid.NewGuid(),
                    PublicKey = this.PublicKey,
                    PayloadId = payloadId,
                    ReplierPublicKey = replyPayload.ReplierCertificate.PublicKey,
                    ReplyInvoice = replyPayload.ReplyInvoice,
                    DecodedReplyInvoice = Crypto.SerializeObject(decodedReplyInvoice),
                    NetworkInvoice = responseFrame.NetworkInvoice,
                    DecodedNetworkInvoice = Crypto.SerializeObject(decodedNetworkInvoice),
                    TheReplyPayload = Crypto.SerializeObject(replyPayload)
                });

            gigGossipNodeEvents.OnNewResponse(this, replyPayload, replyPayload.ReplyInvoice, decodedReplyInvoice, responseFrame.NetworkInvoice, decodedNetworkInvoice);
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
                    Crypto.SerializeObject(new InvoiceAcceptedData()
                    {
                        NetworkInvoice = responseFrame.NetworkInvoice,
                        NetworkInvoicePaymentHash = decodedNetworkInvoice.PaymentHash,
                        TotalSeconds = (int)invoicePaymentTimeout.TotalSeconds
                    }));
                await this._settlerMonitor.MonitorPreimageAsync(
                    responseFrame.SignedSettlementPromise.ServiceUri,
                    relatedNetworkPaymentHash);
                responseFrame = responseFrame.DeepCopy();
                responseFrame.NetworkInvoice = networkInvoice.PaymentRequest;
            }
            await SendMessageAsync(topLayerPublicKey, responseFrame, false, DateTime.Now + invoicePaymentTimeout);
        }
        if (messageId != null) MarkMessageAsDone(messageId);
    }

    public IQueryable<ReplyPayloadRow> GetReplyPayloads(Guid payloadId)
    {
        return (from rp in this.nodeContext.Value.ReplyPayloads where rp.PublicKey==this.PublicKey && rp.PayloadId==payloadId select rp);
    }


    public void OnInvoiceStateChange(string state, byte[] data)
    {
        var iac = Crypto.DeserializeObject<InvoiceAcceptedData>(data);
        FlowLogger.NewEvent(iac.NetworkInvoicePaymentHash, state);
        if (state == "Accepted")
        {
            this.gigGossipNodeEvents.OnNetworkInvoiceAccepted(this, iac);
        }
    }

    public async Task PayNetworkInvoiceAsync(InvoiceAcceptedData iac)
    {
        if (_lndWalletMonitor.IsPaymentMonitored(iac.NetworkInvoicePaymentHash))
            return;
        await _lndWalletMonitor.MonitorPaymentAsync(iac.NetworkInvoicePaymentHash, Crypto.SerializeObject(
            new PaymentData()
            {
                Invoice = iac.NetworkInvoice,
                PaymentHash = iac.NetworkInvoicePaymentHash
            }));
        await LNDWalletClient.SendPaymentAsync(
            MakeWalletAuthToken(), iac.NetworkInvoice, iac.TotalSeconds
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
        ReplyPayload replyPayload = Crypto.DeserializeObject<ReplyPayload>(data);
        FlowLogger.NewMessage(this.PublicKey, replyPayload.SignedRequestPayload.PayloadId.ToString() + "_" + replyPayload.ReplierCertificate.PublicKey, "decrypt");
        gigGossipNodeEvents.OnResponseReady(this, replyPayload, key);
    }

    public async Task AcceptResponseAsync(ReplyPayload replyPayload, string replyInvoice, PayReq decodedReplyInvoice, string networkInvoice, PayReq decodedNetworkInvoice)
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

        if (frame is AskForBroadcastFrame)
        {
            await OnAskForBroadcastFrameAsync(messageId, senderPublicKey, (AskForBroadcastFrame)frame);
        }
        else if (frame is POWBroadcastConditionsFrame)
        {
            await OnPOWBroadcastConditionsFrameAsync(messageId, senderPublicKey, (POWBroadcastConditionsFrame)frame);
        }
        else if (frame is POWBroadcastFrame)
        {
            await OnPOWBroadcastFrameAsync(messageId, senderPublicKey, (POWBroadcastFrame)frame);
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

    public async Task<Guid> BroadcastTopicAsync<T>(T topic, Certificate certificate)
    {
        var topicId = Guid.NewGuid();
        var t = new RequestPayload()
        {
            PayloadId = topicId,
            Topic = Crypto.SerializeObject(topic),
            SenderCertificate = certificate
        };
        t.Sign(this.privateKey);
        await this.BroadcastAsync(t);
        return topicId;
    }

}
