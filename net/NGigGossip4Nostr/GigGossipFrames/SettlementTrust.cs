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
    public SettlementPromise SettlementPromise { get; set; }

    /// <summary>
    /// Gets or sets the network invoice.
    /// </summary>
    public string NetworkInvoice { get; set; }

    /// <summary>
    /// Gets or sets the encrypted reply payload.
    /// </summary>
    public byte[] EncryptedReplyPayload { get; set; }
}
