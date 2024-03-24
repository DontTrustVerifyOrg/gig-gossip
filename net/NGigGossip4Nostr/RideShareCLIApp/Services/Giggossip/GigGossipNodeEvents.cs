using CryptoToolkit;
using GigLNDWalletAPIClient;
using NBitcoin;
using NetworkClientToolkit;
using NGigGossip4Nostr;
using RideShareFrames;

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
            BroadcastFrame = broadcastFrame
        });
    }

    public async void OnNetworkInvoiceAccepted(GigGossipNode me, InvoiceData iac)
    {
        _gigGossipNodeEventSource.FireOnNetworkInvoiceAccepted(new NetworkInvoiceAcceptedEventArgs
        {
            GigGossipNode = me,
            InvoiceData = iac
        });
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

    public void OnNewResponse(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayloadCert, string replyInvoice, PayReqRet decodedReplyInvoice, string networkInvoice, PayReqRet decodedNetworkInvoice)
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
        var connectionReply = Crypto.DeserializeObject<ConnectionReply>(Crypto.SymmetricDecrypt<byte[]>(
            key.AsBytes(),
            replyPayload.Value.EncryptedReplyMessage));
        _gigGossipNodeEventSource.FireOnResponseReady(new ResponseReadyEventArgs()
        {
            GigGossipNode = me,
            RequestPayloadId = replyPayload.Value.SignedRequestPayload.Id,
            Reply = connectionReply
        });
    }


    public void OnResponseCancelled(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload)
    {
        _gigGossipNodeEventSource.FireOnResponseCancelled(new ResponseCancelledEventArgs()
        {
            GigGossipNode = me,
            ReplierCertificateId = replyPayload.Id,
            RequestPayloadId = replyPayload.Value.SignedRequestPayload.Id,
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
        _gigGossipNodeEventSource.FireOnCancelBroadcast(new CancelBroadcastEventArgs
        {
            GigGossipNode = me,
            CancelBroadcastFrame = broadcastFrame,
            PeerPublicKey = peerPublicKey
        });
    }

    public void OnNetworkInvoiceCancelled(GigGossipNode me, InvoiceData iac)
    {
        _gigGossipNodeEventSource.FireOnNetworkInvoiceCancelled(new NetworkInvoiceCancelledEventArgs
        {
            GigGossipNode = me,
            InvoiceData = iac
        });
    }

    public void OnInvoiceAccepted(GigGossipNode me, InvoiceData iac)
    {
        _gigGossipNodeEventSource.FireOnInvoiceAccepted(new InvoiceAcceptedEventArgs
        {
            GigGossipNode = me,
            InvoiceData = iac
        });
    }

    public void OnInvoiceCancelled(GigGossipNode me, InvoiceData iac)
    {
        _gigGossipNodeEventSource.FireOnInvoiceCancelled(new InvoiceCancelledEventArgs
        {
            GigGossipNode = me,
            InvoiceData = iac
        });
    }

    public void OnNewContact(GigGossipNode me, string pubkey)
    {
        _gigGossipNodeEventSource.FireOnNewContact(new NewContactEventArgs
        {
            GigGossipNode = me,
            PublicKey = pubkey
        });
    }

    public void OnSettings(GigGossipNode me, string settings)
    {
    }

    public void OnEoseArrived(GigGossipNode me)
    {
    }

    public void OnServerConnectionState(GigGossipNode me, ServerConnectionSource source, ServerConnectionState state, Uri uri)
    {
        _gigGossipNodeEventSource.FireOnServerConnectionState(new ServerConnectionSourceStateEventArgs
        {
            GigGossipNode = me,
            Source = source,
            State = state,
            Uri = uri
        });
    }
}

