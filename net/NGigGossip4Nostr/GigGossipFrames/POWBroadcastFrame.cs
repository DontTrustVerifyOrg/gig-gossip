using System;
using CryptoToolkit;
namespace NGigGossip4Nostr;


[Serializable]
public class POWBroadcastFrame
{
    public Guid AskId { get; set; }
    public BroadcastPayload BroadcastPayload { get; set; }
    public ProofOfWork ProofOfWork { get; set; }

    public bool Verify(ICertificationAuthorityAccessor caAccessor)
    {
        if (!this.BroadcastPayload.SignedRequestPayload.SenderCertificate.VerifyCertificate(caAccessor))
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