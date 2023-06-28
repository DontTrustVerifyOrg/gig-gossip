using NBitcoin.Secp256k1;
namespace NGigGossip4Nostr;

public class OnionLayer
{
    public string PeerName { get; set; }

    public OnionLayer(string peerName)
    {
        PeerName = peerName;
    }
}

public class OnionRoute
{
    private byte[] _onion;

    public OnionRoute()
    {
        _onion = new byte[0];
    }

    public OnionLayer Peel(ECPrivKey privKey, ECXOnlyPubKey pubKey)
    {
        var layerData = (object[])Crypto.DecryptObject(_onion, privKey, pubKey);
        var layer = (OnionLayer)layerData[0];
        _onion = (byte[])layerData[1];
        return layer;
    }

    public OnionRoute Grow(OnionLayer layer, ECPrivKey privKey, ECXOnlyPubKey pubKey)
    {
        var newOnion = new OnionRoute();
        newOnion._onion = Crypto.EncryptObject(new object[] { layer, _onion },privKey, pubKey);
        return newOnion;
    }

    public bool IsEmpty()
    {
        return _onion.Length == 0;
    }
}

