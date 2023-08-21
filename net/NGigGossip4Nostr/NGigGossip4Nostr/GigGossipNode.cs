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

public class GigGossipNode : NostrNode
{
    protected long priceAmountForRouting;
    protected TimeSpan broadcastConditionsTimeout;
    protected string broadcastConditionsPowScheme;
    protected int broadcastConditionsPowComplexity;
    protected TimeSpan timestampTolerance;
    protected TimeSpan invoicePaymentTimeout;
    protected Dictionary<Guid, BroadcastPayload> _broadcastPayloadsByAskId;
    protected Dictionary<Guid, POWBroadcastConditionsFrame> _myPowBrCondByAskId;
    protected Dictionary<Guid, int> _alreadyBroadcastedRequestPayloadIds;
    protected Dictionary<Guid, Dictionary<string, List<Tuple<ReplyPayload, string>>>> replyPayloads;
    public GigLNDWalletAPIClient.swaggerClient LNDWalletClient;
    protected Guid _walletToken;
    protected Dictionary<Uri,Guid> _settlerToken;
    public Dictionary<Guid, Certificate> Certificates;

    public ISettlerSelector SettlerSelector;
    protected LNDWalletMonitor lndWalletMonitor;
    protected SettlerMonitor settlerMonitor;

    IGigGossipNodeEvents gigGossipNodeEvents;

    public GigGossipNode( ECPrivKey privKey, string[] nostrRelays, int chunkSize) : base(privKey, nostrRelays, chunkSize)
    {
    }

    public void Init(long priceAmountForRouting, TimeSpan broadcastConditionsTimeout, string broadcastConditionsPowScheme,
                           int broadcastConditionsPowComplexity, TimeSpan timestampTolerance, TimeSpan invoicePaymentTimeout,
                           GigLNDWalletAPIClient.swaggerClient lndWalletClient, ISettlerSelector settlerClientSelector)
    {
        this.priceAmountForRouting = priceAmountForRouting;
        this.broadcastConditionsTimeout = broadcastConditionsTimeout;
        this.broadcastConditionsPowScheme = broadcastConditionsPowScheme;
        this.broadcastConditionsPowComplexity = broadcastConditionsPowComplexity;
        this.timestampTolerance = timestampTolerance;
        this.invoicePaymentTimeout = invoicePaymentTimeout;

        this._broadcastPayloadsByAskId = new();
        this._myPowBrCondByAskId = new();
        this._alreadyBroadcastedRequestPayloadIds = new();
        this.replyPayloads = new();
        this.LNDWalletClient = lndWalletClient;
        this._walletToken =  lndWalletClient.GetTokenAsync(this.PublicKey).Result;
        this.SettlerSelector = settlerClientSelector;
        this._settlerToken = new();
        this.lndWalletMonitor = new LNDWalletMonitor(this);
         this.lndWalletMonitor.Start();
        this.settlerMonitor = new SettlerMonitor(this);
        this.settlerMonitor.Start();
        this.Certificates = new();
    }

    public void Start(IGigGossipNodeEvents gigGossipNodeEvents)
    {
        this.gigGossipNodeEvents = gigGossipNodeEvents;
        base.Start();
    }

