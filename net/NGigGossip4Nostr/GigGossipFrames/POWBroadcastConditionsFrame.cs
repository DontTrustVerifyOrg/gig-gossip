using System;
namespace NGigGossip4Nostr;

/// <summary>
/// Represents a frame for broadcasting conditions in proof of work (POW)
/// </summary>
[Serializable]
public class POWBroadcastConditionsFrame
{
    /// <summary>
    /// Gets or sets the unique identifier (AskId) of the broadcast condition.
    /// </summary>
    public Guid AskId { get; set; }

    /// <summary>
    /// Gets or sets the validity period of the broadcast condition.
    /// </summary>
    public DateTime ValidTill { get; set; }

    /// <summary>
    /// Gets or sets the work request associated with this broadcast condition.
    /// </summary>
    /// <see cref="WorkRequest"/>
    public WorkRequest WorkRequest { get; set; }

    /// <summary>
    /// Gets or sets the timestamp tolerance for this broadcast condition.
    /// </summary>
    public TimeSpan TimestampTolerance { get; set; }
}