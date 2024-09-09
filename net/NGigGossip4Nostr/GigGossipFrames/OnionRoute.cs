﻿using NBitcoin.Secp256k1;
using CryptoToolkit;


namespace GigGossipFrames;

/// <summary>
/// An onion route is used in the onion routing protocol to establish an anonymous communication channel. 
/// Each "onion" in the route is peeled back one at a time by each gig gossip node in the network. 
/// </summary>
public partial class OnionRoute 
{
    /// <summary>
    /// Peel off each layer of the onion route using a private key, revealing the next destination.
    /// </summary>
    /// <param name="privKey">The private key used to decrypt the onion layer.</param>
    /// <returns></returns>
    public string Peel(ECPrivKey privKey)
    {
        var layerData = Crypto.DecryptObject<OnionLayer>(Onion.ToArray(), privKey, null) ;
        Onion = layerData.Core;
        return layerData.PublicKey;
    }

    /// <summary>
    /// Grow the onion by adding another layer wrapped around the existing layers.
    /// </summary>
    /// <param name="otherPublicKey">The public key of the other party to add to the onion layer.</param>
    /// <param name="pubKey">The public key used to encrypt the onion layer.</param>
    /// <returns>A new OnionRoute object with added layer.</returns>
    public OnionRoute Grow(string otherPublicKey, ECXOnlyPubKey pubKey)
    {
        var newOnion = new OnionRoute()
        {
            Onion = Crypto.EncryptObject(new OnionLayer()
            {
                PublicKey = otherPublicKey,
                Core = Onion
            }, pubKey, null).AsByteString()
        };
        return newOnion;
    }

    /// <summary>
    /// Check if the onion route is empty. An empty onion route has no layers to peel back.
    /// </summary>
    /// <returns>True if the onion route is empty; otherwise, false.</returns>
    public bool IsEmpty()
    {
        return Onion.Length == 0;
    }

    /// <summary>
    /// Creates a deep copy of the current OnionRoute instance.
    /// </summary>
    /// <returns>A deep copy of the current OnionRoute instance.</returns>
    public OnionRoute DeepCopy()
    {
        return new OnionRoute()
        {
            Onion = this.Onion.ToArray().AsByteString()
        };
    }
}

