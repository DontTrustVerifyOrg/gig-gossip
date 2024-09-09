using CryptoToolkit;
using GigGossipFrames;
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

    public void OnNewResponse(GigGossipNode me, Certificate replyPayloadCert, string replyInvoice, PaymentRequestRecord decodedReplyInvoice, string networkInvoice, PaymentRequestRecord decodedNetworkInvoice)
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

    public void OnResponseReady(GigGossipNode me, Certificate replyPayload, string key)
    {
        var replyPayloadValue = Crypto.BinaryDeserializeObject<ReplyPayloadValue>(replyPayload.Value.ToArray());
        var connectionReply = Crypto.BinaryDeserializeObject<ConnectionReply>(
                                    Crypto.SymmetricBytesDecrypt(
                                        key.AsBytes(),
                                        replyPayloadValue.EncryptedReplyMessage.ToArray()));

        _gigGossipNodeEventSource.FireOnResponseReady(new ResponseReadyEventArgs()
        {
            GigGossipNode = me,
            ReplierCertificateId = replyPayload.Id.AsGuid(),
            RequestPayloadId = replyPayloadValue.SignedRequestPayload.Id.AsGuid(),
            Reply = connectionReply
        });
    }


    public void OnResponseCancelled(GigGossipNode me, Certificate replyPayload)
    {
        var replyPayloadValue = Crypto.BinaryDeserializeObject<ReplyPayloadValue>(replyPayload.Value.ToArray());
        _gigGossipNodeEventSource.FireOnResponseCancelled(new ResponseCancelledEventArgs()
        {
            GigGossipNode = me,
            ReplierCertificateId = replyPayload.Id.AsGuid(),
            RequestPayloadId = replyPayloadValue.SignedRequestPayload.Id.AsGuid(),
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

