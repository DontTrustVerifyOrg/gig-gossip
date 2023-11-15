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
    /// Gets or sets the signed payload for the request. This contains the necessary data for processing the request.
    /// </summary>
    public required Certificate<CancelRequestPayloadValue> SignedCancelRequestPayload { get; set; }
}

