﻿using NBitcoin.Secp256k1;
using ProtoBuf;

namespace CryptoToolkit;

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

[ProtoContract]
public class PropertyValue
{
    [ProtoMember(1)]
    public required string Name { get; set; }
    [ProtoMember(2)]
    public required byte[] Value { get; set; }
}

/// <summary>
/// A Digital Certificate issued by Certification Authority for the Subject
/// </summary>
[ProtoContract]
public class Certificate<T> : IProtoFrame
{
    [ProtoMember(1)]
    public byte[]? Signature { get; set; }

    /// <summary>
    /// The Uri of the Certification Authority service
    /// </summary>
    [ProtoMember(2)]
    public required Uri ServiceUri { get; set; }

    /// <summary>
    /// Kind of the certificate
    /// </summary>
    [ProtoMember(3)]
    public required string Kind { get; set; }

    /// <summary>
    /// Serial number of the certificate
    /// </summary>
    [ProtoMember(4)]
    public required Guid Id { get; set; }

    /// <summary>
    /// Collection of certified properties of the Subject
    /// </summary>
    [ProtoMember(5)]
    public required PropertyValue[] Properties { get; set; }

    /// <summary>
    /// Date and Time when the Certificate will no longer be valid
    /// </summary>
    [ProtoMember(6)]
    public required DateTime NotValidAfter { get; set; }

    /// <summary>
    /// Date and Time before which the Certificate is not yet valid
    /// </summary>
    [ProtoMember(7)]
    public required DateTime NotValidBefore { get; set; }

    /// <summary>
    /// The value managed by the certificate
    /// </summary>
    [ProtoMember(8)]
    public required T Value { get; set; }

    /// <summary>
    /// Verifies the certificate with the Certification Authority public key.
    /// </summary>
    /// <param name="caAccessor">An instance of an object that implements ICertificationAuthorityAccessor</param>
    /// <returns>Returns true if the certificate is valid, false otherwise.</returns>
    public async Task<bool> VerifyAsync(ICertificationAuthorityAccessor caAccessor, CancellationToken cancellationToken)
    {
        if (NotValidAfter >= DateTime.UtcNow && NotValidBefore <= DateTime.UtcNow)
        {
            var caPubKey = await caAccessor.GetPubKeyAsync(this.ServiceUri, cancellationToken);
            var sign = Signature;
            try
            {
                Signature = null;
                if (Crypto.VerifyObject<Certificate<T>>(this, sign, caPubKey))
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
            this.Signature = null;
            this.Signature = Crypto.SignObject(this, privateKey);
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
   protected Certificate<T> IssueCertificate<T>(string kind, Guid id, PropertyValue[] properties, DateTime notValidAfter, DateTime notValidBefore, T value)
   {
       var certificate = new Certificate<T>
       {
           Kind = kind,
           Id = id,
           ServiceUri = ServiceUri,
           Properties = properties,
           NotValidAfter = notValidAfter,
           NotValidBefore = notValidBefore,
           Value = value,
       };
       certificate.Sign(this._CaPrivateKey);
       return certificate;
   }
}
