using System;
namespace NGigGossip4Nostr;

[Serializable]
public class AskForBroadcastFrame
{
    public Guid AskId { get; set; }
    public required RequestPayload SignedRequestPayload { get; set; }
}

