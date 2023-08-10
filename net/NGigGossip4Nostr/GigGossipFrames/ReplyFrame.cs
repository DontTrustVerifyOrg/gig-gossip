using System;
using NBitcoin.Secp256k1;
using CryptoToolkit;
namespace NGigGossip4Nostr;

[Serializable]
public class ReplyFrame
{
    public byte[] EncryptedReplyPayload { get; set; }
    public SettlementPromise SignedSettlementPromise { get; set; }
    public OnionRoute ForwardOnion { get; set; }
    public string NetworkInvoice { get; set; }

    public ReplyPayload DecryptAndVerify(ECPrivKey privKey, ECXOnlyPubKey pubKey, ICertificationAuthorityAccessor caAccessor)
    {
        ReplyPayload replyPayload = Crypto.DecryptObject<ReplyPayload>(this.EncryptedReplyPayload, privKey,pubKey);

        if (!replyPayload.ReplierCertificate.Verify(caAccessor))
        {
            return null;
        }

        if (!replyPayload.Verify(caAccessor))
        {
            return null;
        }

        return replyPayload;
    }

    public ReplyFrame DeepCopy()
    {
        return new ReplyFrame()
        {
            EncryptedReplyPayload = this.EncryptedReplyPayload.ToArray(),
            SignedSettlementPromise = this.SignedSettlementPromise.DeepCopy(),
            ForwardOnion = this.ForwardOnion.DeepCopy(),
            NetworkInvoice = new string(this.NetworkInvoice),
        };
    }
}
