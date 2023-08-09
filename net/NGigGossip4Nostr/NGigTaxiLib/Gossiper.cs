using System;
using NGigGossip4Nostr;
using System.Text;
using CryptoToolkit;
namespace NGigTaxiLib;


public class Gossiper : GigGossipNode
{
    public Gossiper(CertificationAuthority ca, int priceAmountForRouting, GigLNDWalletAPIClient.swaggerClient lndWalletClient, GigGossipSettlerAPIClient.swaggerClient settlerClient)
        :base(Crypto.GeneratECPrivKey(), new[] { "ws://127.0.0.1:6969" })
    {

        var certificate = ca.IssueCertificate(
            this._privateKey.CreateXOnlyPubKey(),
            "is_ok", true,
            DateTime.Now.AddDays(7), DateTime.Now.AddDays(-7));

        Init(
            certificate,
            priceAmountForRouting,
            TimeSpan.FromDays(7),
            "sha256", 2,
            TimeSpan.FromDays(1), TimeSpan.FromSeconds(10),
            lndWalletClient, settlerClient);
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
