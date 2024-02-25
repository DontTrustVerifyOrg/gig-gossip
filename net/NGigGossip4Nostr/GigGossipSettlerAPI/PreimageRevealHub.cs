using System;
using System.Runtime.CompilerServices;
using GigGossipSettler;
using Microsoft.AspNetCore.SignalR;
using Nito.AsyncEx;

#pragma warning disable 1591

namespace GigGossipSettlerAPI;

public class PreimageRevealHub : Hub
{

    public override async Task OnConnectedAsync()
    {
        var authToken = Context?.GetHttpContext()?.Request.Query["authtoken"].First();
        var publicKey = Singlethon.Settler.ValidateAuthToken(authToken);
        Context.Items["publicKey"] = publicKey;
        Singlethon.PreimagesAsyncComQueue4ConnectionId.TryAdd(Context.ConnectionId, new AsyncComQueue<PreimageRevealEventArgs>());
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Singlethon.PreimagesAsyncComQueue4ConnectionId.TryRemove(Context.ConnectionId, out _);
        await base.OnDisconnectedAsync(exception);
    }

    public void Monitor(string authToken,string paymentHash)
    {
        string publicKey;
        lock (Singlethon.Settler)
            publicKey = Singlethon.Settler.ValidateAuthToken(authToken);

        Singlethon.Preimages4UserPublicKey.AddItem(publicKey, paymentHash);
    }

    public async IAsyncEnumerable<string> StreamAsync(string authToken, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string publicKey;
        lock (Singlethon.Settler)
            publicKey = Singlethon.Settler.ValidateAuthToken(authToken);

        AsyncComQueue<PreimageRevealEventArgs> asyncCom;
        if (Singlethon.PreimagesAsyncComQueue4ConnectionId.TryGetValue(Context.ConnectionId, out asyncCom))
        {
            await foreach (var ic in asyncCom.DequeueAsync(cancellationToken))
            {
                if (Singlethon.Preimages4UserPublicKey.ContainsItem(publicKey, ic.PaymentHash))
                    yield return ic.PaymentHash + "|" + ic.Preimage;
            }
        }
    }
}
