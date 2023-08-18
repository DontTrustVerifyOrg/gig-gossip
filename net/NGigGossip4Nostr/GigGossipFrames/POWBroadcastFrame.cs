using System;
using CryptoToolkit;
namespace NGigGossip4Nostr;

/// <summary>
/// Represents a broadcast frame in proof of work (POW) which contains the broadcast payload and the work proof.
/// </summary>
[Serializable]
public class POWBroadcastFrame
{
    /// <summary>
    /// Gets or sets the unique identifier (AskId) for the broadcast frame.
    /// </summary>
    public Guid AskId { get; set; }

    /// <summary>
    /// Gets or sets the payload information for the broadcast frame.
    /// </summary>
    /// <see cref="BroadcastPayload"/>
    public BroadcastPayload BroadcastPayload { get; set; }

    /// <summary>
    /// Gets or sets the ProofOfWork object associated with this broadcast frame.
    /// </summary>
    /// <see cref="ProofOfWork"/>
    public ProofOfWork ProofOfWork { get; set; }

    /// <summary>
    /// Verifies the integrity of the broadcast payload and the proof of work within the broadcast frame.
    /// </summary>
    /// <param name="caAccessor">The Certification Authority accessor used to verify the Sender Certificate.</param>
    /// <returns>Returns true if verification is successful, otherwise returns false.</returns>
    public bool Verify(ICertificationAuthorityAccessor caAccessor)
    {
        if (!this.BroadcastPayload.SignedRequestPayload.SenderCertificate.Verify(caAccessor))
        {
            return false;
        }

        if (!this.BroadcastPayload.SignedRequestPayload.Verify(this.BroadcastPayload.SignedRequestPayload.SenderCertificate.PublicKey.AsECXOnlyPubKey()))
        {
            return false;
        }

        return this.ProofOfWork.Validate(this.BroadcastPayload);
    }
}