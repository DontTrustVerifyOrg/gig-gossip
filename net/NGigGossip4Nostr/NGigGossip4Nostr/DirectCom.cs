using System.Reflection;
using GigDebugLoggerAPIClient;
using NetworkClientToolkit;
using Nostr.Client.Messages.Contacts;

namespace NGigGossip4Nostr;

public class DirectMessageEventArgs : EventArgs
{
    public required string EventId;
    public required string SenderPublicKey;
    public required object Frame { get; set; }
}

public class DirectCom : NostrNode
{
    LogWrapper<DirectCom> TRACE = FlowLoggerFactory.Trace<DirectCom>();

    private GigGossipNode gigGossipNode;
    public DirectCom(GigGossipNode gigGossipNode) : base(gigGossipNode, gigGossipNode.ChunkSize,false)
    {
        OnServerConnectionState += DirectCom_OnServerConnectionState;
        this.gigGossipNode = gigGossipNode;
    }

    public new async Task StartAsync(string[] nostrRelays)
    {
        using var TL = TRACE.Log().Args(nostrRelays);
        try
        {
            await base.StartAsync(nostrRelays);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    private void DirectCom_OnServerConnectionState(object? sender, ServerConnectionStateEventArgs e)
    {
        using var TL = TRACE.Log().Args(sender,e);
        try
        {
            gigGossipNode.FireOnServerConnectionState(ServerConnectionSource.NostrRelay, e.State, e.Uri);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public event EventHandler<DirectMessageEventArgs> OnDirectMessage;

    public async override Task OnMessageAsync(string eventId, string senderPublicKey, object frame)
    {
        using var TL = TRACE.Log().Args(eventId, senderPublicKey, frame);
        try
        {
            OnDirectMessage.Invoke(this, new DirectMessageEventArgs()
            {
                EventId = eventId,
                SenderPublicKey = senderPublicKey,
                Frame = frame,
            });
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public override bool OpenMessage(string id)
    {
        return gigGossipNode.OpenMessage(id);
    }

    public override bool CommitMessage(string id, int kind, DateTime createdAt)
    {
        return gigGossipNode.CommitMessage(id,kind,createdAt);
    }

    public override bool AbortMessage(string id)
    {
        return gigGossipNode.AbortMessage(id);
    }

    public override DateTime? GetLastMessageCreatedAt(int kind, int secondsBefore)
    {
        return gigGossipNode.GetLastMessageCreatedAt(kind, secondsBefore);
    }

}

