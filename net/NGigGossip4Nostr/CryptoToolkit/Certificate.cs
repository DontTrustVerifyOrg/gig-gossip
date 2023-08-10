using NBitcoin.Secp256k1;
namespace CryptoToolkit;

public interface ICertificationAuthorityAccessor
{
    public ECXOnlyPubKey GetPubKey(Uri serviceUri);
    public bool IsRevoked(Certificate certificate);
}

[Serializable]
public class Certificate : SignableObject
{
    public Uri ServiceUri { get; set; }
    public Guid Id { get; set; }
    public string PublicKey { get; set; }

    public Dictionary<string, byte[]> Properties { get; set; }
    public DateTime NotValidAfter { get; set; }
    public DateTime NotValidBefore { get; set; }

    public ECXOnlyPubKey GetECXOnlyPubKey()
    {
        return ECXOnlyPubKey.Create(Convert.FromHexString(PublicKey));
    }

    public new bool Verify(ICertificationAuthorityAccessor caAccessor)
    {
        if (NotValidAfter >= DateTime.Now && NotValidBefore <= DateTime.Now)
        {
            if (Verify(caAccessor.GetPubKey(this.ServiceUri)))
                return true;
        }
        return false;
    }

    internal new void Sign(ECPrivKey privateKey)
    {
        base.Sign(privateKey);
    }
}

public class CertificationAuthority
{
    public Uri ServiceUri { get; set; }
    protected ECPrivKey _CaPrivateKey { get; set; }
    public ECXOnlyPubKey CaXOnlyPublicKey { get; set; }

    public CertificationAuthority(Uri serviceUri, ECPrivKey caPrivateKey)
    {
        ServiceUri = serviceUri;
        _CaPrivateKey = caPrivateKey;
        CaXOnlyPublicKey = caPrivateKey.CreateXOnlyPubKey();
    }

    public Certificate IssueCertificate(ECXOnlyPubKey caxOnlypublicKey, Dictionary<string, byte[]> properties, DateTime notValidAfter, DateTime notValidBefore)
    {
        var certificate = new Certificate
        {
            Id = Guid.NewGuid(),
            ServiceUri = ServiceUri,
            PublicKey = caxOnlypublicKey.AsHex(),
            Properties = properties,
            NotValidAfter = notValidAfter,
            NotValidBefore = notValidBefore
        };
        certificate.Sign(this._CaPrivateKey);
        return certificate;
    }


}

