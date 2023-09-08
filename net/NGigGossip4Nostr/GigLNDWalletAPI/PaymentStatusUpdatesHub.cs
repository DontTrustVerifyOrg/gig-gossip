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
        var authToken = Context?.GetHttpContext()?.Request.Query["authtoken"].First();
        Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken);
        await base.OnConnectedAsync();
    }

    public async Task Monitor(string authToken, string paymentHash, CancellationToken cancellationToken)
    {
        var publicKey = Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).PublicKey;
        lock (Singlethon.PaymentHashes4UserPublicKey)
        {
            if (!Singlethon.PaymentHashes4UserPublicKey.ContainsKey(publicKey))
                Singlethon.PaymentHashes4UserPublicKey[publicKey] = new();
            Singlethon.PaymentHashes4UserPublicKey[publicKey].Add(paymentHash);
        }
    }

    public async IAsyncEnumerable<string> Streaming(string authToken, CancellationToken cancellationToken)
    {
        var publicKey = Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).PublicKey;
        while (true)
        {
            using (await asyncPaymentMonitor.EnterAsync(cancellationToken))
            {
                await asyncPaymentMonitor.WaitAsync(cancellationToken);
                while (paymentChangeQueue.Count > 0)
                {
                    var ic = paymentChangeQueue.Dequeue();
                    lock (Singlethon.InvoiceHashes4UserPublicKey)
                    {
                        if (Singlethon.InvoiceHashes4UserPublicKey.ContainsKey(publicKey))
                            if (Singlethon.InvoiceHashes4UserPublicKey[publicKey].Contains(ic.PaymentHash))
                                yield return ic.PaymentHash + "|" + ic.NewStatus.ToString();
                    }
                }
            }
        }
    }
}

