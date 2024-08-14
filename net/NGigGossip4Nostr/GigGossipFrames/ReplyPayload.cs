using System;
using CryptoToolkit;
using NBitcoin.Secp256k1;
using ProtoBuf;

namespace NGigGossip4Nostr;

/// <summary>
/// Represents the payload of a reply message.
/// </summary>
[ProtoContract]
public class ReplyPayloadValue : IProtoFrame
{
    /// <summary>
    /// Gets or sets the signed request payload.
    /// </summary>
    [ProtoMember(1)]
    public required Certificate<RequestPayloadValue> SignedRequestPayload { get; set; }

    /// <summary>
    /// Gets or sets the encrypted reply message.
    /// </summary>
    [ProtoMember(2)]
    public required byte[] EncryptedReplyMessage { get; set; }

    /// <summary>
    /// Gets or sets the reply invoice.
    /// </summary>
    [ProtoMember(3)]
    public required string ReplyInvoice { get; set; }

    /// <summary>
    /// Gets or sets creation timestamp of the payload.
    /// </summary>
    [ProtoMember(4)]
    public required DateTime Timestamp { get; set; }
}

