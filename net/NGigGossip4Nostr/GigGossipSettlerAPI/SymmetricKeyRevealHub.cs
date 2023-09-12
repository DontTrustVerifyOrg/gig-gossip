using System;
using GigGossipSettler;
using Microsoft.AspNetCore.SignalR;
using Nito.AsyncEx;

namespace GigGossipSettlerAPI;

public class SymmetricKeyRevealHub : Hub
{
    AsyncMonitor asyncRevealMonitor;
    Queue<SymmetricKeyRevealEventArgs> revealQueue = new();
    public SymmetricKeyRevealHub()
    {
        asyncRevealMonitor = new AsyncMonitor();
        Singlethon.Settler.OnSymmetricKeyReveal += Settler_OnSymmetricKeyReveal;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Singlethon.Settler.OnSymmetricKeyReveal -= Settler_OnSymmetricKeyReveal;
        base.Dispose(disposing);
    }

    private void Settler_OnSymmetricKeyReveal(object sender, SymmetricKeyRevealEventArgs e)
    {
        using (asyncRevealMonitor.Enter())
        {
            revealQueue.Enqueue(e);
            asyncRevealMonitor.PulseAll();
        }
    }

    public override async Task OnConnectedAsync()
    {
        var authToken = Context?.GetHttpContext()?.Request.Query["authtoken"].First();
        var publicKey = Singlethon.Settler.ValidateAuthToken(authToken);
        Context.Items["publicKey"] = publicKey;
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Singlethon.SymmetricKeys4UserPublicKey.RemoveConnection((string)Context.Items["publicKey"]);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task Monitor(string authToken, Guid gigId, string replierPublicKey)
    {
        var publicKey = Singlethon.Settler.ValidateAuthToken(authToken);
        Singlethon.SymmetricKeys4UserPublicKey.AddItem(publicKey, Tuple.Create(gigId,replierPublicKey));
    }

    public async IAsyncEnumerable<string> Streaming(string authToken, CancellationToken cancellationToken)
    {
        var publicKey = Singlethon.Settler.ValidateAuthToken(authToken);
        while (true)
        {
            using (await asyncRevealMonitor.EnterAsync(cancellationToken))
            {
                await asyncRevealMonitor.WaitAsync(cancellationToken);
                while (revealQueue.Count > 0)
                {
                    var ic = revealQueue.Dequeue();
                    if (Singlethon.SymmetricKeys4UserPublicKey.ContainsItem(publicKey, Tuple.Create(ic.GigId,ic.ReplierPublicKey) ))
                        yield return ic.GigId.ToString() +"|"+ ic.ReplierPublicKey + "|" + ic.SymmetricKey;
                }
            }
        }
    }
}
