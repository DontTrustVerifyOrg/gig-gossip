using CryptoToolkit;
using NBitcoin.Secp256k1;

namespace GigGossip;

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


public partial class CertificateHeader
{
    /// <summary>
    /// Verifies the certificate with the Certification Authority public key.
    /// </summary>
    /// <param name="caAccessor">An instance of an object that implements ICertificationAuthorityAccessor</param>
    /// <returns>Returns true if the certificate is valid, false otherwise.</returns>
    public bool IsStillValid => NotValidBefore.AsUtcDateTime() <= DateTime.UtcNow && DateTime.UtcNow <= NotValidAfter.AsUtcDateTime();
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

    protected Signature Sign<T>(T obj) where T: Google.Protobuf.IMessage<T>
    {
        return obj.Sign(this._CaPrivateKey);
    }
}
