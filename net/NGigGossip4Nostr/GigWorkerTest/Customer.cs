using System;
using System.Text;
using CryptoToolkit;
using NBitcoin.Protocol;
using NBitcoin.Secp256k1;
using NGeoHash;
using NGigGossip4Nostr;
using NGigTaxiLib;

namespace GigWorkerTest;

public class Customer : Gossiper
{
    Uri mySettler;
    Certificate mycert;

    public Customer(ECPrivKey privKey, string[] nostrRelays)
         : base(privKey, nostrRelays)
    {
    }

    public async Task GenerateMyCert(Uri mySettler)
    {
        this.mySettler = mySettler;
        var token = await this.SettlerToken(mySettler);
        await this.SettlerSelector.GetSettlerClient(mySettler).GiveUserPropertyAsync(
            this.PublicKey, token,
            "ride", Convert.ToBase64String(Encoding.Default.GetBytes("ok")),
            (DateTime.Now + TimeSpan.FromDays(1)).ToLongDateString()
             );

        var cert = await this.SettlerSelector.GetSettlerClient(mySettler).IssueCertificateAsync(
            this.PublicKey, await this.SettlerToken(mySettler), new List<string> { "ride" });
        mycert = Crypto.DeserializeObject<Certificate>(cert);
    }


    public Guid topicId;

    public void Go()
    {
        var fromGh = GeoHash.Encode(latitude: 42.6, longitude: -5.6, numberOfChars: 7);
        var toGh = GeoHash.Encode(latitude: 42.5, longitude: -5.6, numberOfChars: 7);
        topicId = Guid.NewGuid();
        var topic = new RequestPayload()
        {
            PayloadId = topicId,
            Topic = Crypto.SerializeObject(new TaxiTopic()
            {
                FromGeohash = fromGh,
                ToGeohash = toGh,
                PickupAfter = DateTime.Now,
                DropoffBefore = DateTime.Now.AddMinutes(20)
            }),
            SenderCertificate=this.mycert
        };
        topic.Sign(this._privateKey);
        this.Broadcast(topic);
    }


}
