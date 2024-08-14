using System;
using CryptoToolkit;
using ProtoBuf;

namespace NGigGossip4Nostr;

/// <summary>
/// Represents a broadcast frame in proof of work (POW) which contains the broadcast payload and the work proof.
/// </summary>
[ProtoContract]
public class BroadcastFrame
{
    /// <summary>
    /// Gets or sets the signed payload for the request. This contains the necessary data for processing the request.
    /// </summary>
    [ProtoMember(1)]
    public required Certificate<RequestPayloadValue> SignedRequestPayload { get; set; }

    /// <summary>
    /// Gets or sets the Onion Route used for back-routing of the message.
    /// </summary>
    [ProtoMember(2)]
    public required OnionRoute BackwardOnion { get; set; }
}