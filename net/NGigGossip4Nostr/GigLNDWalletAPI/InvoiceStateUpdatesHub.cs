using System;
using Microsoft.AspNetCore.SignalR;
using LNDWallet;
using Nito.AsyncEx;

namespace GigLNDWalletAPI;

public class InvoiceStateUpdatesHub : Hub
{
    AsyncMonitor asyncInvoiceMonitor;
    Queue<InvoiceStateChangedEventArgs> invoiceChangeQueue = new();
    public InvoiceStateUpdatesHub()
    {
        Singlethon.LNDWalletManager.OnInvoiceStateChanged += LNDWalletManager_OnInvoiceStateChanged;
        asyncInvoiceMonitor = new AsyncMonitor();
    }


    private void LNDWalletManager_OnInvoiceStateChanged(object sender, InvoiceStateChangedEventArgs e)
    {
        using (asyncInvoiceMonitor.Enter())
        {
            invoiceChangeQueue.Enqueue(e);
            asyncInvoiceMonitor.PulseAll();
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
        using (await asyncInvoiceMonitor.EnterAsync(cancellationToken))
        {
            while (true)
            {
                await asyncInvoiceMonitor.WaitAsync(cancellationToken);
                while (invoiceChangeQueue.Count > 0)
                {
                    var ic = invoiceChangeQueue.Dequeue();
                    yield return ic.PaymentHash + "|" + ic.NewState.ToString();
                }
            }
        }
    }
}

