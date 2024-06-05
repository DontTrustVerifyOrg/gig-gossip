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
        var publicKey = Singlethon.Settler.ValidateAuthToken(authToken, AccessRights.Valid);
        Context.Items["publicKey"] = publicKey;
        Singlethon.GigStatusAsyncComQueue4ConnectionId.TryAdd(Context.ConnectionId, new AsyncComQueue<GigStatusEventArgs>());
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Singlethon.GigStatusAsyncComQueue4ConnectionId.TryRemove(Context.ConnectionId, out _);
        await base.OnDisconnectedAsync(exception);
    }

    public void Monitor(string authToken, Guid signedRequestPayload, Guid replierCertificateId)
    {
        string publicKey;
        lock (Singlethon.Settler)
            publicKey = Singlethon.Settler.ValidateAuthToken(authToken, AccessRights.Valid);

        Singlethon.GigStatus4UserPublicKey.AddItem(publicKey, new GigReplCert { SignerRequestPayloadId = signedRequestPayload, ReplierCertificateId = replierCertificateId });
    }

    public async IAsyncEnumerable<string> StreamAsync(string authToken, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string publicKey;
        lock (Singlethon.Settler)
            publicKey = Singlethon.Settler.ValidateAuthToken(authToken, AccessRights.Valid);

        AsyncComQueue<GigStatusEventArgs> asyncCom;
        if (Singlethon.GigStatusAsyncComQueue4ConnectionId.TryGetValue(Context.ConnectionId, out asyncCom))
        {
            await foreach (var ic in asyncCom.DequeueAsync(cancellationToken))
            {
                if (Singlethon.GigStatus4UserPublicKey.ContainsItem(publicKey, new GigReplCert { SignerRequestPayloadId = ic.SignedRequestPayloadId, ReplierCertificateId = ic.ReplierCertificateId }))
                    yield return ic.SignedRequestPayloadId.ToString() + "|" + ic.ReplierCertificateId.ToString() + "|" + ic.Status.ToString() + "|" + ic.Value;
            }
        }
    }
}
