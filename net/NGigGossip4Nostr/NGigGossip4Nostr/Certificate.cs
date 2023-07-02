using System;
namespace NGigGossip4Nostr;

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using NBitcoin.Secp256k1;
using NNostr.Client;


[Serializable]
public class Certificate : SignableObject
{
    public string CaName { get; set; }
    public ECXOnlyPubKey PublicKey { get; set; }

    public string Name { get; set; }
    public object Value { get; set; }
    public DateTime NotValidAfter { get; set; }
    public DateTime NotValidBefore { get; set; }

    public virtual bool Verify()
    {
        if (NotValidAfter >= DateTime.Now && NotValidBefore <= DateTime.Now)
        {
            var ca = CertificationAuthority.GetCertificationAuthorityByName(CaName);
            if (ca != null)
            {
                if (!ca.IsRevoked(this))
                {
                    if (Verify(ca.CaXOnlyPublicKey))
                        return true;
                }
            }
        }
        return false;
    }
}

public class CertificationAuthority
{
    private static readonly Dictionary<string, CertificationAuthority> CA_BY_NAME = new Dictionary<string, CertificationAuthority>();

    public string CaName { get; set; }
    private ECPrivKey _CaPrivateKey { get; set; }
    public ECXOnlyPubKey CaXOnlyPublicKey { get; set; }

    public CertificationAuthority(string caName, ECPrivKey caPrivateKey)
    {
        CaName = caName;
        _CaPrivateKey = caPrivateKey;
        CaXOnlyPublicKey = caPrivateKey.CreateXOnlyPubKey();
        CA_BY_NAME[caName] = this;
    }

    public Certificate IssueCertificate(ECXOnlyPubKey caxOnlypublicKey, string name, object value, DateTime notValidAfter, DateTime notValidBefore)
    {
        var certificate = new Certificate
        {
            CaName = CaName,
            PublicKey = caxOnlypublicKey,
            Name = name,
            Value = value,
            NotValidAfter = notValidAfter,
            NotValidBefore = notValidBefore
        };
        certificate.Sign(this._CaPrivateKey);
        return certificate;
    }

    public bool IsRevoked(Certificate certificate)
    {
        return false;
    }

    public static CertificationAuthority GetCertificationAuthorityByName(string caName)
    {
        if (CA_BY_NAME.ContainsKey(caName))
            return CA_BY_NAME[caName];
        throw new ArgumentException("CA not found");
    }
}

public class Cert
{

    static Dictionary<string, CertificationAuthority> CA_BY_NAME = new Dictionary<string, CertificationAuthority>();

    public static CertificationAuthority CreateCertificationAuthority(string caName)
    {
        var privKey = Crypto.GeneratECPrivKey();
        var certificationAuthority = new CertificationAuthority(caName, privKey);
        return certificationAuthority;
    }

    public static CertificationAuthority GetCertificationAuthorityByName(string caName)
    {
        if (CA_BY_NAME.ContainsKey(caName))
        {
            return CA_BY_NAME[caName];
        }
        return null;
    }
}
