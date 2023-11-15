using System;
using CryptoToolkit;

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
    public required Certificate<RequestPayloadValue> SignedRequestPayload { get; set; }

    /// <summary>
    /// Gets or sets the Onion Route used for back-routing of the message.
    /// </summary>
    public required OnionRoute BackwardOnion { get; set; }
}
