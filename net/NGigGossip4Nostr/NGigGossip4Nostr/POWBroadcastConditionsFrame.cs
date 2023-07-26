using System;
namespace NGigGossip4Nostr;

[Serializable]
public class POWBroadcastConditionsFrame
{
    public Guid AskId { get; set; }
    public DateTime ValidTill { get; set; }
    public WorkRequest WorkRequest { get; set; }
    public TimeSpan TimestampTolerance { get; set; }
}