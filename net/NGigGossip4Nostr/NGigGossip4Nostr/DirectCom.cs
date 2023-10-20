using System;
using NBitcoin.Protocol;
using System.Diagnostics;
using GigGossipFrames;

namespace NGigGossip4Nostr;

public class DirectMessageEventArgs : EventArgs
{
    public required string EventId;
    public required string SenderPublicKey;
    public required DirectMessage Message { get; set; }
}

public class DirectCom : NostrNode
{
    public DirectCom(NostrNode me, string[] nostrRelays, int chunkSize) : base(me, nostrRelays, chunkSize)
    {
    }

    public event EventHandler<DirectMessageEventArgs> OnDirectMessage;

    public override void OnContactList(string eventId, Dictionary<string, NostrContact> contactList)
    {
    }

    public async Task SendDirectMessage(string targetPublicKey, DirectMessage directMessage)
    {
        await this.SendMessageAsync(targetPublicKey, directMessage, true);
    }

    public async override Task OnMessageAsync(string eventId, string senderPublicKey, object frame)
    {
        if (frame is DirectMessage)
        {
            OnDirectMessage.Invoke(this, new DirectMessageEventArgs()
            {
                EventId=eventId,
                SenderPublicKey=senderPublicKey,
                Message = (DirectMessage)frame
            });
        }
        else
        {
            Trace.TraceError("unknown request: ", senderPublicKey, frame);
        }
    }
}

