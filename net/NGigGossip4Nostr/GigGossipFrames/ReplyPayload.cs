using System;
using CryptoToolkit;
using NBitcoin.Secp256k1;

namespace NGigGossip4Nostr;

/// <summary>
/// Represents the payload of a reply message.
/// </summary>
[Serializable]
public class ReplyPayload
{
    /// <summary>
    /// Gets or sets the certificate of the replier.
    /// </summary>
    public Certificate ReplierCertificate { get; set; }

    /// <summary>
    /// Gets or sets the signed request payload.
    /// </summary>
    public RequestPayload SignedRequestPayload { get; set; }

    /// <summary>
    /// Gets or sets the encrypted reply message.
    /// </summary>
    public byte[] EncryptedReplyMessage { get; set; }

    /// <summary>
    /// Gets or sets the reply invoice.
    /// </summary>
    public string ReplyInvoice { get; set; }

    /// <summary>
    /// Verifies the validity of the reply payload.
    /// </summary>
    /// <param name="caAccessor">
    /// The certification authority accessor to use for verification.
    /// </param>
    /// <returns>
    /// <c>true</c> if the verification succeeded; otherwise, <c>false</c>.
    /// </returns>
    /// <see cref="Certificate.Verify(ICertificationAuthorityAccessor)"/>
    /// <see cref="RequestPayload.Verify(ECXOnlyPubKey)"/>
    public bool Verify(ICertificationAuthorityAccessor caAccessor)
    {
        if (!this.ReplierCertificate.Verify(caAccessor))
        {
            return false;
        }

        if (!this.SignedRequestPayload.SenderCertificate.Verify(caAccessor))
        {
            return false;
        }

        if (!this.SignedRequestPayload.Verify(this.SignedRequestPayload.SenderCertificate.PublicKey.AsECXOnlyPubKey()))
        {
            return false;
        }

        return true;
    }
}