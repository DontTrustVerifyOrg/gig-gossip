using System;
using CryptoToolkit;

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using NBitcoin.Secp256k1;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace CryptoToolkit;

public interface ICertificationAuthorityAccessor
{
    public ECXOnlyPubKey GetPubKey(string CaName);
}



[Serializable]
public class Certificate : SignableObject
{
    public string CaName { get; set; }
    public SerializedECXOnlyPubKey PublicKey { get; set; }

    public string Name { get; set; }
    public object Value { get; set; }
    public DateTime NotValidAfter { get; set; }
    public DateTime NotValidBefore { get; set; }

    public bool VerifyCertificate(ICertificationAuthorityAccessor caAccessor)
    {
        if (NotValidAfter >= DateTime.Now && NotValidBefore <= DateTime.Now)
        {
            if (Verify(caAccessor.GetPubKey(this.CaName)))
                return true;
        }
        return false;
    }
}

public class CertificationAuthority
{
    public string CaName { get; set; }
    protected ECPrivKey _CaPrivateKey { get; set; }
    public ECXOnlyPubKey CaXOnlyPublicKey { get; set; }

    public CertificationAuthority(string caName, ECPrivKey caPrivateKey)
    {
        CaName = caName;
        _CaPrivateKey = caPrivateKey;
        CaXOnlyPublicKey = caPrivateKey.CreateXOnlyPubKey();
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

}

