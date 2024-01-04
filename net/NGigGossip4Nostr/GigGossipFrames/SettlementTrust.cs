using System;

namespace NGigGossip4Nostr;

/// <summary>
/// Represents a settlement trust.
/// </summary>
[Serializable]
public class SettlementTrust
{
    /// <summary>
    /// Gets or sets the settlement promise.
    /// </summary>
    /// <see cref="SettlementPromise"/>
    public required SettlementPromise SettlementPromise { get; set; }

    /// <summary>
    /// Gets or sets the network invoice.
    /// </summary>
    public required string NetworkInvoice { get; set; }

    /// <summary>
    /// Gets or sets the encrypted reply payload.
    /// </summary>
    public required byte[] EncryptedReplyPayload { get; set; }

    public required Guid ReplierCertificateId { get; set; }
}
