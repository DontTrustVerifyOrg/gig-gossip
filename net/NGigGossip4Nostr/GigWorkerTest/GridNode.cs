using System;
using NGigGossip4Nostr;
using NGigTaxiLib;
using System.Text;

namespace GigWorkerTest;

public enum GridNodeType
{
    Gossiper,
    Customer,
    GigWorker,
}

public class GridNode : Customer
{
    GridNodeType nodeType;
    public GridNode(string name, CertificationAuthority ca, int priceAmountForRouting, Settler settler)
         : base(name, ca, priceAmountForRouting, settler)
    {
        nodeType = GridNodeType.Gossiper;
    }

    public void SetGridNodeType(GridNodeType nodeType)
    {
        this.nodeType = nodeType;
    }

    public event EventHandler OnBroadcastAccepted;

    public override Tuple<byte[]?, int> AcceptBroadcast(RequestPayload signedTopic)
    {
        if (nodeType == GridNodeType.GigWorker)
            return new Tuple<byte[]?, int>(Encoding.Default.GetBytes($"mynameis={this.Name}"), 4321);
        else
        {
            var ret = base.AcceptBroadcast(signedTopic);
            OnBroadcastAccepted.Invoke(this, EventArgs.Empty);
            return ret;
        }
    }
}

