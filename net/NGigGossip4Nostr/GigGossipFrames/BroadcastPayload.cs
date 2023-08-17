using System;
namespace NGigGossip4Nostr;

/// <summary>
/// Represents a payload for a broadcast
/// Includes the signed request payload, an onion route for backwards routing and a timestamp.
/// </summary>
[Serializable]
public class BroadcastPayload
{
    /// <summary>
    /// Gets or sets the signed payload for the request. This contains the necessary data for processing the request.
    /// </summary>
    public required RequestPayload SignedRequestPayload { get; set; }

    /// <summary>
    /// Gets or sets the Onion Route used for back-routing of the message.
    /// </summary>
    public required OnionRoute BackwardOnion { get; set; }
 
    /// <summary>
    /// Gets or sets the optional timestamp of the broadcast.
    /// </summary>
    public DateTime? Timestamp { get; set; }

    /// <summary>
    /// Set the timestamp of the broadcast payload to the specified date and time.
    /// </summary>
    /// <param name="timestamp">The Date and Time to assign as the timestamp.</param>
    public void SetTimestamp(DateTime timestamp)
    {
        this.Timestamp = timestamp;
    }
}
