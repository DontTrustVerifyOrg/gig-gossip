using System;

using NGigGossip4Nostr;
using NGigTaxiLib;
using System.Text;
namespace GigWorkerTest;

public class GigWorker : Gossiper
{
    public GigWorker(CertificationAuthority ca, int priceAmountForRouting, Settler settler)
         : base(ca, priceAmountForRouting, settler)
    {
    }

    public override AcceptBroadcastResponse? AcceptBroadcast(RequestPayload signedTopic)
    {
        return new AcceptBroadcastResponse(){
            Message=Encoding.Default.GetBytes($"mynameis={this.PublicKey}"),
            Fee= 4321,
            SettlerServiceUri =settler.Name,
            MyCertificate = ...
            );
    }
}