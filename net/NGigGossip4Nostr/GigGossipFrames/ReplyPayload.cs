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

    public bool Verify(ICertificationAuthorityAccessor caAccessor)
    {
        if (!this.ReplierCertificate.Verify(caAccessor))
        {
            return false;
        }

        if (!this.SignedRequestPayload.SenderCertificate.Verify(caAccessor))
        {
            return false;
        }

        if (!this.SignedRequestPayload.Verify(this.SignedRequestPayload.SenderCertificate.GetECXOnlyPubKey()))
        {
            return false;
        }

        return true;
    }
}