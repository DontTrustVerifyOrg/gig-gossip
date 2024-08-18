using System;
using NBitcoin.Protocol;
using System.Diagnostics;
using NBitcoin.Secp256k1;
using System.Collections.Concurrent;
using NetworkClientToolkit;

namespace NGigGossip4Nostr;

public class DirectMessageEventArgs : EventArgs
{
    public required string EventId;
    public required string SenderPublicKey;
    public required object Frame { get; set; }
}

public class DirectCom : NostrNode
{
    private GigGossipNode gigGossipNode;
    public DirectCom(GigGossipNode gigGossipNode) : base(gigGossipNode, gigGossipNode.ChunkSize,false)
    {
        OnServerConnectionState += DirectCom_OnServerConnectionState;
        this.gigGossipNode = gigGossipNode;
    }

    public new async Task StartAsync(string[] nostrRelays)
    {
        await base.StartAsync(nostrRelays);
    }

    private void DirectCom_OnServerConnectionState(object? sender, ServerConnectionStateEventArgs e)
    {
        gigGossipNode.FireOnServerConnectionState(ServerConnectionSource.NostrRelay, e.State, e.Uri);
    }

    public event EventHandler<DirectMessageEventArgs> OnDirectMessage;

    public async override Task OnMessageAsync(string eventId, bool isNew, string senderPublicKey, object frame)
    {
        OnDirectMessage.Invoke(this, new DirectMessageEventArgs()
        {
            EventId = eventId,
            SenderPublicKey = senderPublicKey,
            Frame = frame,
        });
    }

    public override bool OpenMessage(string id)
    {
        return gigGossipNode.OpenMessage(id);
    }

    public override bool CommitMessage(string id)
    {
        return gigGossipNode.CommitMessage(id);
    }

    public override bool AbortMessage(string id)
    {
        return gigGossipNode.AbortMessage(id);
    }

}

