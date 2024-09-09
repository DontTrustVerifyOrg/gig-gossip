﻿using CryptoToolkit;
using NBitcoin.Secp256k1;
using Newtonsoft.Json.Linq;

namespace GigGossipFrames;

/// <summary>
/// Interface that allows access and operations related to the Certification Authority.
/// </summary>
public interface ICertificationAuthorityAccessor
{
    /// <summary>
    /// Method to get the Public Key of the certification authority
    /// </summary>
    /// <param name="serviceUri">The Uri of the Certifiation Authority service</param>
    /// <returns> Returns ECXOnlyPubKey of Certification Authority that can be used to validate signatures of e.g. issued certificates.</returns>
    public Task<ECXOnlyPubKey> GetPubKeyAsync(Uri serviceUri, CancellationToken cancellationToken);

    /// <summary>
    /// Method to check if a certificate is revoked
    /// </summary>
    /// <param name="id">A Digital Certificate id</param>
    /// <returns>Returns true if the certificate has been revoked, false otherwise. Usefull to implement revocation list.</returns>
    public Task<bool> IsRevokedAsync(Uri serviceUri, Guid id, CancellationToken cancellationToken);
}




public partial class Certificate
{
    /// <summary>
    /// Verifies the certificate with the Certification Authority public key.
    /// </summary>
    /// <param name="caAccessor">An instance of an object that implements ICertificationAuthorityAccessor</param>
    /// <returns>Returns true if the certificate is valid, false otherwise.</returns>
    public async Task<bool> VerifyAsync(ICertificationAuthorityAccessor caAccessor, CancellationToken cancellationToken)
    {
        if (NotValidAfter.AsUtcDateTime() >= DateTime.UtcNow && NotValidBefore.AsUtcDateTime() <= DateTime.UtcNow)
        {
            var caPubKey = await caAccessor.GetPubKeyAsync(new Uri(this.CertificationAuthorityUri), cancellationToken);
            var sign = Signature;
            try
            {
                Signature = null;
                if (Crypto.VerifyObject<Certificate>(this, sign.ToArray(), caPubKey))
                    return true;
            }
            finally
            {
                Signature = sign;
            }
        }
        return false;
    }

    /// <summary>
    /// Signs the certificate using the supplied private key of Certification Authority. For intertnal use only.
    /// </summary>
    /// <param name="privateKey">Private key of Certification Authority.</param>
    internal void Sign(ECPrivKey privateKey)
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

}

/// <summary>
/// A Certification Authority (CA) is a trusted entity responsible for issuing and managing digital certificates
/// </summary>
public class CertificationAuthority
{
    /// <summary>
    /// The Uri of the certification authority's service.
    /// </summary>
    public Uri ServiceUri { get; set; }

    /// <summary>
    /// A private key of Certification Authority. 
    /// </summary>
    protected ECPrivKey _CaPrivateKey { get; set; }

    /// <summary>
    /// A public key of Certification Authority.
    /// </summary>
    public ECXOnlyPubKey CaXOnlyPublicKey { get; set; }

    /// <summary>
    /// Constructor for the CertificationAuthority class.
    /// Establishes the service URI, and initializes the CA's private key and public key.
    /// </summary>
    public CertificationAuthority(Uri serviceUri, ECPrivKey caPrivateKey)
    {
        ServiceUri = serviceUri;
        _CaPrivateKey = caPrivateKey;
        CaXOnlyPublicKey = caPrivateKey.CreateXOnlyPubKey();
    }

    /// <summary>
    /// Issues a new certificate with provided details.
    /// </summary>
    /// <param name="properties">Properties of the Subject.</param>
    /// <param name="notValidAfter">The date after which the certificate is not valid.</param>
    /// <param name="notValidBefore">The date before which the certificate is not valid.</param>
    /// <returns>A new certificate signed and issued by the Certification Authority for the Subject.</returns>
    protected Certificate IssueCertificate<T>(string kind, Guid id, IDictionary<string, byte[]> properties, DateTime notValidAfter, DateTime notValidBefore, T value) where T : Google.Protobuf.IMessage<T>
    {
        var certificate = new Certificate
        {
            Kind = kind,
            Id = id.AsUUID(),
            CertificationAuthorityUri = ServiceUri.AbsoluteUri,
            NotValidAfter = notValidAfter.AsUnixTimestamp(),
            NotValidBefore = notValidBefore.AsUnixTimestamp(),
            Value = Crypto.BinarySerializeObject(value).AsByteString(),
        };
        certificate.Properties.Add(new Dictionary<string, Google.Protobuf.ByteString>(from kv in properties select KeyValuePair.Create(kv.Key, kv.Value.AsByteString())));
        certificate.Sign(this._CaPrivateKey);
        return certificate;
    }
}
