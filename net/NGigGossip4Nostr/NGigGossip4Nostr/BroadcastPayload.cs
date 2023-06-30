using System;
namespace NGigGossip4Nostr;



public class BroadcastPayload
{
    public required RequestPayload SignedRequestPayload { get; set; }
    public required OnionRoute BackwardOnion { get; set; }
    public DateTime? Timestamp { get; set; }

    public void SetTimestamp(DateTime timestamp)
    {
        this.Timestamp = timestamp;
    }
}
