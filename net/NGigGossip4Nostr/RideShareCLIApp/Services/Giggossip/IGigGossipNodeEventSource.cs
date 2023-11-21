using CryptoToolkit;
using GigLNDWalletAPIClient;
using NGigGossip4Nostr;

namespace RideShareCLIApp;

public interface IGigGossipNodeEventSource
{
    public event EventHandler<NewResponseEventArgs> OnNewResponse;
    public event EventHandler<ResponseReadyEventArgs> OnResponseReady;
    public event EventHandler<AcceptBroadcastEventArgs> OnAcceptBroadcast;
    public event EventHandler<InvoiceSettledEventArgs> OnInvoiceSettled;
    public event EventHandler<PaymentStatusChangeEventArgs> OnPaymentStatusChange;
}

public class AcceptBroadcastEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required string PeerPublicKey;
    public required BroadcastFrame BroadcastFrame;
}

public class NewResponseEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required string ReplyInvoice;
    public required PayReq DecodedReplyInvoice;
    public required string NetworkInvoice;
    public required PayReq DecodedNetworkInvoice;
    public required Certificate<ReplyPayloadValue> ReplyPayloadCert;
}
public class ResponseReadyEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required ConnectionReply Reply;
}

public class InvoiceSettledEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required Uri ServiceUri;
    public required string PaymentHash;
    public required string Preimage;
}

public class PaymentStatusChangeEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required string Status;
    public required PaymentData PaymentData;
}

