using System;
using Microsoft.AspNetCore.SignalR;
using LNDWallet;
using Nito.AsyncEx;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using NetworkToolkit;

namespace GigLNDWalletAPI;

public class TransactionUpdatesHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var authToken = Context?.GetHttpContext()?.Request.Query["authtoken"].First();
        var account = Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken, false);
        Context.Items["publicKey"] = account.PublicKey;
        Singlethon.TransactionAsyncComQueue4ConnectionId.TryAdd(Context.ConnectionId, new AsyncComQueue<NewTransactionFoundEventArgs>());
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Singlethon.TransactionAsyncComQueue4ConnectionId.TryRemove(Context.ConnectionId, out _);
        await base.OnDisconnectedAsync(exception);
    }

    public async IAsyncEnumerable<NewTransactionFound> StreamAsync(string authToken, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LNDAccountManager account;
        lock (Singlethon.LNDWalletManager)
            account = Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken);

        AsyncComQueue<NewTransactionFoundEventArgs> asyncCom;
        if (Singlethon.TransactionAsyncComQueue4ConnectionId.TryGetValue(Context.ConnectionId, out asyncCom))
        {
            await foreach (var ic in asyncCom.DequeueAsync(cancellationToken))
            {
                if(ic.PublicKey==account.PublicKey)
                {
                    Trace.TraceInformation(ic.PublicKey + "|" + ic.NewTransactionFound.TxHash + "|" + ic.NewTransactionFound.NumConfirmations.ToString() + "|" + ic.NewTransactionFound.Address + "|" + ic.NewTransactionFound.AmountSat.ToString());
                    yield return ic.NewTransactionFound;
                }
            }
        }
    }
}