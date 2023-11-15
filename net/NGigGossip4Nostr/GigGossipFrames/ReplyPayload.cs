using System;
using CryptoToolkit;
using NBitcoin.Secp256k1;

namespace NGigGossip4Nostr;

/// <summary>
/// Represents the payload of a reply message.
/// </summary>
[Serializable]
public class ReplyPayloadValue
{
    /// <summary>
    /// Gets or sets the signed request payload.
    /// </summary>
    public required Certificate<RequestPayloadValue> SignedRequestPayload { get; set; }

    /// <summary>
    /// Gets or sets the encrypted reply message.
    /// </summary>
    public required byte[] EncryptedReplyMessage { get; set; }

    /// <summary>
    /// Gets or sets the reply invoice.
    /// </summary>
    public required string ReplyInvoice { get; set; }

    /// <summary>
    /// Gets or sets creation timestamp of the payload.
    /// </summary>
    public required DateTime Timestamp { get; set; }
}

