using System;
using CryptoToolkit;
using NBitcoin.Secp256k1;

namespace GigGossipFrames;

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
    public async Task<bool> VerifyAsync(byte[] encryptedSignedReplyPayload, ICertificationAuthorityAccessor caAccessor,CancellationToken cancellationToken)
    {
        var caPubKey = await caAccessor.GetPubKeyAsync(new Uri(this.MySecurityCenterUri), cancellationToken);
        var sign = Signature;
        try
        {
            Signature = null;
            if (Crypto.VerifyObject(this, sign.ToArray(), caPubKey))
                if (Crypto.ComputeSha256(encryptedSignedReplyPayload).SequenceEqual(this.HashOfEncryptedReplyPayload))
                    return true;
        }
        finally
        {
            Signature = sign;
        }
        return false;
    }

    /// <summary>
    /// Signs the settlement promise using a given private key.
    /// Overrides the base method from the <see cref="SignableObject"/> class.
    /// </summary>
    /// <param name="privateKey">
    /// The private key to use for signing.
    /// </param>
    /// <see cref="SignableObject.Sign(ECPrivKey)"/>
    public void Sign(ECPrivKey privateKey)
    {
        var sign = Signature;
        try
        {
            this.Signature = Google.Protobuf.ByteString.Empty;
            this.Signature = Crypto.SignObject(this, privateKey).AsByteString();
        }
        catch
        {
            this.Signature = sign;
            throw;
        }
    }

    /// <summary>
    /// Create a deep copy of this settlement promise.
    /// </summary>
    /// <returns>A deep copy of this instance.</returns>
    public SettlementPromise DeepCopy()
    {
        if (this.Signature == null)
            throw new InvalidOperationException("Object must be signed but the signature is null.");
        return new SettlementPromise()
        {
            MySecurityCenterUri = this.MySecurityCenterUri,
            TheirSecurityCenterUri = this.TheirSecurityCenterUri,
            NetworkPaymentHash = this.NetworkPaymentHash.ToArray().AsByteString(),
            HashOfEncryptedReplyPayload = this.HashOfEncryptedReplyPayload.ToArray().AsByteString(),
            ReplyPaymentAmountSat = this.ReplyPaymentAmountSat,
            Signature = this.Signature!.ToArray().AsByteString(),
        };
    }
}