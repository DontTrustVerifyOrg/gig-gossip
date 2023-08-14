using System;
using NGigGossip4Nostr;
using System.Text;
using CryptoToolkit;
using NBitcoin.Secp256k1;

namespace NGigTaxiLib;


public class Gossiper : GigGossipNode
{
    public Gossiper(ECPrivKey privKey, string[] nostrRelays)
        : base(privKey, nostrRelays)
    {
    }

    public override bool AcceptTopic(AbstractTopic topic)
    {
        if (topic is TaxiTopic)
        {
            var taxiTopic = (TaxiTopic)topic;

            return taxiTopic.FromGeohash.Length >= 7 &&
                   taxiTopic.ToGeohash.Length >= 7 &&
                   taxiTopic.DropoffBefore >= DateTime.Now;
        }

        return false;
    }
}
