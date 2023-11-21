using CryptoToolkit;
using GigLNDWalletAPIClient;
using NGigGossip4Nostr;

namespace RideShareCLIApp;

public class GigGossipNodeEvents : IGigGossipNodeEvents
{
    private readonly GigGossipNodeEventSource _gigGossipNodeEventSource;

    public GigGossipNodeEvents(GigGossipNodeEventSource gigGossipNodeEventSource)
    {
        _gigGossipNodeEventSource = gigGossipNodeEventSource;
    }

    public void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, BroadcastFrame broadcastFrame)
    {
        _gigGossipNodeEventSource.FireOnAcceptBroadcast(new AcceptBroadcastEventArgs()
        {
            GigGossipNode = me,
            PeerPublicKey = peerPublicKey,
            BroadcastFrame = broadcastFrame,
        });
    }

    public async void OnNetworkInvoiceAccepted(GigGossipNode me, InvoiceData iac)
    {
        await me.PayNetworkInvoiceAsync(iac);
    }

    public void OnInvoiceSettled(GigGossipNode me, Uri serviceUri, string paymentHash, string preimage)
    {
        _gigGossipNodeEventSource.FireOnInvoiceSettled(new InvoiceSettledEventArgs()
        {
            GigGossipNode = me,
            PaymentHash = paymentHash,
            Preimage = preimage,
            ServiceUri = serviceUri
        });
    }

    public void OnNewResponse(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayloadCert, string replyInvoice, PayReq decodedReplyInvoice, string networkInvoice, PayReq decodedNetworkInvoice)
    {
        _gigGossipNodeEventSource.FireOnNewResponse(new NewResponseEventArgs()
        {
            GigGossipNode = me,
            ReplyPayloadCert = replyPayloadCert,
            ReplyInvoice = replyInvoice,
            DecodedReplyInvoice = decodedReplyInvoice,
            NetworkInvoice = networkInvoice,
            DecodedNetworkInvoice = decodedNetworkInvoice,
        });
    }

    public void OnResponseReady(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload, string key)
    {
        var taxiReply = Crypto.DeserializeObject<ConnectionReply>(Crypto.SymmetricDecrypt<byte[]>(
            key.AsBytes(),
            replyPayload.Value.EncryptedReplyMessage));
        _gigGossipNodeEventSource.FireOnResponseReady(new ResponseReadyEventArgs()
        {
            GigGossipNode = me,
            Reply = taxiReply
        });
    }

    public void OnPaymentStatusChange(GigGossipNode me, string status, PaymentData paydata)
    {
        _gigGossipNodeEventSource.FireOnPaymentStatusChange(new PaymentStatusChangeEventArgs()
        {
            GigGossipNode = me,
            PaymentData = paydata,
            Status = status
        });
    }

    public void OnCancelBroadcast(GigGossipNode me, string peerPublicKey, CancelBroadcastFrame broadcastFrame)
    {
        throw new NotImplementedException();
    }

    public void OnNetworkInvoiceCancelled(GigGossipNode me, InvoiceData iac)
    {
        throw new NotImplementedException();
    }

    public void OnInvoiceAccepted(GigGossipNode me, InvoiceData iac)
    {
        throw new NotImplementedException();
    }

    public void OnInvoiceCancelled(GigGossipNode me, InvoiceData iac)
    {
        throw new NotImplementedException();
    }
}

