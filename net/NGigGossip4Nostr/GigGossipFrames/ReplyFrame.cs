using System;
using NBitcoin.Secp256k1;

namespace GigGossip;

/// <summary>
/// Represents a reply frame that contains encrypted payload, settlement promise, onion route and network invoice.
/// </summary>
public partial class ReplyFrame
{
    /// <summary>
    /// Decrypts and verifies the encrypted reply payload.
    /// </summary>
    /// <param name="privKey">The private key for decryption.</param>
    /// <param name="pubKey">The public key for decryption.</param>
    /// <param name="caAccessor">The Certification Authority accessor used for verification.</param>
    /// <returns>Returns decrypted reply payload if verification is successful, otherwise returns null.</returns>
    public async Task<JobReply> DecryptAndVerifyAsync(ECPrivKey privKey, ECXOnlyPubKey pubKey, ICertificationAuthorityAccessor caAccessor, TimeSpan timestampTolerance, CancellationToken cancellationToken)
    {
        var replyPayload = Crypto.DecryptObject<JobReply>(this.EncryptedJobReply.Value.ToArray(), privKey, pubKey);

        if (!await replyPayload.VerifyAsync(caAccessor, timestampTolerance, cancellationToken))
            throw new InvalidOperationException();

        return replyPayload;
    }

}
