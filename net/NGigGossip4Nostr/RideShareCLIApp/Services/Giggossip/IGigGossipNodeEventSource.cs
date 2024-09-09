using CryptoToolkit;
using GigGossipFrames;
using GigLNDWalletAPIClient;
using NetworkClientToolkit;
using NGigGossip4Nostr;
using RideShareFrames;

namespace RideShareCLIApp;

public interface IGigGossipNodeEventSource
{
    public event EventHandler<AcceptBroadcastEventArgs> OnAcceptBroadcast;

    public event EventHandler<NewResponseEventArgs> OnNewResponse;
    public event EventHandler<ResponseReadyEventArgs> OnResponseReady;
    public event EventHandler<ResponseCancelledEventArgs> OnResponseCancelled;
    public event EventHandler<CancelBroadcastEventArgs> OnCancelBroadcast;

    public event EventHandler<NetworkInvoiceAcceptedEventArgs> OnNetworkInvoiceAccepted;
    public event EventHandler<NetworkInvoiceCancelledEventArgs> OnNetworkInvoiceCancelled;

    public event EventHandler<InvoiceAcceptedEventArgs> OnInvoiceAccepted;
    public event EventHandler<InvoiceSettledEventArgs> OnInvoiceSettled;
    public event EventHandler<InvoiceCancelledEventArgs> OnInvoiceCancelled;

    public event EventHandler<PaymentStatusChangeEventArgs> OnPaymentStatusChange;
    public event EventHandler<NewContactEventArgs> OnNewContact;

    public event EventHandler<ServerConnectionSourceStateEventArgs> OnServerConnectionState;
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
    public required PaymentRequestRecord DecodedReplyInvoice;
    public required string NetworkInvoice;
    public required PaymentRequestRecord DecodedNetworkInvoice;
    public required Certificate ReplyPayloadCert;
}
public class ResponseReadyEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required Guid RequestPayloadId;
    public required Guid ReplierCertificateId;
    public required ConnectionReply Reply;
}
public class ResponseCancelledEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required Guid RequestPayloadId;
    public required Guid ReplierCertificateId;
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

public class ServerConnectionSourceStateEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required ServerConnectionSource Source;
    public required ServerConnectionState State;
    public required Uri Uri;
}