    public void LoadCertificates(Uri settler)
    {
        var settlerClient = this.SettlerSelector.GetSettlerClient(settler);
        var authToken = MakeSettlerAuthTokenAsync(settler).Result;
        var mycerts =  settlerClient.ListCertificatesAsync(
             authToken, this.PublicKey
            ).Result;

        foreach (var cid in mycerts)
        {
            var scert =  settlerClient.GetCertificateAsync(
                 authToken, this.PublicKey,
                cid.ToString()
                ).Result;
            Certificates[cid] = Crypto.DeserializeObject<Certificate>(scert);
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

    public void IncrementBroadcasted(Guid payloadId)
    {
        if (!_alreadyBroadcastedRequestPayloadIds.ContainsKey(payloadId))
            _alreadyBroadcastedRequestPayloadIds[payloadId] = 0;
        _alreadyBroadcastedRequestPayloadIds[payloadId] += 1;
    }

    public bool CanIncrementBroadcast(Guid payloadId)
    {
        if (!_alreadyBroadcastedRequestPayloadIds.ContainsKey(payloadId))
            return true;
        return _alreadyBroadcastedRequestPayloadIds[payloadId] <= 2;
    }

    public void Broadcast(RequestPayload requestPayload,
                          string? originatorPublicKey = null,
                          OnionRoute? backwardOnion = null)
    {

        this.IncrementBroadcasted(requestPayload.PayloadId);

        if (!this.CanIncrementBroadcast(requestPayload.PayloadId))
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

            this._broadcastPayloadsByAskId[askForBroadcastFrame.AskId] = broadcastPayload;
            this.SendMessage(peerPublicKey, askForBroadcastFrame);
        }
    }

    public void OnAskForBroadcastFrame(string peerPublicKey, AskForBroadcastFrame askForBroadcastFrame)
    {
        if (!CanIncrementBroadcast(askForBroadcastFrame.SignedRequestPayload.PayloadId))
        {
            Trace.TraceInformation("already broadcasted, don't ask");
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

        _myPowBrCondByAskId[powBroadcastConditionsFrame.AskId] = powBroadcastConditionsFrame;
        SendMessage(peerPublicKey, powBroadcastConditionsFrame);
    }

    public void OnPOWBroadcastConditionsFrame(string peerPublicKey, POWBroadcastConditionsFrame powBroadcastConditionsFrame)
    {
        if (DateTime.Now <= powBroadcastConditionsFrame.ValidTill)
        {
            if (_broadcastPayloadsByAskId.ContainsKey(powBroadcastConditionsFrame.AskId))
            {
                BroadcastPayload broadcastPayload = _broadcastPayloadsByAskId[powBroadcastConditionsFrame.AskId];
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
    }


    public void OnPOWBroadcastFrame(string peerPublicKey, POWBroadcastFrame powBroadcastFrame)
    {
        if (!_myPowBrCondByAskId.ContainsKey(powBroadcastFrame.AskId))
            return;

        var myPowBroadcastConditionFrame = _myPowBrCondByAskId[powBroadcastFrame.AskId];

        if (powBroadcastFrame.ProofOfWork.PowScheme != myPowBroadcastConditionFrame.WorkRequest.PowScheme)
            return;

        if (powBroadcastFrame.ProofOfWork.PowTarget != myPowBroadcastConditionFrame.WorkRequest.PowTarget)
            return;

        if (powBroadcastFrame.BroadcastPayload.Timestamp > DateTime.Now)
            return;

        if (powBroadcastFrame.BroadcastPayload.Timestamp + myPowBroadcastConditionFrame.TimestampTolerance < DateTime.Now)
            return;

        if (!powBroadcastFrame.Verify(SettlerSelector))
            return;

        gigGossipNodeEvents.OnAcceptBroadcast(this, peerPublicKey, powBroadcastFrame);
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
        var replyPaymentHash =  settlerClient.GenerateReplyPaymentPreimageAsync( authToken, powBroadcastFrame.BroadcastPayload.SignedRequestPayload.PayloadId.ToString()).Result;
        var replyInvoice = ( LNDWalletClient.AddHodlInvoiceAsync(MakeWalletAuthToken(), acceptBroadcastResponse.Fee, replyPaymentHash, "", (long)invoicePaymentTimeout.TotalSeconds)).Result.PaymentRequest;
        this.settlerMonitor.MonitorPreimage(
            acceptBroadcastResponse.SettlerServiceUri,
            replyPaymentHash,
            (preimage) =>
            {
                LNDWalletClient.SettleInvoiceAsync(
                    MakeWalletAuthToken(),
                    preimage
                    ).Wait();
            });
        var signedRequestPayloadSerialized = Crypto.SerializeObject(powBroadcastFrame.BroadcastPayload.SignedRequestPayload);
        var replierCertificateSerialized = Crypto.SerializeObject(acceptBroadcastResponse.MyCertificate);
        var settr =  settlerClient.GenerateSettlementTrustAsync(authToken, Convert.ToBase64String(acceptBroadcastResponse.Message), replyInvoice, Convert.ToBase64String(signedRequestPayloadSerialized), Convert.ToBase64String(replierCertificateSerialized)).Result;
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

        this.OnResponseFrame(peerPublicKey, responseFrame, newResponse: true);
    }

    public void OnResponseFrame(string peerPublicKey, ReplyFrame responseFrame, bool newResponse = false)
    {
        var decodedInvoice =  LNDWalletClient.DecodeInvoiceAsync(MakeWalletAuthToken(), responseFrame.NetworkInvoice).Result;
        if (responseFrame.ForwardOnion.IsEmpty())
        {
            if (!responseFrame.SignedSettlementPromise.NetworkPaymentHash.AsHex().SequenceEqual(decodedInvoice.PaymentHash))
            {
                Trace.TraceError("reply payload has different network_payment_hash than network_invoice");
                return;
            }

            ReplyPayload replyPayload = responseFrame.DecryptAndVerify(privateKey, SettlerSelector.GetPubKey(responseFrame.SignedSettlementPromise.ServiceUri), this.SettlerSelector);
            if (replyPayload == null)
            {
                Trace.TraceError("reply payload mismatch");
                return;
            }
            var payloadId = replyPayload.SignedRequestPayload.PayloadId;
            if (!replyPayloads.ContainsKey(payloadId))
            {
                replyPayloads[payloadId] = new();
            }
            var replierId = replyPayload.ReplierCertificate.PublicKey;
            if (!replyPayloads[payloadId].ContainsKey(replierId))
            {
                replyPayloads[payloadId][replierId] = new();
            }

            replyPayloads[payloadId][replierId].Add(new Tuple<ReplyPayload, string>(replyPayload, responseFrame.NetworkInvoice));
            gigGossipNodeEvents.OnNewResponse(this, replyPayload, responseFrame.NetworkInvoice);
            settlerMonitor.MonitorSymmetricKey(responseFrame.SignedSettlementPromise.ServiceUri, payloadId,
                (key) =>
                {
                    gigGossipNodeEvents.OnResponseReady(this, replyPayload, key);
                });
        }
        else
        {
            var topLayerPulicKey = responseFrame.ForwardOnion.Peel(privateKey);
            if (this.GetContacts().Contains(topLayerPulicKey))
            {
                if (!responseFrame.SignedSettlementPromise.Verify(responseFrame.EncryptedReplyPayload, this.SettlerSelector))
                {
                    return;
                }
                if (!responseFrame.SignedSettlementPromise.NetworkPaymentHash.AsHex().SequenceEqual(decodedInvoice.PaymentHash))
                {
                    return;
                }
                if (!newResponse)
                {
                    var settlerClient = this.SettlerSelector.GetSettlerClient(responseFrame.SignedSettlementPromise.ServiceUri);
                    var relatedNetworkPaymentHash =  settlerClient.GenerateRelatedPreimageAsync( MakeSettlerAuthTokenAsync(responseFrame.SignedSettlementPromise.ServiceUri).Result, decodedInvoice.PaymentHash).Result;
                    var networkInvoice =  LNDWalletClient.AddHodlInvoiceAsync(
                        this.MakeWalletAuthToken(),
                        decodedInvoice.NumSatoshis + this.priceAmountForRouting,
                        relatedNetworkPaymentHash, "", (long)invoicePaymentTimeout.TotalSeconds).Result;
                    this.lndWalletMonitor.MonitorInvoice(
                        relatedNetworkPaymentHash, "Accepted",
                        () =>
                        {
                            LNDWalletClient.SendPaymentAsync(
                                MakeWalletAuthToken(),
                                responseFrame.NetworkInvoice,
                                (int)invoicePaymentTimeout.TotalSeconds
                                ).Wait();
                        });
                    this.settlerMonitor.MonitorPreimage(
                        responseFrame.SignedSettlementPromise.ServiceUri,
                        relatedNetworkPaymentHash,
                        (preimage) =>
                        {
                            LNDWalletClient.SettleInvoiceAsync(
                                MakeWalletAuthToken(),
                                preimage
                                ).Wait();
                        });
                    responseFrame = responseFrame.DeepCopy();
                    responseFrame.NetworkInvoice = networkInvoice.PaymentRequest;
                }
                SendMessage(topLayerPulicKey, responseFrame);
            }
        }
    }

    public List<List<Tuple<ReplyPayload, string>>> GetResponses(Guid payloadId)
    {
        if (!replyPayloads.ContainsKey(payloadId))
        {
            Trace.TraceError("topic has no responses");
            return new();
        }
        return replyPayloads[payloadId].Values.ToList();
    }

    public void AcceptResponse(ReplyPayload replyPayload, string networkInvoice)
    {
        var payloadId = replyPayload.SignedRequestPayload.PayloadId;
        if (!replyPayloads.ContainsKey(payloadId))
        {
            Trace.TraceError("topic has no responses");
            return;
        }

        if (!replyPayloads[payloadId].ContainsKey(replyPayload.ReplierCertificate.PublicKey))
        {
            Trace.TraceError("replier has not responded for this topic");
            return;
        }

        Trace.TraceInformation("accepting the network payment");

        LNDWalletClient.SendPaymentAsync(MakeWalletAuthToken(), networkInvoice, (int)this.invoicePaymentTimeout.TotalSeconds).Wait();
        LNDWalletClient.SendPaymentAsync(MakeWalletAuthToken(), replyPayload.ReplyInvoice, (int)this.invoicePaymentTimeout.TotalSeconds).Wait();
    }

    public override void OnMessage(string senderPublicKey, object frame)
    {
        if (frame is AskForBroadcastFrame)
        {
            OnAskForBroadcastFrame(senderPublicKey, (AskForBroadcastFrame)frame);
        }
        else if (frame is POWBroadcastConditionsFrame)
        {
            OnPOWBroadcastConditionsFrame(senderPublicKey, (POWBroadcastConditionsFrame)frame);
        }
        else if (frame is POWBroadcastFrame)
        {
            OnPOWBroadcastFrame(senderPublicKey, (POWBroadcastFrame)frame);
        }
        else if (frame is ReplyFrame)
        {
            OnResponseFrame(senderPublicKey, (ReplyFrame)frame);
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
