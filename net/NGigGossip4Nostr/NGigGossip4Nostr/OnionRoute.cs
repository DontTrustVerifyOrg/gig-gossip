using NBitcoin.Secp256k1;
using NNostr.Client;

namespace NGigGossip4Nostr;

[Serializable]
public class OnionLayer
{
    public string PeerName { get; set; }
    public OnionLayer(string peerName)
    {
        PeerName = peerName;
    }
}

[Serializable]
public class OnionRoute
{
    private byte[] _onion;

    public OnionRoute()
    {
        _onion = new byte[0];
    }

    public OnionLayer Peel(ECPrivKey privKey)
    {
        var layerData = (object[])Crypto.DecryptObject(_onion, privKey, null) ;
        var layer = (OnionLayer)layerData[0];
        _onion = (byte[])layerData[1];
        return layer;
    }

    public OnionRoute Grow(OnionLayer layer, ECXOnlyPubKey pubKey)
    {
        var newOnion = new OnionRoute();
        newOnion._onion = Crypto.EncryptObject(new object[] { layer, _onion }, pubKey, null);
        return newOnion;
    }

    public bool IsEmpty()
    {
        return _onion.Length == 0;
    }

    public OnionRoute DeepCopy()
    {
        return new OnionRoute()
        {
            _onion = this._onion.ToArray()
        };
    }
}

