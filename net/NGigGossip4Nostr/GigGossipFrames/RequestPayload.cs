using System;
using CryptoToolkit;
using NBitcoin.Secp256k1;

namespace NGigGossip4Nostr;

/// <summary>
/// Represents the payload of a request message.
/// </summary>
[Serializable]
public class RequestPayloadValue
{
    /// <summary>
    /// Gets or sets the identifier of the payload.
    /// </summary>
    public required Guid PayloadId { get; set; }

    /// <summary>
    /// Gets or sets the topic of the payload.
    /// </summary>
    public required byte[] Topic { get; set; }

}
