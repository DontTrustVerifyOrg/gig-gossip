using System;
using CryptoToolkit;
using NBitcoin.Secp256k1;
using ProtoBuf;

namespace NGigGossip4Nostr;

/// <summary>
/// Represents the payload of a request message.
/// </summary>
[ProtoContract]
public class CancelRequestPayloadValue
{
    /// <summary>
    /// Gets or sets creation timestamp of the payload.
    /// </summary>
    [ProtoMember(1)]
    public required DateTime Timestamp { get; set; }
}
