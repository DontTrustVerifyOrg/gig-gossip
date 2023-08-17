using NBitcoin.Secp256k1;

namespace CryptoToolkit;

/// <summary>
/// Interface that allows access and operations related to the Certification Authority.
/// </summary>
public interface ICertificationAuthorityAccessor
{
    /// <summary>
    /// Method to get the Public Key
    /// </summary>
    /// <param name="serviceUri">The Uri of the Certifiation Authority service</param>
    /// <returns> Returns ECXOnlyPubKey of Certification Authority that can be used to validate signatures of issued certificates.</returns>
    public ECXOnlyPubKey GetPubKey(Uri serviceUri);

    /// <summary>
    /// Method to check if a certificate is revoked
    /// </summary>
    /// <param name="certificate">A Digital Certificate object</param>
    /// <returns>Returns true if the certificate has been revoked, false otherwise.</returns>
    public bool IsRevoked(Certificate certificate);
}

/// <summary>
/// A Digital Certificate issued by Certification Authority for the Subject
/// </summary>
[Serializable]
public class Certificate : SignableObject
{  
   /// <summary>
   /// The Uri of the Certification Authority service
   /// </summary>
   public Uri ServiceUri { get; set; }
   
   /// <summary>
   /// Serial number of the certificate
   /// </summary>
   public Guid Id { get; set; }

   /// <summary>
   /// hex-encoded string representation of the public key of the Subject
   /// </summary>
   public string PublicKey { get; set; }

   /// <summary>
   /// Collection of certified properties of the Subject
   /// </summary>
   public Dictionary<string, byte[]> Properties { get; set; }

   /// <summary>
   /// Date and Time when the Certificate will no longer be valid
   /// </summary>
   public DateTime NotValidAfter { get; set; }

   /// <summary>
   /// Date and Time before which the Certificate is not yet valid
   /// </summary>
   public DateTime NotValidBefore { get; set; }

   /// <summary>
   /// Method to get the ECXOnlyPubKey of the Subject
   /// </summary>
   /// <returns> Returns ECXOnlyPubKey.</returns>
   public ECXOnlyPubKey GetECXOnlyPubKey()
   {
      return ECXOnlyPubKey.Create(Convert.FromHexString(PublicKey));
   }

   /// <summary>
   /// Verifies the certificate with the Certification Authority public key.
   /// </summary>
   /// <param name="caAccessor">An instance of an object that implements ICertificationAuthorityAccessor</param>
   /// <returns>Returns true if the certificate is valid, false otherwise.</returns>
   public new bool Verify(ICertificationAuthorityAccessor caAccessor)
   {
       if (NotValidAfter >= DateTime.Now && NotValidBefore <= DateTime.Now)
       {
           if (Verify(caAccessor.GetPubKey(this.ServiceUri)))
               return true;
       }
       return false;
   }

   /// <summary>
   /// Signs the certificate using the supplied private key of Certification Authority. For intertnal use only.
   /// </summary>
   /// <param name="privateKey">Private key of Certification Authority.</param>
   internal new void Sign(ECPrivKey privateKey)
   {
       base.Sign(privateKey);
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
   /// <param name="ecxOnlypublicKey">Public key of the Subject.</param>
   /// <param name="properties">Properties of the Subject.</param>
   /// <param name="notValidAfter">The date after which the certificate is not valid.</param>
   /// <param name="notValidBefore">The date before which the certificate is not valid.</param>
   /// <returns>A new certificate signed and issued by the Certification Authority for the Subject.</returns>
   public Certificate IssueCertificate(ECXOnlyPubKey ecxOnlypublicKey, Dictionary<string, byte[]> properties, DateTime notValidAfter, DateTime notValidBefore)
   {
       var certificate = new Certificate
       {
           Id = Guid.NewGuid(),
           ServiceUri = ServiceUri,
           PublicKey = ecxOnlypublicKey.AsHex(),
           Properties = properties,
           NotValidAfter = notValidAfter,
           NotValidBefore = notValidBefore
       };
       certificate.Sign(this._CaPrivateKey);
       return certificate;
   }
}
