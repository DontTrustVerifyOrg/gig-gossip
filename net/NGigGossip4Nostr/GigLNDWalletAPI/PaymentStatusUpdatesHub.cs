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
        asyncPaymentMonitor = new AsyncMonitor();
        Singlethon.LNDWalletManager.OnPaymentStatusChanged += LNDWalletManager_OnPaymentStatusChanged;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Singlethon.LNDWalletManager.OnPaymentStatusChanged -= LNDWalletManager_OnPaymentStatusChanged;
        base.Dispose(disposing);
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
        var authToken = Context?.GetHttpContext()?.Request.Query["authtoken"].First();
        var account = Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken);
        Context.Items["publicKey"] = account.PublicKey;
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Singlethon.PaymentHashes4ConnectionId.RemoveConnection((string)Context.Items["publicKey"]);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task Monitor(string authToken, string paymentHash)
    {
        var account = Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken);
        Singlethon.PaymentHashes4ConnectionId.AddItem(account.PublicKey, paymentHash);
    }

    public async IAsyncEnumerable<string> Streaming(string authToken, CancellationToken cancellationToken)
    {
        var account = Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken);
        while (true)
        {
            using (await asyncPaymentMonitor.EnterAsync(cancellationToken))
            {
                await asyncPaymentMonitor.WaitAsync(cancellationToken);
                while (paymentChangeQueue.Count > 0)
                {
                    var ic = paymentChangeQueue.Dequeue();
                    if (Singlethon.PaymentHashes4ConnectionId.ContainsItem(account.PublicKey, ic.PaymentHash))
                        yield return ic.PaymentHash + "|" + ic.NewStatus.ToString();
                }
            }
        }
    }
}
