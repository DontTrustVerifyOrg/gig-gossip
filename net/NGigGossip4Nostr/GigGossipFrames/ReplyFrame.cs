using System;
using NBitcoin.Secp256k1;
using CryptoToolkit;
using ProtoBuf;

namespace NGigGossip4Nostr;

/// <summary>
/// Represents a reply frame that contains encrypted payload, settlement promise, onion route and network invoice.
/// </summary>
[ProtoContract]
public class ReplyFrame
{
    /// <summary>
    /// Gets or sets the encrypted reply payload.
    /// </summary>
    [ProtoMember(1)]
    public required byte[] EncryptedReplyPayload { get; set; }

    /// <summary>
    /// Gets or sets the signed settlement promise.
    /// </summary>
    /// <see cref="SettlementPromise"/>
    [ProtoMember(2)]
    public required SettlementPromise SignedSettlementPromise { get; set; }

    /// <summary>
    /// Gets or sets the forward onion route.
    /// </summary>
    /// <see cref="OnionRoute"/>
    [ProtoMember(3)]
    public required OnionRoute ForwardOnion { get; set; }

    /// <summary>
    /// Gets or sets the network invoice.
    /// </summary>
    [ProtoMember(4)]
    public required string NetworkInvoice { get; set; }

    /// <summary>
    /// Decrypts and verifies the encrypted reply payload.
    /// </summary>
    /// <param name="privKey">The private key for decryption.</param>
    /// <param name="pubKey">The public key for decryption.</param>
    /// <param name="caAccessor">The Certification Authority accessor used for verification.</param>
    /// <returns>Returns decrypted reply payload if verification is successful, otherwise returns null.</returns>
    public async Task<Certificate<ReplyPayloadValue>> DecryptAndVerifyAsync(ECPrivKey privKey, ECXOnlyPubKey pubKey, ICertificationAuthorityAccessor caAccessor, CancellationToken cancellationToken)
    {
        var replyPayload = Crypto.DecryptObject<Certificate<ReplyPayloadValue>>(this.EncryptedReplyPayload, privKey, pubKey);

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
            EncryptedReplyPayload = this.EncryptedReplyPayload.ToArray(),
            SignedSettlementPromise = this.SignedSettlementPromise.DeepCopy(),
            ForwardOnion = this.ForwardOnion.DeepCopy(),
            NetworkInvoice = new string(this.NetworkInvoice),
        };
    }
}
