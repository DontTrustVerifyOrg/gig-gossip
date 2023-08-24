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

public interface IGigGossipNodeEvents
{
    public void OnNewResponse(GigGossipNode me, ReplyPayload replyPayload, string networkInvoice);
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
        var settlerClient = this.SettlerSelector.GetSettlerClient(acceptBroadcastResponse.SettlerServiceUri);
        var authToken = MakeSettlerAuthTokenAsync(acceptBroadcastResponse.SettlerServiceUri).Result;
        var replyPaymentHash = settlerClient.GenerateReplyPaymentPreimageAsync(authToken, powBroadcastFrame.BroadcastPayload.SignedRequestPayload.PayloadId.ToString()).Result;
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

        var responseFrame = new ReplyFrame()
        {
            EncryptedReplyPayload = encryptedReplyPayload,
            SignedSettlementPromise = signedSettlementPromise,
            ForwardOnion = powBroadcastFrame.BroadcastPayload.BackwardOnion,
            NetworkInvoice = networkInvoice
        };

        this.OnResponseFrame(null, peerPublicKey, responseFrame, newResponse: true);
    }

    public void OnResponseFrame(string messageId, string peerPublicKey, ReplyFrame responseFrame, bool newResponse = false)
    {
        var decodedInvoice = LNDWalletClient.DecodeInvoiceAsync(MakeWalletAuthToken(), responseFrame.NetworkInvoice).Result;
        if (responseFrame.ForwardOnion.IsEmpty())
        {
            if (!responseFrame.SignedSettlementPromise.NetworkPaymentHash.AsHex().SequenceEqual(decodedInvoice.PaymentHash))
            {
                Trace.TraceError("reply payload has different network_payment_hash than network_invoice");
                if(messageId!=null) MarkMessageAsDone(messageId);
                return;
            }

            ReplyPayload replyPayload = responseFrame.DecryptAndVerify(privateKey, SettlerSelector.GetPubKey(responseFrame.SignedSettlementPromise.ServiceUri), this.SettlerSelector);
            if (replyPayload == null)
            {
                Trace.TraceError("reply payload mismatch");
                if (messageId != null) MarkMessageAsDone(messageId);
                return;
            }
            var payloadId = replyPayload.SignedRequestPayload.PayloadId;

            settlerMonitor.MonitorSymmetricKey(responseFrame.SignedSettlementPromise.ServiceUri, payloadId, Crypto.SerializeObject(replyPayload));

            gigGossipNodeEvents.OnNewResponse(this, replyPayload, responseFrame.NetworkInvoice);

            this.nodeContext.Value.AddObject(
                new ReplyPayloadRow()
                {
                    ReplyId = Guid.NewGuid(),
                    PublicKey = this.PublicKey,
                    PayloadId = payloadId,
                    ReplierPublicKey = replyPayload.ReplierCertificate.PublicKey,
                    NetworkInvoice = responseFrame.NetworkInvoice,
                    TheReplyPayload = Crypto.SerializeObject(replyPayload)
                });
        }
        else
        {
            var topLayerPulicKey = responseFrame.ForwardOnion.Peel(privateKey);
            if (this.GetContacts().Contains(topLayerPulicKey))
            {
                if (!responseFrame.SignedSettlementPromise.Verify(responseFrame.EncryptedReplyPayload, this.SettlerSelector))
                {
                    if (messageId != null) MarkMessageAsDone(messageId);
                    return;
                }
                if (!responseFrame.SignedSettlementPromise.NetworkPaymentHash.AsHex().SequenceEqual(decodedInvoice.PaymentHash))
                {
                    if (messageId != null) MarkMessageAsDone(messageId);
                    return;
                }
                if (!newResponse)
                {
                    var settlerClient = this.SettlerSelector.GetSettlerClient(responseFrame.SignedSettlementPromise.ServiceUri);
                    var relatedNetworkPaymentHash = settlerClient.GenerateRelatedPreimageAsync(MakeSettlerAuthTokenAsync(responseFrame.SignedSettlementPromise.ServiceUri).Result, decodedInvoice.PaymentHash).Result;
                    var networkInvoice = LNDWalletClient.AddHodlInvoiceAsync(
                        this.MakeWalletAuthToken(),
                        decodedInvoice.NumSatoshis + this.priceAmountForRouting,
                        relatedNetworkPaymentHash, "", (long)invoicePaymentTimeout.TotalSeconds).Result;
                    this.lndWalletMonitor.MonitorInvoice(
                        relatedNetworkPaymentHash, "Accepted",
                        Crypto.SerializeObject(new InvoiceAcceptedData()
                        {
                            NetworkInvoice = responseFrame.NetworkInvoice,
                            TotalSeconds = (int)invoicePaymentTimeout.TotalSeconds
                        }));
                    this.settlerMonitor.MonitorPreimage(
                        responseFrame.SignedSettlementPromise.ServiceUri,
                        relatedNetworkPaymentHash);
                    responseFrame = responseFrame.DeepCopy();
                    responseFrame.NetworkInvoice = networkInvoice.PaymentRequest;
                }
                SendMessage(topLayerPulicKey, responseFrame);
            }
        }
        if (messageId != null) MarkMessageAsDone(messageId);
    }

    [Serializable]
    public class InvoiceAcceptedData
    {
        public string NetworkInvoice { get; set; }
        public int TotalSeconds { get; set; }
    }

    public void OnInvoiceStateChange(byte[] data)
    {
        var iac = Crypto.DeserializeObject<InvoiceAcceptedData>(data);
        LNDWalletClient.SendPaymentAsync(
            MakeWalletAuthToken(), iac.NetworkInvoice, iac.TotalSeconds
            ).Wait();
    }

    public void OnPreimageRevealed(string preimage)
    {
        LNDWalletClient.SettleInvoiceAsync(
            MakeWalletAuthToken(),
            preimage
            ).Wait();
    }

    public void OnSymmetricKeyRevealed(byte[] data, string key)
    {
        ReplyPayload replyPayload = Crypto.DeserializeObject<ReplyPayload>(data);
        gigGossipNodeEvents.OnResponseReady(this, replyPayload, key);
    }

    public void AcceptResponse(ReplyPayload replyPayload, string networkInvoice)
    {
        LNDWalletClient.SendPaymentAsync(MakeWalletAuthToken(), networkInvoice, (int)this.invoicePaymentTimeout.TotalSeconds).Wait();
        LNDWalletClient.SendPaymentAsync(MakeWalletAuthToken(), replyPayload.ReplyInvoice, (int)this.invoicePaymentTimeout.TotalSeconds).Wait();
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
