using System;

using NBitcoin.Secp256k1;

namespace GigGossip;

/// <summary>
/// Represents a settlement promise.
/// </summary>
public partial class SettlementPromise
{
    /// <summary>
    /// Verifies the settlement promise.
    /// </summary>
    /// <param name="encryptedSignedReplyPayload">The encrypted signed reply payload.</param>
    /// <param name="caAccessor">The certification authority accessor.</param>
    /// <returns><c>true</c> if the verification was successful; otherwise, <c>false</c>.</returns>
    public async Task<bool> VerifyAsync(byte[] encryptedSignedReplyPayload, ICertificationAuthorityAccessor caAccessor, Signature signature, CancellationToken cancellationToken)
    {
        var caPubKey = await caAccessor.GetPubKeyAsync(this.Header.MySecurityCenterUri.AsUri(), cancellationToken);
        if (Crypto.VerifyObject(this, signature.Value.ToArray(), caPubKey))
            if (Crypto.ComputeSha256(encryptedSignedReplyPayload).SequenceEqual(this.Header.HashOfEncryptedJobReply.Value))
                return true;
        return false;
    }

}