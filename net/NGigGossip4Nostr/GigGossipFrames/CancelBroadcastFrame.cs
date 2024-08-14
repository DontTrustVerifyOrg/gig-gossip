using System;
using CryptoToolkit;
using ProtoBuf;

namespace NGigGossip4Nostr;

/// <summary>
/// Represents a request for a broadcast.
/// </summary>
[ProtoContract]
public class CancelBroadcastFrame
{
    /// <summary>
    /// Gets or sets the signed payload for the request. This contains the necessary data for processing the request.
    /// </summary>
    [ProtoMember(1)]
    public required Certificate<CancelRequestPayloadValue> SignedCancelRequestPayload { get; set; }
}

