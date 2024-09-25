
using GigGossip;
using GigLNDWalletAPIClient;
using NetworkClientToolkit;
using NGigGossip4Nostr;

namespace RideShareCLIApp;

public interface IGigGossipNodeEventSource
{
    public event EventHandler<AcceptBroadcastEventArgs> OnAcceptBroadcast;

    public event EventHandler<NewResponseEventArgs> OnNewResponse;
    public event EventHandler<ResponseReadyEventArgs> OnResponseReady;
    public event EventHandler<ResponseCancelledEventArgs> OnResponseCancelled;
    public event EventHandler<CancelBroadcastEventArgs> OnCancelBroadcast;

    public event EventHandler<NetworkInvoiceAcceptedEventArgs> OnNetworkInvoiceAccepted;
    public event EventHandler<NetworkInvoiceSettledEventArgs> OnNetworkInvoiceSettled;
    public event EventHandler<NetworkInvoiceCancelledEventArgs> OnNetworkInvoiceCancelled;

    public event EventHandler<JobInvoiceAcceptedEventArgs> OnJobInvoiceAccepted;
    public event EventHandler<JobInvoiceSettledEventArgs> OnJobInvoiceSettled;
    public event EventHandler<JobInvoiceCancelledEventArgs> OnJobInvoiceCancelled;

    public event EventHandler<PaymentStatusChangeEventArgs> OnPaymentStatusChange;
    public event EventHandler<NewContactEventArgs> OnNewContact;

    public event EventHandler<ServerConnectionSourceStateEventArgs> OnServerConnectionState;

    public event EventHandler<LNDInvoiceStateChangedEventArgs> OnLNDInvoiceStateChanged;
    public event EventHandler<LNDPaymentStatusChangedEventArgs> OnLNDPaymentStatusChanged;
    public event EventHandler<LNDNewTransactionEventArgs> OnLNDNewTransaction;
    public event EventHandler<LNDPayoutStateChangedEventArgs> OnLNDPayoutStateChanged;
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
    public required string NetworkPaymentRequest;
    public required PaymentRequestRecord DecodedNetworkInvoice;
    public required JobReply ReplyPayloadCert;
}
public class ResponseReadyEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required Guid RequestPayloadId;
    public required Guid ReplierCertificateId;
    public required Reply Reply;
}
public class ResponseCancelledEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required Guid RequestPayloadId;
    public required Guid ReplierCertificateId;
}

public class NetworkInvoiceSettledEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required InvoiceData InvoiceData;
}


public class JobInvoiceSettledEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required InvoiceData InvoiceData;
}

public class PaymentStatusChangeEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required PaymentStatus Status;
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

public class JobInvoiceAcceptedEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required InvoiceData InvoiceData;
}

public class JobInvoiceCancelledEventArgs : EventArgs
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

public class LNDInvoiceStateChangedEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required InvoiceStateChange InvoiceStateChange;
}

public class LNDNewTransactionEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required NewTransactionFound NewTransactionFound;
}

public class LNDPaymentStatusChangedEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required PaymentStatusChanged PaymentStatusChanged;
}

public class LNDPayoutStateChangedEventArgs : EventArgs
{
    public required GigGossipNode GigGossipNode;
    public required PayoutStateChanged PayoutStateChanged;
}

