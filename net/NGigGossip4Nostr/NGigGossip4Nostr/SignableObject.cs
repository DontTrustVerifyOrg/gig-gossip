using NBitcoin.Secp256k1;
namespace NGigGossip4Nostr;

public abstract class SignableObject
{
    public byte[] Signature { get; set; }

    public void Sign(ECPrivKey privateKey)
    {
        Signature = Crypto.SignObject(this, privateKey);
    }

    public bool Verify(ECXOnlyPubKey publicKey)
    {
        var signature = Signature;
        Signature = new byte[0];
        var result = Crypto.VerifyObject(this, signature, publicKey);
        Signature = signature;
        return result;
    }
}