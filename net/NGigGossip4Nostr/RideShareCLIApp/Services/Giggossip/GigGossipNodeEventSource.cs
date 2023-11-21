
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
    public event EventHandler<InvoiceSettledEventArgs> OnInvoiceAccepted;
    public event EventHandler<InvoiceSettledEventArgs> OnInvoiceSettled;
    public event EventHandler<PaymentStatusChangeEventArgs> OnPaymentStatusChange;

    public readonly GigGossipNodeEvents GigGossipNodeEvents;

    public void FireOnNewResponse(NewResponseEventArgs args) => OnNewResponse?.Invoke(this, args);
    public void FireOnResponseReady(ResponseReadyEventArgs args) => OnResponseReady?.Invoke(this, args);
    public void FireOnAcceptBroadcast(AcceptBroadcastEventArgs args) => OnAcceptBroadcast?.Invoke(this, args);
    public void FireOnInvoiceSettled(InvoiceSettledEventArgs args) => OnInvoiceSettled?.Invoke(this, args);
    public void FireOnPaymentStatusChange(PaymentStatusChangeEventArgs args) => OnPaymentStatusChange?.Invoke(this, args);
}

