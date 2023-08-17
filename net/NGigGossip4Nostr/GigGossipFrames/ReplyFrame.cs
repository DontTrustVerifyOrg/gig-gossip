using System;
using NBitcoin.Secp256k1;
using CryptoToolkit;
namespace NGigGossip4Nostr;

/// <summary>
/// Represents a reply frame that contains encrypted payload, settlement promise, onion route and network invoice.
/// </summary>
[Serializable]
public class ReplyFrame
{
    /// <summary>
    /// Gets or sets the encrypted reply payload.
    /// </summary>
    public byte[] EncryptedReplyPayload { get; set; }

    /// <summary>
    /// Gets or sets the signed settlement promise.
    /// </summary>
    /// <see cref="SettlementPromise"/>
    public SettlementPromise SignedSettlementPromise { get; set; }

    /// <summary>
    /// Gets or sets the forward onion route.
    /// </summary>
    /// <see cref="OnionRoute"/>
    public OnionRoute ForwardOnion { get; set; }

    /// <summary>
    /// Gets or sets the network invoice.
    /// </summary>
    public string NetworkInvoice { get; set; }

    /// <summary>
    /// Decrypts and verifies the encrypted reply payload.
    /// </summary>
    /// <param name="privKey">The private key for decryption.</param>
    /// <param name="pubKey">The public key for decryption.</param>
    /// <param name="caAccessor">The Certification Authority accessor used for verification.</param>
    /// <returns>Returns decrypted reply payload if verification is successful, otherwise returns null.</returns>
    public ReplyPayload DecryptAndVerify(ECPrivKey privKey, ECXOnlyPubKey pubKey, ICertificationAuthorityAccessor caAccessor)
    {
        ReplyPayload replyPayload = Crypto.DecryptObject<ReplyPayload>(this.EncryptedReplyPayload, privKey,pubKey);

        if (!replyPayload.ReplierCertificate.Verify(caAccessor))
        {
            return null;
        }

        if (!replyPayload.Verify(caAccessor))
        {
            return null;
        }

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
            EncryptedReplyPayload = this.EncryptedReplyPayload.ToArray(),
            SignedSettlementPromise = this.SignedSettlementPromise.DeepCopy(),
            ForwardOnion = this.ForwardOnion.DeepCopy(),
            NetworkInvoice = new string(this.NetworkInvoice),
        };
    }
}
