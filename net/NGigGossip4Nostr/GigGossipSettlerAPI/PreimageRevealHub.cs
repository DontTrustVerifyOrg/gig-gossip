using System;
using System.Runtime.CompilerServices;
using GigGossipSettler;
using Microsoft.AspNetCore.SignalR;
using Nito.AsyncEx;

namespace GigGossipSettlerAPI;

public class PreimageRevealHub : Hub
{
    AsyncMonitor asyncRevealMonitor;
    Queue<PreimageRevealEventArgs> revealQueue = new();
    public PreimageRevealHub()
    {
        asyncRevealMonitor = new AsyncMonitor();
        Singlethon.Settler.OnPreimageReveal += Settler_OnPreimageReveal;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Singlethon.Settler.OnPreimageReveal -= Settler_OnPreimageReveal;
        base.Dispose(disposing);
    }

    private void Settler_OnPreimageReveal(object sender, PreimageRevealEventArgs e)
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
        Singlethon.Preimages4UserPublicKey.RemoveConnection((string)Context.Items["publicKey"]);
        await base.OnDisconnectedAsync(exception);
    }

    public void Monitor(string authToken,string paymentHash)
    {
        var publicKey = Singlethon.Settler.ValidateAuthToken(authToken);
        Singlethon.Preimages4UserPublicKey.AddItem(publicKey, paymentHash);
    }

    public async IAsyncEnumerable<string> StreamAsync(string authToken, [EnumeratorCancellation] CancellationToken cancellationToken)
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
                    if (Singlethon.Preimages4UserPublicKey.ContainsItem(publicKey, ic.PaymentHash))
                        yield return ic.PaymentHash + "|" + ic.Preimage;
                }
            }
        }
    }
}
