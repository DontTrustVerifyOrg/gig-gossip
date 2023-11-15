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
    /// Gets or sets the payload information for the broadcast frame.
    /// </summary>
    /// <see cref="TheBroadcastPayload"/>
    public required BroadcastPayload TheBroadcastPayload { get; set; }

    /// <summary>
    /// Verifies the integrity of the broadcast payload and the proof of work within the broadcast frame.
    /// </summary>
    /// <param name="caAccessor">The Certification Authority accessor used to verify the Sender Certificate.</param>
    /// <returns>Returns true if verification is successful, otherwise returns false.</returns>
    public async Task<bool> VerifyAsync(ICertificationAuthorityAccessor caAccessor)
    {
        return await this.TheBroadcastPayload.SignedRequestPayload.VerifyAsync(caAccessor);
    }
}