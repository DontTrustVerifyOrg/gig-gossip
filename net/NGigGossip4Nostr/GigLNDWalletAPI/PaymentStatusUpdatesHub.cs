using System;
using Microsoft.AspNetCore.SignalR;
using LNDWallet;
using Nito.AsyncEx;

namespace GigLNDWalletAPI;

public class PaymentStatusUpdatesHub : Hub
{
    AsyncMonitor asyncPaymentMonitor;
    Queue<PaymentStatusChangedEventArgs> paymentChangeQueue = new();
    public PaymentStatusUpdatesHub()
    {
        Singlethon.LNDWalletManager.OnPaymentStatusChanged += LNDWalletManager_OnPaymentStatusChanged;
        asyncPaymentMonitor = new AsyncMonitor();
    }


    private void LNDWalletManager_OnPaymentStatusChanged(object sender, PaymentStatusChangedEventArgs e)
    {
        using (asyncPaymentMonitor.Enter())
        {
            paymentChangeQueue.Enqueue(e);
            asyncPaymentMonitor.PulseAll();
        }
    }

    public override async Task OnConnectedAsync()
    {
        var authToken = Context?.GetHttpContext()?.GetRouteValue("authtoken") as string;
        await Groups.AddToGroupAsync(Context?.ConnectionId, authToken);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        var authToken = Context?.GetHttpContext()?.GetRouteValue("authtoken") as string;
        await Groups.RemoveFromGroupAsync(Context?.ConnectionId, authToken);
        await base.OnDisconnectedAsync(exception);
    }

    public async IAsyncEnumerable<string> Streaming(CancellationToken cancellationToken)
    {
        using (await asyncPaymentMonitor.EnterAsync(cancellationToken))
        {
            while (true)
            {
                await asyncPaymentMonitor.WaitAsync(cancellationToken);
                while (paymentChangeQueue.Count > 0)
                {
                    var ic = paymentChangeQueue.Dequeue();
                    yield return ic.PaymentHash + "|" + ic.NewStatus.ToString();
                }
            }
        }
    }
}

