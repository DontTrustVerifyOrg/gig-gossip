using System;
using CryptoToolkit;
using NBitcoin.Secp256k1;
using ProtoBuf;

namespace NGigGossip4Nostr;

/// <summary>
/// Represents the payload of a request message.
/// </summary>
[ProtoContract]
public class RequestPayloadValue : IProtoFrame
{
    /// <summary>
    /// Gets or sets the topic of the payload.
    /// </summary>
    [ProtoMember(1)]
    public required byte[] Topic { get; set; }

    /// <summary>
    /// Gets or sets creation timestamp of the payload.
    /// </summary>
    [ProtoMember(2)]
    public required DateTime Timestamp { get; set; }
}
