using System;

using NGigGossip4Nostr;
using NGigTaxiLib;
using System.Text;
namespace GigWorkerTest;

public class GigWorker : Gossiper
{
    public GigWorker(string name, CertificationAuthority ca, int priceAmountForRouting, Settler settler)
         : base(name, ca, priceAmountForRouting, settler)
    {
    }

    public override Tuple<byte[]?, int> AcceptBroadcast(RequestPayload signedTopic)
    {
        return new Tuple<byte[]?, int>(Encoding.Default.GetBytes($"mynameis={this.name}"), 4321);
    }
}