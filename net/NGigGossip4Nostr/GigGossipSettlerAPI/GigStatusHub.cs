using System;
using System.Runtime.CompilerServices;
using GigGossipSettler;
using Microsoft.AspNetCore.SignalR;
using NetworkToolkit;

#pragma warning disable 1591

namespace GigGossipSettlerAPI;

public class GigStatusHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var authToken = Context?.GetHttpContext()?.Request.Query["authtoken"].First();
        var publicKey = Singlethon.Settler.ValidateAuthToken(authToken);
        Context.Items["publicKey"] = publicKey;
        Singlethon.GigStatusAsyncComQueue4ConnectionId.TryAdd(Context.ConnectionId, new AsyncComQueue<GigStatusEventArgs>());
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Singlethon.GigStatusAsyncComQueue4ConnectionId.TryRemove(Context.ConnectionId, out _);
        await base.OnDisconnectedAsync(exception);
    }

    public async IAsyncEnumerable<GigStatusKey> StreamAsync(string authToken, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string publicKey;
        lock (Singlethon.Settler)
            publicKey = Singlethon.Settler.ValidateAuthToken(authToken);

        AsyncComQueue<GigStatusEventArgs> asyncCom;
        if (Singlethon.GigStatusAsyncComQueue4ConnectionId.TryGetValue(Context.ConnectionId, out asyncCom))
        {
            await foreach (var ic in asyncCom.DequeueAsync(cancellationToken))
            {
                yield return ic.GigStatusChanged;
            }
        }
    }
}
