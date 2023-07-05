using System;
using NBitcoin.Protocol;
using NGeoHash;
using NGigGossip4Nostr;
using NGigTaxiLib;

namespace GigWorkerTest;

public class Customer : Gossiper
{
    public Customer(string name, CertificationAuthority ca, int priceAmountForRouting, Settler settler)
         : base(name, ca, priceAmountForRouting, settler)
    {
    }

    Guid topicId;

    public void Go()
    {
        var fromGh = GeoHash.Encode(latitude: 42.6, longitude: -5.6, numberOfChars: 7);
        var toGh = GeoHash.Encode(latitude: 42.5, longitude: -5.6, numberOfChars: 7);
        topicId = Guid.NewGuid();
        var topic = new RequestPayload()
        {
            PayloadId = topicId,
            Topic = new TaxiTopic()
            {
                FromGeohash = fromGh,
                ToGeohash = toGh,
                PickupAfter = DateTime.Now,
                DropoffBefore = DateTime.Now.AddMinutes(20)
            },
            SenderCertificate=this.certificate
        };
        topic.Sign(this._privateKey);
        this.Broadcast(topic);
    }


}
