using System;
using NGigGossip4Nostr;
using System.Diagnostics;
using System.Text;
using NGigTaxiLib;
using CryptoToolkit;

namespace GigWorkerTest;

public class ComplexTest
{
    public ComplexTest()
    {
    }

    static int[] GRID_SHAPE = new int[] { 3, 3 };
    static int NUM_MESSAGES = 5;

    public void Run()
    {

        var ca = Cert.CreateCertificationAuthority("CA");
        var settlerPrivKey = Crypto.GeneratECPrivKey();
        var setter_certificate = ca.IssueCertificate(settlerPrivKey.CreateXOnlyPubKey(), "is_ok", true, DateTime.Now.AddDays(7), DateTime.Now.AddDays(-7));
        var settler = new Settler("ST", setter_certificate, settlerPrivKey, 12);


        var things = new Dictionary<string, GridNode>();

        var GRID_SHAPE_ITER = from x in GRID_SHAPE select Enumerable.Range(0, x);

        var nod_name_f = (IEnumerable<int> nod_idx) => "GridNode<" + string.Join(",", nod_idx.Select(i => i.ToString()).ToList()) + ">";

        foreach (var nod_idx in GRID_SHAPE_ITER.MultiCartesian())
        {
            var node_name = nod_name_f(nod_idx);
            things[node_name] = new GridNode(ca, 1, settler);
            things[node_name].OnNewResponse += GridNode_OnNewResponse;
            things[node_name].OnResponseReady += GridNode_OnResponseReady;
            things[node_name].OnBroadcastAccepted += ComplexTest_OnBroadcastAccepted;
        }

        var already = new HashSet<string>();
        foreach (var nod_idx in GRID_SHAPE_ITER.MultiCartesian())
        {
            var node_name = nod_name_f(nod_idx);
            for(int k=0; k<nod_idx.Length;k++)
            {
                var nod1_idx = nod_idx.Select((x, i) => i == k ? (x + 1) % GRID_SHAPE[k] : x);
                var node_name_1 = nod_name_f(nod1_idx);
                if (already.Contains(node_name + ":" + node_name_1))
                    continue;
                if (already.Contains(node_name_1 + ":" + node_name))
                    continue;

                things[node_name].ConnectTo(things[node_name_1]);
                already.Add(node_name + ":" + node_name_1);
                already.Add(node_name_1 + ":" + node_name);

                Console.WriteLine(node_name + "<->" + node_name_1);
            }
        }

        var thingsList = things.Values.ToArray();


        var rnd = new Random();
        var customers = new List<GridNode>();
        for (int i = 0; i < NUM_MESSAGES; i++)
        {
            int startIdx = rnd.Next(0, thingsList.Length);
            int endIdx = rnd.Next(0, thingsList.Length);

            Console.WriteLine($"{thingsList[startIdx].PublicKey} ->>> {thingsList[endIdx].PublicKey}");

            thingsList[startIdx].SetGridNodeType(GridNodeType.Customer);
            customers.Add(thingsList[startIdx]);
            thingsList[endIdx].SetGridNodeType(GridNodeType.GigWorker);
        }

        foreach (var nod_idx in GRID_SHAPE_ITER.MultiCartesian())
        {
            var node_name = nod_name_f(nod_idx);
            things[node_name].Start();
        }

        foreach(var customer in customers)
        {
            customer.Go();
        }

        while (true)
        {
            Thread.Sleep(1000);
        }

    }

    private void GridNode_OnNewResponse(object? sender, ResponseEventArgs e)
    {
        (sender as GigGossipNode).AcceptResponse(e.payload, e.network_invoice);
    }

    private void ComplexTest_OnBroadcastAccepted(object? sender, EventArgs e)
    {
    }

    private void GridNode_OnResponseReady(object? sender, ResponseEventArgs e)
    {
        var message = (byte[])Crypto.SymmetricDecrypt(e.network_invoice.Preimage, e.payload.EncryptedReplyMessage);
        Trace.TraceInformation(Encoding.Default.GetString(message));
    }
}

