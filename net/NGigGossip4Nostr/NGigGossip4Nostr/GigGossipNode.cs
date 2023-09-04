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

public interface IGigGossipNodeEvents
{
    public void OnNewResponse(GigGossipNode me, ReplyPayload replyPayload, string replyInvoice, PayReq decodedReplyInvoice, string networkInvoice, PayReq decodedNetworkInvoice);
    public void OnResponseReady(GigGossipNode me, ReplyPayload replyPayload, string key);
    public void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, POWBroadcastFrame broadcastFrame);
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
    protected Guid _walletToken;
    protected Dictionary<Uri, Guid> _settlerToken;

    public ISettlerSelector SettlerSelector;
    protected LNDWalletMonitor lndWalletMonitor;
    protected SettlerMonitor settlerMonitor;

    IGigGossipNodeEvents gigGossipNodeEvents;

    internal ThreadLocal<GigGossipNodeContext> nodeContext;

    public GigGossipNode(string connectionString, ECPrivKey privKey, string[] nostrRelays, int chunkSize, bool deleteDb = false) : base(privKey, nostrRelays, chunkSize)
    {
        this.nodeContext = new ThreadLocal<GigGossipNodeContext>(() => new GigGossipNodeContext(connectionString.Replace("$HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))));
        if (deleteDb)
            nodeContext.Value.Database.EnsureDeleted();
        nodeContext.Value.Database.EnsureCreated();
    }

    public void Init(int fanout, long priceAmountForRouting, TimeSpan broadcastConditionsTimeout, string broadcastConditionsPowScheme,
                           int broadcastConditionsPowComplexity, TimeSpan timestampTolerance, TimeSpan invoicePaymentTimeout,
                           GigLNDWalletAPIClient.swaggerClient lndWalletClient, ISettlerSelector settlerClientSelector)
    {
        this.fanout = fanout;
        this.priceAmountForRouting = priceAmountForRouting;
        this.broadcastConditionsTimeout = broadcastConditionsTimeout;
        this.broadcastConditionsPowScheme = broadcastConditionsPowScheme;
        this.broadcastConditionsPowComplexity = broadcastConditionsPowComplexity;
        this.timestampTolerance = timestampTolerance;
        this.invoicePaymentTimeout = invoicePaymentTimeout;

        this.LNDWalletClient = lndWalletClient;
        this._walletToken = lndWalletClient.GetTokenAsync(this.PublicKey).Result;
        this.SettlerSelector = settlerClientSelector;
        this._settlerToken = new();
        this.lndWalletMonitor = new LNDWalletMonitor(this);
        this.settlerMonitor = new SettlerMonitor(this);
        this.LoadContactList();
    }

    public void Start(IGigGossipNodeEvents gigGossipNodeEvents)
    {
        this.gigGossipNodeEvents = gigGossipNodeEvents;
        base.Start();
        this.lndWalletMonitor.Start();
        this.settlerMonitor.Start();
    }

    public override void Stop()
    {
        base.Stop();
        this.lndWalletMonitor.Stop();
        this.settlerMonitor.Stop();
    }


    Dictionary<string, NostrContact> _contactList = new();

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

    public void PublishContactList()
    {
        lock (_contactList)
        {
            this.PublishContactList(_contactList);
        }
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

    public void LoadCertificates(Uri settler)
    {
        var settlerClient = this.SettlerSelector.GetSettlerClient(settler);
        var authToken = MakeSettlerAuthTokenAsync(settler).Result;
        var mycerts = new HashSet<Guid>(settlerClient.ListCertificatesAsync(
             authToken, this.PublicKey
            ).Result);

        mycerts.ExceptWith(from c in this.nodeContext.Value.UserCertificates where c.PublicKey == this.PublicKey select c.CertificateId);

        foreach (var cid in mycerts)
        {
            var scert = settlerClient.GetCertificateAsync(
                 authToken, this.PublicKey,
                cid.ToString()
                ).Result;
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

    public bool IncrementBroadcasted(Guid payloadId)
    {
        var myInc = (from inc in this.nodeContext.Value.BroadcastCounters where inc.PublicKey == this.PublicKey && inc.PayloadId == payloadId select inc).FirstOrDefault();
        if (myInc == null)
        {
            this.nodeContext.Value.AddObject(new BroadcastCounterRow() { PublicKey = this.PublicKey, PayloadId = payloadId, Counter = 1 });
            return true;
        }
        else
        {
            if (myInc.Counter < this.fanout)
            {
                myInc.Counter += 1;
                this.nodeContext.Value.SaveObject(myInc);
                return true;
            }
            else
                return false;
        }
    }

    public bool CanIncrementBroadcast(Guid payloadId)
    {
        var myInc = (from inc in this.nodeContext.Value.BroadcastCounters where inc.PublicKey == this.PublicKey && inc.PayloadId == payloadId select inc).FirstOrDefault();
        if (myInc == null)
            return true;
        else
            return myInc.Counter < this.fanout;
    }


    public void Broadcast(RequestPayload requestPayload,
                        string? originatorPublicKey = null,
                        OnionRoute? backwardOnion = null)
    {

        if (!this.IncrementBroadcasted(requestPayload.PayloadId))
        {
            Trace.TraceInformation("already broadcasted");
            return;
        }

        foreach (var peerPublicKey in this.GetContacts())
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

            this.SendMessage(peerPublicKey, askForBroadcastFrame);

            this.nodeContext.Value.AddObject(
                new BroadcastPayloadRow()
                {
                    PublicKey = this.PublicKey,
                    AskId = askForBroadcastFrame.AskId,
                    TheBroadcastPayload = Crypto.SerializeObject(broadcastPayload)
                });
        }
    }

    public void OnAskForBroadcastFrame(string messageId, string peerPublicKey, AskForBroadcastFrame askForBroadcastFrame)
    {
        if (!CanIncrementBroadcast(askForBroadcastFrame.SignedRequestPayload.PayloadId))
        {
            Trace.TraceInformation("already broadcasted, don't ask");
            MarkMessageAsDone(messageId);
            return;
        }

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

        SendMessage(peerPublicKey, powBroadcastConditionsFrame);
        MarkMessageAsDone(messageId);
    }

    public void OnPOWBroadcastConditionsFrame(string messageId, string peerPublicKey, POWBroadcastConditionsFrame powBroadcastConditionsFrame)
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
                SendMessage(peerPublicKey, powBroadcastFrame);
                FlowLogger.NewMessage(this.PublicKey, peerPublicKey, "broadcast");
            }
        }
        MarkMessageAsDone(messageId);
    }


    public void OnPOWBroadcastFrame(string messageId, string peerPublicKey, POWBroadcastFrame powBroadcastFrame)
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

        if (!powBroadcastFrame.Verify(SettlerSelector))
        {
            MarkMessageAsDone(messageId);
            return;
        }

        gigGossipNodeEvents.OnAcceptBroadcast(this, peerPublicKey, powBroadcastFrame);
        MarkMessageAsDone(messageId);
    }

    public void BroadcastToPeers(string peerPublicKey, POWBroadcastFrame powBroadcastFrame)
    {
        this.Broadcast(
            requestPayload: powBroadcastFrame.BroadcastPayload.SignedRequestPayload,
            originatorPublicKey: peerPublicKey,
            backwardOnion: powBroadcastFrame.BroadcastPayload.BackwardOnion);
    }

    public void AcceptBraodcast(string peerPublicKey, POWBroadcastFrame powBroadcastFrame, AcceptBroadcastResponse acceptBroadcastResponse)
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
            var settlerClient = this.SettlerSelector.GetSettlerClient(acceptBroadcastResponse.SettlerServiceUri);
            var authToken = MakeSettlerAuthTokenAsync(acceptBroadcastResponse.SettlerServiceUri).Result;
            var replyPaymentHash = settlerClient.GenerateReplyPaymentPreimageAsync(authToken, powBroadcastFrame.BroadcastPayload.SignedRequestPayload.PayloadId.ToString(),this.PublicKey).Result;
            var replyInvoice = (LNDWalletClient.AddHodlInvoiceAsync(MakeWalletAuthToken(), acceptBroadcastResponse.Fee, replyPaymentHash, "", (long)invoicePaymentTimeout.TotalSeconds)).Result.PaymentRequest;
            this.settlerMonitor.MonitorPreimage(
                acceptBroadcastResponse.SettlerServiceUri,
                replyPaymentHash);
            var signedRequestPayloadSerialized = Crypto.SerializeObject(powBroadcastFrame.BroadcastPayload.SignedRequestPayload);
            var replierCertificateSerialized = Crypto.SerializeObject(acceptBroadcastResponse.MyCertificate);
            var settr = settlerClient.GenerateSettlementTrustAsync(authToken, Convert.ToBase64String(acceptBroadcastResponse.Message), replyInvoice, Convert.ToBase64String(signedRequestPayloadSerialized), Convert.ToBase64String(replierCertificateSerialized)).Result;
            var settlementTrust = Crypto.DeserializeObject<SettlementTrust>(Convert.FromBase64String(settr));

            var signedSettlementPromise = settlementTrust.SettlementPromise;
            var networkInvoice = settlementTrust.NetworkInvoice;
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

        this.OnResponseFrame(null, peerPublicKey, responseFrame, newResponse: true);
    }

    public void OnResponseFrame(string messageId, string peerPublicKey, ReplyFrame responseFrame, bool newResponse = false)
    {
        var decodedNetworkInvoice = LNDWalletClient.DecodeInvoiceAsync(MakeWalletAuthToken(), responseFrame.NetworkInvoice).Result;
        if (responseFrame.ForwardOnion.IsEmpty())
        {
            FlowLogger.NewMessage(peerPublicKey, this.PublicKey, "reply");
            ReplyPayload replyPayload = responseFrame.DecryptAndVerify(privateKey, SettlerSelector.GetPubKey(responseFrame.SignedSettlementPromise.ServiceUri), this.SettlerSelector);
            if (replyPayload == null)
            {
                Trace.TraceError("reply payload mismatch");
                if (messageId != null) MarkMessageAsDone(messageId);
                return;
            }
            var payloadId = replyPayload.SignedRequestPayload.PayloadId;

            settlerMonitor.MonitorSymmetricKey(responseFrame.SignedSettlementPromise.ServiceUri, payloadId, replyPayload.ReplierCertificate.PublicKey, Crypto.SerializeObject(replyPayload));

            var decodedReplyInvoice = LNDWalletClient.DecodeInvoiceAsync(MakeWalletAuthToken(), replyPayload.ReplyInvoice).Result;

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
            FlowLogger.NewReply(peerPublicKey, this.PublicKey, "reply");
            var topLayerPublicKey = responseFrame.ForwardOnion.Peel(privateKey);
            if (!responseFrame.SignedSettlementPromise.Verify(responseFrame.EncryptedReplyPayload, this.SettlerSelector))
            {
                if (messageId != null) MarkMessageAsDone(messageId);
                return;
            }
            if (!newResponse)
            {
                var settlerClient = this.SettlerSelector.GetSettlerClient(responseFrame.SignedSettlementPromise.ServiceUri);
                var settok = MakeSettlerAuthTokenAsync(responseFrame.SignedSettlementPromise.ServiceUri).Result;

                if (!settlerClient.ValidateRelatedPaymentHashesAsync(settok,
                    responseFrame.SignedSettlementPromise.NetworkPaymentHash.AsHex(),
                    decodedNetworkInvoice.PaymentHash).Result)
                {
                    if (messageId != null) MarkMessageAsDone(messageId);
                    return;
                }

                var relatedNetworkPaymentHash = settlerClient.GenerateRelatedPreimageAsync(
                    settok,
                    decodedNetworkInvoice.PaymentHash).Result;

                var networkInvoice = LNDWalletClient.AddHodlInvoiceAsync(
                    this.MakeWalletAuthToken(),
                    decodedNetworkInvoice.NumSatoshis + this.priceAmountForRouting,
                    relatedNetworkPaymentHash, "", (long)invoicePaymentTimeout.TotalSeconds).Result;
                this.lndWalletMonitor.MonitorInvoice(
                    relatedNetworkPaymentHash,
                    Crypto.SerializeObject(new InvoiceAcceptedData()
                    {
                        NetworkInvoice = responseFrame.NetworkInvoice,
                        NetworkInvoicePaymentHash = decodedNetworkInvoice.PaymentHash,
                        TotalSeconds = (int)invoicePaymentTimeout.TotalSeconds
                    }));
                this.settlerMonitor.MonitorPreimage(
                    responseFrame.SignedSettlementPromise.ServiceUri,
                    relatedNetworkPaymentHash);
                responseFrame = responseFrame.DeepCopy();
                responseFrame.NetworkInvoice = networkInvoice.PaymentRequest;
            }
            SendMessage(topLayerPublicKey, responseFrame);
        }
        if (messageId != null) MarkMessageAsDone(messageId);
    }

    public IQueryable<ReplyPayloadRow> GetReplyPayloads(Guid payloadId)
    {
        return (from rp in this.nodeContext.Value.ReplyPayloads where rp.PublicKey==this.PublicKey && rp.PayloadId==payloadId select rp);
    }

    [Serializable]
    public class InvoiceAcceptedData
    {
        public string NetworkInvoice { get; set; }
        public string NetworkInvoicePaymentHash { get; set; }
        public int TotalSeconds { get; set; }
    }

    public void OnInvoiceStateChange(string state, byte[] data)
    {
        if (state == "Accepted")
        {
            var iac = Crypto.DeserializeObject<InvoiceAcceptedData>(data);
            if (lndWalletMonitor.IsPaymentMonitored(iac.NetworkInvoicePaymentHash))
                return;
            LNDWalletClient.SendPaymentAsync(
                MakeWalletAuthToken(), iac.NetworkInvoice, iac.TotalSeconds
                ).Wait();
            lndWalletMonitor.MonitorPayment(iac.NetworkInvoicePaymentHash, new byte[] { });
        }
    }

    public void OnPaymentStatusChange(string statys, byte[] data)
    {
    }

    public void OnPreimageRevealed(string preimage)
    {
        try
        {
            LNDWalletClient.SettleInvoiceAsync(
                MakeWalletAuthToken(),
                preimage
                ).Wait();
        }
        catch(Exception ex)
        {//invoice was not accepted or was cancelled
         //            Trace.TraceError(ex.ToString());
            Trace.TraceInformation("Invoice cannot be settled");
        }
    }

    public void OnSymmetricKeyRevealed(byte[] data, string key)
    {
        ReplyPayload replyPayload = Crypto.DeserializeObject<ReplyPayload>(data);
        gigGossipNodeEvents.OnResponseReady(this, replyPayload, key);
    }

    public void AcceptResponse(ReplyPayload replyPayload, string replyInvoice, PayReq decodedReplyInvoice, string networkInvoice, PayReq decodedNetworkInvoice)
    {
        if (!lndWalletMonitor.IsPaymentMonitored(decodedNetworkInvoice.PaymentHash))
        {
            LNDWalletClient.SendPaymentAsync(MakeWalletAuthToken(), networkInvoice, (int)this.invoicePaymentTimeout.TotalSeconds).Wait();
            lndWalletMonitor.MonitorPayment(decodedNetworkInvoice.PaymentHash, new byte[] { });
        }

        if (!lndWalletMonitor.IsPaymentMonitored(decodedReplyInvoice.PaymentHash))
        {
            LNDWalletClient.SendPaymentAsync(MakeWalletAuthToken(), replyInvoice, (int)this.invoicePaymentTimeout.TotalSeconds).Wait();
            lndWalletMonitor.MonitorPayment(decodedReplyInvoice.PaymentHash, new byte[] { });
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

    public override void OnMessage(string messageId, string senderPublicKey, object frame)
    {
        if (IsMessageDone(messageId))
            return; //Already Processed

        if (frame is AskForBroadcastFrame)
        {
            OnAskForBroadcastFrame(messageId, senderPublicKey, (AskForBroadcastFrame)frame);
        }
        else if (frame is POWBroadcastConditionsFrame)
        {
            OnPOWBroadcastConditionsFrame(messageId, senderPublicKey, (POWBroadcastConditionsFrame)frame);
        }
        else if (frame is POWBroadcastFrame)
        {
            OnPOWBroadcastFrame(messageId, senderPublicKey, (POWBroadcastFrame)frame);
        }
        else if (frame is ReplyFrame)
        {
            OnResponseFrame(messageId, senderPublicKey, (ReplyFrame)frame);
        }
        else
        {
            Trace.TraceError("unknown request: ", senderPublicKey, frame);
        }

    }

    public Guid BroadcastTopic<T>(T topic, Certificate certificate)
    {
        var topicId = Guid.NewGuid();
        var t = new RequestPayload()
        {
            PayloadId = topicId,
            Topic = Crypto.SerializeObject(topic),
            SenderCertificate = certificate
        };
        t.Sign(this.privateKey);
        this.Broadcast(t);
        return topicId;
    }

}
