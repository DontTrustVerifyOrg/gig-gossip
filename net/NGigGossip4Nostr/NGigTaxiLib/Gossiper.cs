using System;
using NGigGossip4Nostr;
using System.Text;
using CryptoToolkit;
using NBitcoin.Secp256k1;

namespace NGigTaxiLib;


public class Gossiper : GigGossipNode
{
    public Gossiper(ECPrivKey privKey, string[] nostrRelays, CertificationAuthority ca, int priceAmountForRouting,
        GigLNDWalletAPIClient.swaggerClient lndWalletClient, ISettlerSelector settlerSelector)
        : base(privKey, nostrRelays)
    {
        Init(
            priceAmountForRouting,
            TimeSpan.FromDays(7),
            "sha256", 2,
            TimeSpan.FromDays(1), TimeSpan.FromSeconds(10),
            lndWalletClient, settlerSelector);
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
