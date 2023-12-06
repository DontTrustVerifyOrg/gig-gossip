using System;
using System.Runtime.CompilerServices;
using GigGossipSettler;
using Microsoft.AspNetCore.SignalR;
using Nito.AsyncEx;

namespace GigGossipSettlerAPI;

public class SymmetricKeyRevealHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var authToken = Context?.GetHttpContext()?.Request.Query["authtoken"].First();
        var publicKey = Singlethon.Settler.ValidateAuthToken(authToken);
        Context.Items["publicKey"] = publicKey;
        Singlethon.SymmetricKeyAsyncComQueue4ConnectionId.TryAdd(Context.ConnectionId, new AsyncComQueue<SymmetricKeyRevealEventArgs>());
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Singlethon.SymmetricKeyAsyncComQueue4ConnectionId.TryRemove(Context.ConnectionId, out _);
        await base.OnDisconnectedAsync(exception);
    }

    public void Monitor(string authToken, Guid signedRequestPayload, Guid replierCertificateId)
    {
        string publicKey;
        lock (Singlethon.Settler)
            publicKey = Singlethon.Settler.ValidateAuthToken(authToken);

        Singlethon.SymmetricKeys4UserPublicKey.AddItem(publicKey, new GigReplCert { SignerRequestPayloadId = signedRequestPayload, ReplierCertificateId = replierCertificateId });
    }

    public async IAsyncEnumerable<string> StreamAsync(string authToken, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string publicKey;
        lock (Singlethon.Settler)
            publicKey = Singlethon.Settler.ValidateAuthToken(authToken);

        AsyncComQueue<SymmetricKeyRevealEventArgs> asyncCom;
        if (Singlethon.SymmetricKeyAsyncComQueue4ConnectionId.TryGetValue(Context.ConnectionId, out asyncCom))
        {
            await foreach (var ic in asyncCom.DequeueAsync(cancellationToken))
            {
                if (Singlethon.SymmetricKeys4UserPublicKey.ContainsItem(publicKey, new GigReplCert { SignerRequestPayloadId = ic.SignedRequestPayloadId, ReplierCertificateId = ic.ReplierCertificateId }))
                    yield return ic.SignedRequestPayloadId.ToString() + "|" + ic.ReplierCertificateId.ToString() + "|" + ic.SymmetricKey;
            }
        }
    }
}
