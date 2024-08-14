using System;
using ProtoBuf;

namespace NGigGossip4Nostr;

/// <summary>
/// Represents a settlement trust.
/// </summary>
[ProtoContract]
public class SettlementTrust
{
    /// <summary>
    /// Gets or sets the settlement promise.
    /// </summary>
    /// <see cref="SettlementPromise"/>
    [ProtoMember(1)]
    public required SettlementPromise SettlementPromise { get; set; }

    /// <summary>
    /// Gets or sets the network invoice.
    /// </summary>
    [ProtoMember(2)]
    public required string NetworkInvoice { get; set; }

    /// <summary>
    /// Gets or sets the encrypted reply payload.
    /// </summary>
    [ProtoMember(3)]
    public required byte[] EncryptedReplyPayload { get; set; }

    [ProtoMember(4)]
    public required Guid ReplierCertificateId { get; set; }
}
