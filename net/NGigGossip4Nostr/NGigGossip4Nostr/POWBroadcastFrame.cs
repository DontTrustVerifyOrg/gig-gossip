using System;
namespace NGigGossip4Nostr;
public class POWBroadcastFrame
{
    public Guid AskId { get; set; }
    public BroadcastPayload BroadcastPayload { get; set; }
    public ProofOfWork ProofOfWork { get; set; }

    public bool Verify()
    {
        if (!this.BroadcastPayload.SignedRequestPayload.SenderCertificate.Verify())
        {
            return false;
        }

        if (!this.BroadcastPayload.SignedRequestPayload.Verify(this.BroadcastPayload.SignedRequestPayload.SenderCertificate.PublicKey))
        {
            return false;
        }

        return this.ProofOfWork.Validate(this.BroadcastPayload);
    }
}