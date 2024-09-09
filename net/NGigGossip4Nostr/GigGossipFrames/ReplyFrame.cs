using System;
using NBitcoin.Secp256k1;
using CryptoToolkit;

namespace GigGossipFrames;

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
    public async Task<Certificate> DecryptAndVerifyAsync(ECPrivKey privKey, ECXOnlyPubKey pubKey, ICertificationAuthorityAccessor caAccessor, CancellationToken cancellationToken)
    {
        var replyPayload = Crypto.DecryptObject<Certificate>(this.EncryptedReplyPayload.ToArray(), privKey, pubKey);

        if (!await replyPayload.VerifyAsync(caAccessor, cancellationToken))
            throw new InvalidOperationException();

        return replyPayload;
    }

    /// <summary>
    /// Creates a deep copy of the ReplyFrame instance
    /// </summary>
    /// <returns>A new ReplyFrame object that is a deep copy of this instance.</returns>
    public ReplyFrame DeepCopy()
    {
        return new ReplyFrame()
        {
            EncryptedReplyPayload = this.EncryptedReplyPayload.ToArray().AsByteString(),
            SignedSettlementPromise = this.SignedSettlementPromise.DeepCopy(),
            ForwardOnion = this.ForwardOnion.DeepCopy(),
            NetworkInvoice = new string(this.NetworkInvoice),
        };
    }
}
