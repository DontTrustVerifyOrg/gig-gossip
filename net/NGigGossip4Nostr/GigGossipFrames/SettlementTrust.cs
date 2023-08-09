using System;

namespace NGigGossip4Nostr;

[Serializable]
public class SettlementTrust
{
    public SettlementPromise SettlementPromise { get; set; }
    public string NetworkInvoice { get; set; }
    public byte[] EncryptedReplyPayload { get; set; }
}
