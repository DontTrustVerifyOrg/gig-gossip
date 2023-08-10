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
        return new AcceptBroadcastResponse(){ Message=Encoding.Default.GetBytes($"mynameis={this.Name}"), Fee= 4321, SettlerCaName =settler.Name);
    }
}