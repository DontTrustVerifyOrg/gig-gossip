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

public class ResponseEventArgs : EventArgs
{
    public ReplyPayload payload { get; set; }
    public string network_invoice { get; set; }
}

public class AcceptBroadcastResponse
{
    public Uri SettlerServiceUri { get; set; }
    public Certificate MyCertificate { get; set; }
    public byte[] Message { get; set; }
    public long Fee { get; set; }
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
    protected Dictionary<string, string> nextNetworkInvoiceToPay;
    protected Dictionary<string, ReplyPayload> replyPayloadsByHodlInvoicePaymentHash;
    protected GigLNDWalletAPIClient.swaggerClient lndWalletClient;
    protected Guid _walletToken;
    protected Dictionary<Uri,Guid> _settlerToken;

    protected ISettlerSelector settlerClientSelector;


    public GigGossipNode( ECPrivKey privKey, string[] nostrRelays) : base(privKey, nostrRelays)
    {
    }

    public async void Init(long priceAmountForRouting, TimeSpan broadcastConditionsTimeout, string broadcastConditionsPowScheme,
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
        this.nextNetworkInvoiceToPay = new();
        this.replyPayloadsByHodlInvoicePaymentHash = new();
        this.lndWalletClient = lndWalletClient;
        this._walletToken = await lndWalletClient.GetTokenAsync(this.PublicKey);
        this.settlerClientSelector = settlerClientSelector;
        this._settlerToken = new();
    }

    protected string walletToken()
    {
        return Crypto.MakeSignedTimedToken(this._privateKey, DateTime.Now, this._walletToken);
    }
    protected async Task<string> settlerToken(Uri serviceUri)
    {
        Guid? token = null;
        lock (this._settlerToken)
        {
            if (this._settlerToken.ContainsKey(serviceUri))
                token = this._settlerToken[serviceUri];
        }
        if (token == null)
        {
            token = await settlerClientSelector.GetSettlerClient(serviceUri).GetTokenAsync(this.PublicKey);
            lock (this._settlerToken)
            {
                this._settlerToken[serviceUri] = token.Value;
            }
        }
        return Crypto.MakeSignedTimedToken(this._privateKey, DateTime.Now, token.Value);
    }


