using NBitcoin.Secp256k1;
using CryptoToolkit;
namespace NGigGossip4Nostr;

[Serializable]
public class SignableObject
{
    public byte[] Signature { get; set; }

    public void Sign(ECPrivKey privateKey)
    {
        Signature = Crypto.SignObject(this, privateKey);
    }

    public bool Verify(ECXOnlyPubKey publicKey)
    {
        var signature = Signature;
        Signature = null;
        var result = Crypto.VerifyObject(this, signature, publicKey);
        Signature = signature;
        return result;
    }
}