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
        var authToken = Context?.GetHttpContext()?.Request.Query["authtoken"].First();
        Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken);
        await base.OnConnectedAsync();
    }

    public async Task Monitor(string authToken, string paymentHash)
    {
        var publicKey = Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).PublicKey;
        lock (Singlethon.InvoiceHashes4UserPublicKey)
        {
            if (!Singlethon.InvoiceHashes4UserPublicKey.ContainsKey(publicKey))
                Singlethon.InvoiceHashes4UserPublicKey[publicKey] = new();
            Singlethon.InvoiceHashes4UserPublicKey[publicKey].Add(paymentHash);
        }
    }

    public async IAsyncEnumerable<string> Streaming(string authToken, CancellationToken cancellationToken)
    {
        var publicKey = Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).PublicKey;
        while (true)
        {
            using (await asyncInvoiceMonitor.EnterAsync(cancellationToken))
            {
                await asyncInvoiceMonitor.WaitAsync(cancellationToken);
                while (invoiceChangeQueue.Count > 0)
                {
                    var ic = invoiceChangeQueue.Dequeue();
                    lock (Singlethon.InvoiceHashes4UserPublicKey)
                    {
                        if (Singlethon.InvoiceHashes4UserPublicKey.ContainsKey(publicKey))
                            if (Singlethon.InvoiceHashes4UserPublicKey[publicKey].Contains(ic.PaymentHash))
                                yield return ic.PaymentHash + "|" + ic.NewState.ToString();
                    }
                }
            }
        }
    }
}

