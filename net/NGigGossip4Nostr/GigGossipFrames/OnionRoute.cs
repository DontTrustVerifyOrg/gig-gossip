using NBitcoin.Secp256k1;

namespace GigGossip;

/// <summary>
/// An onion route is used in the onion routing protocol to establish an anonymous communication channel. 
/// Each "onion" in the route is peeled back one at a time by each gig gossip node in the network. 
/// </summary>
public partial class Onion
{

    public static Onion GetEmpty()
    {
        return new Onion { EncryptedOnionLayer = new EncryptedData { Value = new byte[] { }.AsByteString() } };
    }

    /// <summary>
    /// Peel off each layer of the onion route using a private key, revealing the next destination.
    /// </summary>
    /// <param name="privKey">The private key used to decrypt the onion layer.</param>
    /// <returns></returns>
    public string Peel(ECPrivKey privKey)
    {
        var layerData = EncryptedOnionLayer.Decrypt<OnionLayer>(privKey);
        EncryptedOnionLayer = layerData.EncryptedOnionLayer;
        return layerData.PublicKey.AsHex();
    }

    /// <summary>
    /// Grow the onion by adding another layer wrapped around the existing layers.
    /// </summary>
    /// <param name="otherPublicKey">The public key of the other party to add to the onion layer.</param>
    /// <param name="pubKey">The public key used to encrypt the onion layer.</param>
    /// <returns>A new OnionRoute object with added layer.</returns>
    public Onion Grow(string otherPublicKey, ECXOnlyPubKey pubKey)
    {
        var newOnion = new Onion()
        {
            EncryptedOnionLayer = new OnionLayer()
            {
                PublicKey = otherPublicKey.AsPublicKey(),
                EncryptedOnionLayer = this.EncryptedOnionLayer.Clone(),
            }.Encrypt(pubKey),
        };
        return newOnion;
    }

    /// <summary>
    /// Check if the onion route is empty. An empty onion route has no layers to peel back.
    /// </summary>
    /// <returns>True if the onion route is empty; otherwise, false.</returns>
    public bool IsEmpty()
    {
        return this. EncryptedOnionLayer.Value.Length == 0;
    }

}

