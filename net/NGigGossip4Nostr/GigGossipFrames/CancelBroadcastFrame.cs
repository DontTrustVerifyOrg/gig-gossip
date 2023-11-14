using System;
using CryptoToolkit;

namespace NGigGossip4Nostr;

/// <summary>
/// Represents a request for a broadcast.
/// </summary>
[Serializable]
public class CancelBroadcastFrame
{
    /// <summary>
    /// Gets or sets the unique identifier for the `AskForBroadcastFrame`.
    /// This is used to track individual ask for broadcast requests.
    /// </summary>
    public required Guid CancelId { get; set; }

    /// <summary>
    /// Gets or sets the signed payload for the request. This contains the necessary data for processing the request.
    /// </summary>
    public required Certificate<CancelRequestPayloadValue> SignedCancelRequestPayload { get; set; }
}

