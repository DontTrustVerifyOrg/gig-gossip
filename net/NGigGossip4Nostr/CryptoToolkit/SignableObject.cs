using NBitcoin.Secp256k1;
using CryptoToolkit;
namespace CryptoToolkit;

[Serializable]
public class SignableObject
{
    public byte[] Signature { get; set; }

    protected void Sign(ECPrivKey privateKey)
    {
        Signature = Crypto.SignObject(this, privateKey);
    }

    protected bool Verify(ECXOnlyPubKey publicKey)
    {
        var signature = Signature;
        Signature = null;
        var result = Crypto.VerifyObject(this, signature, publicKey);
        Signature = signature;
        return result;
    }
}