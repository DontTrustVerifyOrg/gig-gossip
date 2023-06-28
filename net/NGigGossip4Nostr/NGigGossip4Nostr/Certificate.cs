using System;
namespace NGigGossip4Nostr;

using System;
using System.Collections.Generic;
using NBitcoin.Secp256k1;

public class Certificate
{
    public string CaName { get; set; }
    public ECXOnlyPubKey PublicKey { get; set; }
    public string Name { get; set; }
    public object Value { get; set; }
    public DateTime NotValidAfter { get; set; }
    public DateTime NotValidBefore { get; set; }
    public byte[] Signature { get; set; }

    public bool Verify()
    {
        if (NotValidAfter >= DateTime.Now && NotValidBefore <= DateTime.Now)
        {
            var ca = CertificationAuthority.GetCertificationAuthorityByName(CaName);
            if (ca != null)
            {
                if (!ca.IsRevoked(this))
                {
                    var obj = (CaName, PublicKey, Name, Value, NotValidAfter, NotValidBefore);
                    return Crypto.VerifyObject(obj, Signature, ca.CaXOnlyPublicKey);
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
    private ECPrivKey CaPrivateKey { get; set; }
    public ECXOnlyPubKey CaXOnlyPublicKey { get; set; }

    public CertificationAuthority(string caName, ECPrivKey caPrivateKey, ECXOnlyPubKey caPublicKey)
    {
        CaName = caName;
        CaPrivateKey = caPrivateKey;
        CaXOnlyPublicKey = caPublicKey;
        CA_BY_NAME[caName] = this;
    }

    public Certificate IssueCertificate(ECXOnlyPubKey caxOnlypublicKey, string name, object value, DateTime notValidAfter, DateTime notValidBefore)
    {
        var obj = (CaName, caxOnlypublicKey, name, value, notValidAfter, notValidBefore);
        var signature = Crypto.SignObject(obj, CaPrivateKey);
        return new Certificate { CaName = CaName, PublicKey = caxOnlypublicKey, Name = name, Value = value, NotValidAfter = notValidAfter, NotValidBefore = notValidBefore, Signature = signature };
    }

    public bool IsRevoked(Certificate certificate)
    {
        return false;
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

public class Cert
{

    static Dictionary<string, CertificationAuthority> CA_BY_NAME = new Dictionary<string, CertificationAuthority>();

    public static CertificationAuthority CreateCertificationAuthority(string caName)
    {
        var privKey = Crypto.GeneratECPrivKey();
        var certificationAuthority = new CertificationAuthority(caName, privKey, privKey.CreateXOnlyPubKey());
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
