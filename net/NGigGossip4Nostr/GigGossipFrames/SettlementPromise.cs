using System;
using CryptoToolkit;
using NBitcoin.Secp256k1;
using ProtoBuf;

namespace NGigGossip4Nostr;

/// <summary>
/// Represents a settlement promise.
/// </summary>
[ProtoContract]
public class SettlementPromise
{
    [ProtoMember(1)]
    public byte[]? Signature { get; set; }

    /// <summary>
    /// Gets or sets the service URI of the Settler.
    /// </summary>
    [ProtoMember(2)]
    public required Uri ServiceUri { get; set; }

    /// <summary>
    /// Gets or sets the service URI of the Requester Settler.
    /// </summary>
    [ProtoMember(3)]
    public required Uri RequestersServiceUri { get; set; }

    /// <summary>
    /// Gets or sets the network payment hash.
    /// </summary>
    [ProtoMember(4)]
    public required byte[] NetworkPaymentHash { get; set; }

    /// <summary>
    /// Gets or sets the hash of encrypted reply payload.
    /// </summary>
    [ProtoMember(5)]
    public required byte[] HashOfEncryptedReplyPayload { get; set; }

    /// <summary>
    /// Gets or sets the reply payment amount.
    /// </summary>
    [ProtoMember(6)]
    public required long ReplyPaymentAmount { get; set; }

    /// <summary>
    /// Verifies the settlement promise.
    /// </summary>
    /// <param name="encryptedSignedReplyPayload">The encrypted signed reply payload.</param>
    /// <param name="caAccessor">The certification authority accessor.</param>
    /// <returns><c>true</c> if the verification was successful; otherwise, <c>false</c>.</returns>
    public async Task<bool> VerifyAsync(byte[] encryptedSignedReplyPayload, ICertificationAuthorityAccessor caAccessor,CancellationToken cancellationToken)
    {
        var caPubKey = await caAccessor.GetPubKeyAsync(this.ServiceUri, cancellationToken);
        var sign = Signature;
        try
        {
            Signature = null;
            if (Crypto.VerifyObject(this, sign, caPubKey))
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
            this.Signature = null;
            this.Signature = Crypto.SignObject(this, privateKey);
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
            ServiceUri = this.ServiceUri,
            RequestersServiceUri = this.RequestersServiceUri,
            NetworkPaymentHash = this.NetworkPaymentHash.ToArray(),
            HashOfEncryptedReplyPayload = this.HashOfEncryptedReplyPayload.ToArray(),
            ReplyPaymentAmount = this.ReplyPaymentAmount,
            Signature = this.Signature!.ToArray()
        };
    }
}