
namespace RideShareCLIApp;

public class GigGossipNodeEventSource : IGigGossipNodeEventSource
{
    public GigGossipNodeEventSource()
    {
        GigGossipNodeEvents = new(this);
    }

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


    public readonly GigGossipNodeEvents GigGossipNodeEvents;

    public void FireOnAcceptBroadcast(AcceptBroadcastEventArgs args) => OnAcceptBroadcast?.Invoke(this, args);

    public void FireOnNewResponse(NewResponseEventArgs args) => OnNewResponse?.Invoke(this, args);
    public void FireOnResponseReady(ResponseReadyEventArgs args) => OnResponseReady?.Invoke(this, args);
    public void FireOnResponseCancelled(ResponseCancelledEventArgs args) => OnResponseCancelled?.Invoke(this, args);
    public void FireOnCancelBroadcast(CancelBroadcastEventArgs args) => OnCancelBroadcast?.Invoke(this, args);

    public void FireOnNetworkInvoiceAccepted(NetworkInvoiceAcceptedEventArgs args) => OnNetworkInvoiceAccepted?.Invoke(this, args);
    public void FireOnNetworkInvoiceSettled(NetworkInvoiceSettledEventArgs args) => OnNetworkInvoiceSettled?.Invoke(this, args);
    public void FireOnNetworkInvoiceCancelled(NetworkInvoiceCancelledEventArgs args) => OnNetworkInvoiceCancelled?.Invoke(this, args);

    public void FireOnJobInvoiceAccepted(JobInvoiceAcceptedEventArgs args) => OnJobInvoiceAccepted?.Invoke(this, args);
    public void FireOnJobInvoiceSettled(JobInvoiceSettledEventArgs args) => OnJobInvoiceSettled?.Invoke(this, args);
    public void FireOnJobInvoiceCancelled(JobInvoiceCancelledEventArgs args) => OnJobInvoiceCancelled?.Invoke(this, args);

    public void FireOnPaymentStatusChange(PaymentStatusChangeEventArgs args) => OnPaymentStatusChange?.Invoke(this, args);

    public void FireOnNewContact(NewContactEventArgs args) => OnNewContact?.Invoke(this, args);
    public void FireOnServerConnectionState(ServerConnectionSourceStateEventArgs args) => OnServerConnectionState?.Invoke(this, args);


    public void FireOnLNDInvoiceStateChanged(LNDInvoiceStateChangedEventArgs args) => OnLNDInvoiceStateChanged?.Invoke(this, args);
    public void FireOnLNDPaymentStatusChanged(LNDPaymentStatusChangedEventArgs args) => OnLNDPaymentStatusChanged?.Invoke(this, args);
    public void FireOnLNDNewTransaction(LNDNewTransactionEventArgs args) => OnLNDNewTransaction?.Invoke(this, args);
    public void FireOnLNDPayoutStateChanged(LNDPayoutStateChangedEventArgs args) => OnLNDPayoutStateChanged?.Invoke(this, args);



}
