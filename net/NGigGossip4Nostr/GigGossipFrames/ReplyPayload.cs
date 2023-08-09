using System;
using CryptoToolkit;
using NBitcoin.Secp256k1;

namespace NGigGossip4Nostr;

[Serializable]
public class ReplyPayload
{
    public Certificate ReplierCertificate { get; set; }
    public RequestPayload SignedRequestPayload { get; set; }
    public byte[] EncryptedReplyMessage { get; set; }
    public string ReplyInvoice { get; set; }

    public bool VerifyAll(ICertificationAuthorityAccessor caAccessor)
    {
        if (!this.ReplierCertificate.VerifyCertificate(caAccessor))
        {
            return false;
        }

        if (!this.SignedRequestPayload.SenderCertificate.VerifyCertificate(caAccessor))
        {
            return false;
        }

        if (!this.SignedRequestPayload.Verify(this.SignedRequestPayload.SenderCertificate.PublicKey))
        {
            return false;
        }

        return true;
    }
}