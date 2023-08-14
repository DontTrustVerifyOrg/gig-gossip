using System;

using NGigGossip4Nostr;
using NGigTaxiLib;
using System.Text;
using NBitcoin.Secp256k1;
using CryptoToolkit;

namespace GigWorkerTest;

public class GigWorker : Gossiper
{
    Uri mySettler;
    Certificate mycert;

    public GigWorker(ECPrivKey privKey, string[] nostrRelays)
         : base(privKey, nostrRelays)
    {
    }

    public async Task GenerateMyCert(Uri mySettler)
    {
        this.mySettler = mySettler;
        var token = await this.settlerToken(mySettler);
        var val = Convert.ToBase64String(Encoding.Default.GetBytes("ok"));
        await this.settlerClientSelector.GetSettlerClient(mySettler).GiveUserPropertyAsync(
            this.PublicKey, token,
            "drive", val,
            (DateTime.Now + TimeSpan.FromDays(1)).ToLongDateString()
             );

        var cert = await this.settlerClientSelector.GetSettlerClient(mySettler).IssueCertificateAsync(
            this.PublicKey, await this.settlerToken(mySettler), new List<string> { "drive" });
        mycert = Crypto.DeserializeObject<Certificate>(cert);
    }

    public override AcceptBroadcastResponse? AcceptBroadcast(RequestPayload signedRequestPayload)
    {
        return new AcceptBroadcastResponse()
        {
            Message = Encoding.Default.GetBytes($"mynameis={this.PublicKey}"),
            Fee = 4321,
            SettlerServiceUri = mySettler,
            MyCertificate = mycert
        };
    }
}