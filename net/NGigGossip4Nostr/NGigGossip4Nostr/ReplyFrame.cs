using System;
using NBitcoin.Secp256k1;
namespace NGigGossip4Nostr;

public class ReplyFrame
{
    public byte[] EncryptedReplyPayload { get; set; }
    public SettlementPromise SignedSettlementPromise { get; set; }
    public OnionRoute ForwardOnion { get; set; }
    public HodlInvoice NetworkInvoice { get; set; }

    public ReplyPayload DecryptAndVerify(ECPrivKey privKey, ECXOnlyPubKey pubKey)
    {
        ReplyPayload replyPayload = (ReplyPayload) Crypto.DecryptObject(this.EncryptedReplyPayload, privKey,pubKey);

        if (!replyPayload.ReplierCertificate.Verify())
        {
            return null;
        }

        if (!replyPayload.VerifyAll())
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
            NetworkInvoice = this.NetworkInvoice.DeepCopy()
        };
    }
}
