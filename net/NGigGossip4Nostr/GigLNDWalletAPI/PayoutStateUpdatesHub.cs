using System;
using Microsoft.AspNetCore.SignalR;
using LNDWallet;
using Nito.AsyncEx;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using NetworkToolkit;

namespace GigLNDWalletAPI;

public class PayoutStateUpdatesHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var authToken = Context?.GetHttpContext()?.Request.Query["authtoken"].First();
        var account = Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken, false);
        Context.Items["publicKey"] = account.PublicKey;
        Singlethon.PayoutAsyncComQueue4ConnectionId.TryAdd(Context.ConnectionId, new AsyncComQueue<PayoutStateChangedEventArgs>());
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Singlethon.PayoutAsyncComQueue4ConnectionId.TryRemove(Context.ConnectionId, out _);
        await base.OnDisconnectedAsync(exception);
    }

    public async IAsyncEnumerable<PayoutStateChanged> StreamAsync(string authToken, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LNDAccountManager account;
        lock (Singlethon.LNDWalletManager)
            account = Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken);

        AsyncComQueue<PayoutStateChangedEventArgs> asyncCom;
        if (Singlethon.PayoutAsyncComQueue4ConnectionId.TryGetValue(Context.ConnectionId, out asyncCom))
        {
            await foreach (var ic in asyncCom.DequeueAsync(cancellationToken))
            {
                if (ic.PublicKey == account.PublicKey)
                {
                    Trace.TraceInformation(ic.PublicKey + "|" +ic.PayoutStateChanged.PayoutId + "|" + ic.PayoutStateChanged.NewState.ToString() + "|" + ic.PayoutStateChanged.PayoutFee.ToString() + (ic.PayoutStateChanged.Tx==null?"": ("|" + ic.PayoutStateChanged.Tx)));
                    yield return ic.PayoutStateChanged;
                }
            }
        }
    }
}
