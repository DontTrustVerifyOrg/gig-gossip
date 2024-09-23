using System;
using Microsoft.AspNetCore.SignalR;
using LNDWallet;
using Nito.AsyncEx;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using NetworkToolkit;

namespace GigLNDWalletAPI;

public class PaymentStatusUpdatesHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var authToken = Context?.GetHttpContext()?.Request.Query["authtoken"].First();
        var account = Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken, false);
        Context.Items["publicKey"] = account.PublicKey;
        Singlethon.PaymentAsyncComQueue4ConnectionId.TryAdd(Context.ConnectionId, new AsyncComQueue<PaymentStatusChangedEventArgs>());
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Singlethon.PaymentAsyncComQueue4ConnectionId.TryRemove(Context.ConnectionId, out _);
        await base.OnDisconnectedAsync(exception);
    }

    public async IAsyncEnumerable<PaymentStatusChanged> StreamAsync(string authToken, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LNDAccountManager account;
        lock (Singlethon.LNDWalletManager)
            account = Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken);

        AsyncComQueue<PaymentStatusChangedEventArgs> asyncCom;
        if (Singlethon.PaymentAsyncComQueue4ConnectionId.TryGetValue(Context.ConnectionId, out asyncCom))
        {
            await foreach (var ic in asyncCom.DequeueAsync(cancellationToken))
            {
                if (ic.PublicKey == account.PublicKey)
                {
                    Trace.TraceInformation(ic.PublicKey + "|" +ic.PaymentStatusChanged.PaymentHash + "|" + ic.PaymentStatusChanged.NewStatus.ToString() + "|" + ic.PaymentStatusChanged.FailureReason.ToString());
                    yield return ic.PaymentStatusChanged;
                }
            }
        }
    }
}
