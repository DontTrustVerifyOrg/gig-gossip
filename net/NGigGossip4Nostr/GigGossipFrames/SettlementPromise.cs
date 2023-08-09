using System;
using CryptoToolkit;
namespace NGigGossip4Nostr;

[Serializable]
public class SettlementPromise : SignableObject
{
    public Certificate SettlerCertificate { get; set; }
    public byte[] NetworkPaymentHash { get; set; }
    public byte[] HashOfEncryptedReplyPayload { get; set; }
    public long ReplyPaymentAmount { get; set; }

    public bool VerifyAll(byte[] encryptedSignedReplyPayload, ICertificationAuthorityAccessor caAccessor)
    {
        if (!this.SettlerCertificate.VerifyCertificate(caAccessor))
        {
            return false;
        }

        if (!this.Verify(this.SettlerCertificate.PublicKey))
        {
            return false;
        }

        if (!Crypto.ComputeSha256(new List<byte[]>() { encryptedSignedReplyPayload }).SequenceEqual(this.HashOfEncryptedReplyPayload))
        {
            return false;
        }

        return true;
    }

    public SettlementPromise DeepCopy()
    {
        return new SettlementPromise()
        {
            SettlerCertificate = this.SettlerCertificate,
            NetworkPaymentHash = this.NetworkPaymentHash.ToArray(),
            HashOfEncryptedReplyPayload = this.HashOfEncryptedReplyPayload.ToArray(),
            ReplyPaymentAmount = this.ReplyPaymentAmount,
            Signature = this.Signature.ToArray()
        };
    }
}