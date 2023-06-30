using System.Text;
using NGigGossip4Nostr;
using NGigTaxiLib;

Console.WriteLine("start");


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

public class Customer : Gossiper
{
    public Customer(string name, CertificationAuthority ca, int priceAmountForRouting, Settler settler)
         : base(name, ca, priceAmountForRouting, settler)
    {
    }
}