    public virtual bool AcceptTopic(AbstractTopic topic)
    {
        return false;
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
        if (!this.AcceptTopic(requestPayload.Topic))
        {
            return;
        }

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
                    new OnionLayer(this.PublicKey),
                    Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(peerPublicKey))),
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

    public virtual AcceptBroadcastResponse? AcceptBroadcast(RequestPayload signedRequestPayload)
    {
        return null;
    }

    public async void OnPOWBroadcastFrame(string peerPublicKey, POWBroadcastFrame powBroadcastFrame)
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

        if (!powBroadcastFrame.Verify(settlerClientSelector))
            return;

        var acceptBroadcastResponse = this.AcceptBroadcast(powBroadcastFrame.BroadcastPayload.SignedRequestPayload);

        if (acceptBroadcastResponse != null)
        {
            var settlerClient = this.settlerClientSelector.GetSettlerClient(acceptBroadcastResponse.SettlerServiceUri);
            var replyPaymentHash = await settlerClient.GenerateReplyPaymentPreimageAsync(this.PublicKey, await settlerToken(acceptBroadcastResponse.SettlerServiceUri), powBroadcastFrame.AskId.ToString());
            var replyInvoice = (await lndWalletClient.AddHodlInvoiceAsync(this.PublicKey, walletToken() , acceptBroadcastResponse.Fee, replyPaymentHash, "", (long)invoicePaymentTimeout.TotalSeconds)).PaymentRequest;
            var signedRequestPayloadSerialized = Crypto.SerializeObject(powBroadcastFrame.BroadcastPayload.SignedRequestPayload);
            var replierCertificateSerialized = Crypto.SerializeObject(acceptBroadcastResponse.MyCertificate);
            var settr = await settlerClient.GenerateSettlementTrustAsync(this.PublicKey, await settlerToken(acceptBroadcastResponse.SettlerServiceUri), acceptBroadcastResponse.Message, replyInvoice, signedRequestPayloadSerialized, replierCertificateSerialized);
            var settlementTrust = Crypto.DeserializeObject< SettlementTrust>(settr);
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
        else
        {
            this.Broadcast(
                requestPayload: powBroadcastFrame.BroadcastPayload.SignedRequestPayload,
                originatorPublicKey: peerPublicKey,
                backwardOnion: powBroadcastFrame.BroadcastPayload.BackwardOnion);
        }
    }

    public event EventHandler<ResponseEventArgs> OnNewResponse;

    public async void OnResponseFrame(string peerPublicKey, ReplyFrame responseFrame, bool newResponse = false)
    {
        var decodedInvoice = await lndWalletClient.DecodeInvoiceAsync(this.PublicKey, walletToken(), responseFrame.NetworkInvoice);
        if (responseFrame.ForwardOnion.IsEmpty())
        {
            if (!responseFrame.SignedSettlementPromise.NetworkPaymentHash.AsHex().SequenceEqual(decodedInvoice.PaymentHash))
            {
                Trace.TraceError("reply payload has different network_payment_hash than network_invoice");
                return;
            }

            ReplyPayload replyPayload = responseFrame.DecryptAndVerify(_privateKey, settlerClientSelector.GetPubKey(responseFrame.SignedSettlementPromise.ServiceUri), this.settlerClientSelector);
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
            replyPayloadsByHodlInvoicePaymentHash[decodedInvoice.PaymentHash] = replyPayload;
            OnNewResponse.Invoke(this, new ResponseEventArgs() { network_invoice = responseFrame.NetworkInvoice, payload = replyPayload });
        }
        else
        {
            var topLayer = responseFrame.ForwardOnion.Peel(_privateKey);
            if (this.GetContacts().Contains(topLayer.PublicKey))
            {
                if (!responseFrame.SignedSettlementPromise.Verify(responseFrame.EncryptedReplyPayload, this.settlerClientSelector))
                {
                    return;
                }
                if (!responseFrame.SignedSettlementPromise.NetworkPaymentHash.AsHex().SequenceEqual(decodedInvoice.PaymentHash))
                {
                    return;
                }
                if (!newResponse)
                {
                    var settlerClient = this.settlerClientSelector.GetSettlerClient(responseFrame.SignedSettlementPromise.ServiceUri);
                    var nextNetworkInvoice = responseFrame.NetworkInvoice;
                    var rr = settlerClient.GenerateRelatedPreimageAsync(this.PublicKey, await settlerToken(responseFrame.SignedSettlementPromise.ServiceUri), decodedInvoice.PaymentHash);
                    var networkInvoice = await lndWalletClient.AddHodlInvoiceAsync( 
                        this.PublicKey, this.walletToken(),
                        decodedInvoice.NumSatoshis + this.priceAmountForRouting,
                        decodedInvoice.PaymentHash, "", (long)invoicePaymentTimeout.TotalSeconds);
                    //settler.RegisterForSettlementInPaymentChain(responseFrame.NetworkInvoice.Id,networkInvoice.Id);
                    this.nextNetworkInvoiceToPay[networkInvoice.PaymentHash] = nextNetworkInvoice;
                    responseFrame = responseFrame.DeepCopy();
                    responseFrame.NetworkInvoice = networkInvoice.PaymentRequest;
                }
                SendMessage(topLayer.PublicKey, responseFrame);
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

    public async void AcceptResponse(ReplyPayload replyPayload, string networkInvoice)
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

        await lndWalletClient.SendPaymentAsync(this.PublicKey, walletToken(), networkInvoice, 10000);
    }

    public override async void OnMessage(string senderPublicKey, object frame)
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

    public bool AcceptingHodlInvoice(string invoice)
    {
        return true;
    }

    public event EventHandler<ResponseEventArgs> OnResponseReady;

}
