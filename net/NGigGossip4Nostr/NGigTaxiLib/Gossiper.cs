using System;
using NGigGossip4Nostr;
using System.Text;
namespace NGigTaxiLib;


public class Gossiper : GigGossipNode
{
    public Gossiper(string name, CertificationAuthority ca, int priceAmountForRouting, Settler settler):base(name)
    {
        var privateKey = Crypto.GeneratECPrivKey();
        var paymentChannel = new PaymentChannel();

        var certificate = ca.IssueCertificate(
            privateKey.CreateXOnlyPubKey(),
            "is_ok", true,
            DateTime.Now.AddDays(7), DateTime.Now.AddDays(-7));

        Init(
            certificate,
            privateKey,
            paymentChannel,
            priceAmountForRouting,
            TimeSpan.FromDays(7),
            "sha256", 1,
            TimeSpan.FromDays(1), TimeSpan.FromSeconds(10),
            settler);
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
