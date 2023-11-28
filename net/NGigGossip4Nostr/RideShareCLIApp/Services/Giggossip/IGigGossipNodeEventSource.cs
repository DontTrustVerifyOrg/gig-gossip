using CryptoToolkit;
using GigLNDWalletAPIClient;
using NGigGossip4Nostr;

namespace RideShareCLIApp;

public interface IGigGossipNodeEventSource
{
    public event EventHandler<AcceptBroadcastEventArgs> OnAcceptBroadcast;

    public event EventHandler<NewResponseEventArgs> OnNewResponse;
    public event EventHandler<ResponseReadyEventArgs> OnResponseReady;
    public event EventHandler<CancelBroadcastEventArgs> OnCancelBroadcast;

    public event EventHandler<NetworkInvoiceAcceptedEventArgs> OnNetworkInvoiceAccepted;
    public event EventHandler<NetworkInvoiceCancelledEventArgs> OnNetworkInvoiceCancelled;

    public event EventHandler<InvoiceAcceptedEventArgs> OnInvoiceAccepted;
    public event EventHandler<InvoiceSettledEventArgs> OnInvoiceSettled;
    public event EventHandler<InvoiceCancelledEventArgs> OnInvoiceCancelled;

    public event EventHandler<PaymentStatusChangeEventArgs> OnPaymentStatusChange;
    public event EventHandler<NewContactEventArgs> OnNewContact;
}

public class AcceptBroadcastEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required string PeerPublicKey;
    public required BroadcastFrame BroadcastFrame;
}

public class NetworkInvoiceAcceptedEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required InvoiceData InvoiceData;
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
    public required Guid RequestPayloadId;
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

public class CancelBroadcastEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required string PeerPublicKey;
    public required CancelBroadcastFrame CancelBroadcastFrame;
}

public class NetworkInvoiceCancelledEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required InvoiceData InvoiceData;
}

public class InvoiceAcceptedEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required InvoiceData InvoiceData;
}

public class InvoiceCancelledEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required InvoiceData InvoiceData;
}

public class NewContactEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required string PublicKey;
}
