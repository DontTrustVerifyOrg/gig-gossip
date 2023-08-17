using System;
using CryptoToolkit;
using NBitcoin.Secp256k1;

namespace NGigGossip4Nostr;

/// <summary>
/// Represents the payload of a request message.
/// </summary>
[Serializable]
public class RequestPayload : SignableObject
{
    /// <summary>
    /// Gets or sets the identifier of the payload.
    /// </summary>
    public Guid PayloadId { get; set; }

    /// <summary>
    /// Gets or sets the topic of the payload.
    /// </summary>
    public byte[] Topic { get; set; }

    /// <summary>
    /// Gets or sets the certificate of the sender.
    /// </summary>
    public Certificate SenderCertificate { get; set; }

    /// <summary>
    /// Signs the request payload using a given private key.
    /// Overrides the base method from the <see cref="SignableObject"/> class.
    /// </summary>
    /// <param name="privateKey">
    /// The private key to use for signing.
    /// </param>
    /// <see cref="SignableObject.Sign(ECPrivKey)"/>
    public new void Sign(ECPrivKey privateKey)
    {
        base.Sign(privateKey);
    }

    /// <summary>
    /// Verifies the signature of the request payload using a given public key.
    /// Overrides the base method from the <see cref="SignableObject"/> class.
    /// </summary>
    /// <param name="publicKey">
    /// The public key to use for verification.
    /// </param>
    /// <returns>
    /// <c>true</c> if the verification succeeded; otherwise, <c>false</c>.
    /// </returns>
    /// <see cref="SignableObject.Verify(ECXOnlyPubKey)"/>
    public new bool Verify(ECXOnlyPubKey publicKey)
    {
        return base.Verify(publicKey);
    }

}