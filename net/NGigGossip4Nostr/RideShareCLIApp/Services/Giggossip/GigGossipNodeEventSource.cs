
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
    public event EventHandler<CancelBroadcastEventArgs> OnCancelBroadcast;

    public event EventHandler<NetworkInvoiceAcceptedEventArgs> OnNetworkInvoiceAccepted;
    public event EventHandler<NetworkInvoiceCancelledEventArgs> OnNetworkInvoiceCancelled;

    public event EventHandler<InvoiceAcceptedEventArgs> OnInvoiceAccepted;
    public event EventHandler<InvoiceSettledEventArgs> OnInvoiceSettled;
    public event EventHandler<InvoiceCancelledEventArgs> OnInvoiceCancelled;

    public event EventHandler<PaymentStatusChangeEventArgs> OnPaymentStatusChange;

    public event EventHandler<NewContactEventArgs> OnNewContact;

    public readonly GigGossipNodeEvents GigGossipNodeEvents;

    public void FireOnAcceptBroadcast(AcceptBroadcastEventArgs args) => OnAcceptBroadcast?.Invoke(this, args);

    public void FireOnNewResponse(NewResponseEventArgs args) => OnNewResponse?.Invoke(this, args);
    public void FireOnResponseReady(ResponseReadyEventArgs args) => OnResponseReady?.Invoke(this, args);
    public void FireOnCancelBroadcast(CancelBroadcastEventArgs args) => OnCancelBroadcast?.Invoke(this, args);

    public void FireOnNetworkInvoiceAccepted(NetworkInvoiceAcceptedEventArgs args) => OnNetworkInvoiceAccepted?.Invoke(this, args);
    public void FireOnNetworkInvoiceCancelled(NetworkInvoiceCancelledEventArgs args) => OnNetworkInvoiceCancelled?.Invoke(this, args);

    public void FireOnInvoiceAccepted(InvoiceAcceptedEventArgs args) => OnInvoiceAccepted?.Invoke(this, args);
    public void FireOnInvoiceSettled(InvoiceSettledEventArgs args) => OnInvoiceSettled?.Invoke(this, args);
    public void FireOnInvoiceCancelled(InvoiceCancelledEventArgs args) => OnInvoiceCancelled?.Invoke(this, args);

    public void FireOnPaymentStatusChange(PaymentStatusChangeEventArgs args) => OnPaymentStatusChange?.Invoke(this, args);

    public void FireOnNewContact(NewContactEventArgs args) => OnNewContact?.Invoke(this, args);
}
