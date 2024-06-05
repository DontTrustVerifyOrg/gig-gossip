using System;
using Microsoft.AspNetCore.SignalR;
using LNDWallet;
using Nito.AsyncEx;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using NetworkToolkit;

namespace GigLNDWalletAPI;

public class InvoiceStateUpdatesHub : Hub
{
    public static AccessRights AccessRights;
    public override async Task OnConnectedAsync()
    {
        var authToken = Context?.GetHttpContext()?.Request.Query["authtoken"].First();
        var account = Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken,AccessRights);
        Context.Items["publicKey"] = account.PublicKey;
        Singlethon.InvoiceAsyncComQueue4ConnectionId.TryAdd(Context.ConnectionId, new AsyncComQueue<InvoiceStateChangedEventArgs>());
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Singlethon.InvoiceAsyncComQueue4ConnectionId.TryRemove(Context.ConnectionId, out _);
        await base.OnDisconnectedAsync(exception);
    }

    public void Monitor(string authToken, string paymentHash)
    {
        LNDAccountManager account;
        lock (Singlethon.LNDWalletManager)
            account = Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken);
        Singlethon.InvoiceHashes4PublicKey.AddItem(account.PublicKey, paymentHash);
    }

    public void StopMonitoring(string authToken, string paymentHash)
    {
        LNDAccountManager account;
        lock (Singlethon.LNDWalletManager)
            account = Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken);
        Singlethon.InvoiceHashes4PublicKey.RemoveItem(account.PublicKey, paymentHash);
    }

    public async IAsyncEnumerable<string> StreamAsync(string authToken, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LNDAccountManager account;
        lock (Singlethon.LNDWalletManager)
            account = Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken);

        AsyncComQueue<InvoiceStateChangedEventArgs> asyncCom;
        if (Singlethon.InvoiceAsyncComQueue4ConnectionId.TryGetValue(Context.ConnectionId, out asyncCom))
        {
            await foreach (var ic in asyncCom.DequeueAsync(cancellationToken))
            {
                if (Singlethon.InvoiceHashes4PublicKey.ContainsItem(account.PublicKey, ic.PaymentHash))
                {
                    Trace.TraceInformation(ic.PaymentHash + "|" + ic.NewState.ToString());
                    yield return ic.PaymentHash + "|" + ic.NewState.ToString();
                }
            }
        }
    }
}
