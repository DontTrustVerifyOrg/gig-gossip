using System;
namespace NGigGossip4Nostr;

public class ReplyPayload
{
    public Certificate ReplierCertificate { get; set; }
    public RequestPayload SignedRequestPayload { get; set; }
    public byte[] EncryptedReplyMessage { get; set; }
    public HodlInvoice ReplyInvoice { get; set; }

    public bool VerifyAll()
    {
        if (!this.ReplierCertificate.Verify())
        {
            return false;
        }

        if (!this.SignedRequestPayload.SenderCertificate.Verify())
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