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
    public GridNode(CertificationAuthority ca, int priceAmountForRouting, Settler settler)
         : base( ca, priceAmountForRouting, settler)
    {
        nodeType = GridNodeType.Gossiper;
    }

    public void SetGridNodeType(GridNodeType nodeType)
    {
        this.nodeType = nodeType;
    }

    public event EventHandler OnBroadcastAccepted;

    public override AcceptBroadcastResponse? AcceptBroadcast(RequestPayload signedTopic)
    {
        if (nodeType == GridNodeType.GigWorker)
            return new AcceptBroadcastResponse(){ Message=Encoding.Default.GetBytes($"mynameis={this.Name}"), Fee=4321, SettlerCaName=...);
        else
        {
            var ret = base.AcceptBroadcast(signedTopic);
            OnBroadcastAccepted.Invoke(this, EventArgs.Empty);
            return ret;
        }
    }
}

