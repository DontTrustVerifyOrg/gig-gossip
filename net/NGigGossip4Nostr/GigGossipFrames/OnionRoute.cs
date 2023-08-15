using NBitcoin.Secp256k1;
using CryptoToolkit;

namespace NGigGossip4Nostr;

[Serializable]
public class OnionLayer
{
    public string PublicKey { get; set; }
    public byte[] Core { get; set; }
}

[Serializable]
public class OnionRoute
{
    public byte[] Onion { get; set; } = new byte[0];

    public string Peel(ECPrivKey privKey)
    {
        var layerData = Crypto.DecryptObject<OnionLayer>(Onion, privKey, null) ;
        Onion = layerData.Core;
        return layerData.PublicKey;
    }

    public OnionRoute Grow(string otherPublicKey, ECXOnlyPubKey pubKey)
    {
        var newOnion = new OnionRoute();
        newOnion.Onion = Crypto.EncryptObject(new OnionLayer() { PublicKey = otherPublicKey, Core = Onion }, pubKey, null);
        return newOnion;
    }

    public bool IsEmpty()
    {
        return Onion.Length == 0;
    }

    public OnionRoute DeepCopy()
    {
        return new OnionRoute()
        {
            Onion = this.Onion.ToArray()
        };
    }
}

