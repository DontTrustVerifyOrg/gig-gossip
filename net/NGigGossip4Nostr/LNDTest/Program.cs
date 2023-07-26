
using System;
using LNDClient;

var conf = new LND.NodesConfiguration();

conf.AddNodeConfiguration(
    @"/Users/pawel/work/locallnd/.lnd/data/chain/bitcoin/regtest/admin.macaroon",
    @"/Users/pawel/work/locallnd/.lnd/tls.cert",
    @"localhost:10009",
    @"localhost:9735"
    );
conf.AddNodeConfiguration(
    @"/Users/pawel/work/locallnd/.lnd2/data/chain/bitcoin/regtest/admin.macaroon",
    @"/Users/pawel/work/locallnd/.lnd2/tls.cert",
    @"localhost:11009",
    @"localhost:9734"
    );
conf.AddNodeConfiguration(
    @"/Users/pawel/work/locallnd/.lnd3/data/chain/bitcoin/regtest/admin.macaroon",
    @"/Users/pawel/work/locallnd/.lnd3/tls.cert",
    @"localhost:11010",
    @"localhost:9736"
    );



for (int i = 1; i <= 3; i++)
    Console.WriteLine("lnd{" + i.ToString() + "}: Pubkey: {" + LND.GetNodePubkey(conf, i) + "}");

for (int i = 1; i <= 3; i++)
    Console.WriteLine("lnd{" + i.ToString() + "}: Balance: {" + LND.GetWalletBalance(conf, i) + "}");

var peersof2 = LND.ListPeers(conf, 2);
if (!peersof2.ContainsKey(LND.GetNodePubkey(conf, 1)))
{
    LND.Connect(conf, 2, 1, LND.GetNodePubkey(conf, 1));
}
if (!peersof2.ContainsKey(LND.GetNodePubkey(conf, 3)))
    LND.Connect(conf, 2, 3, LND.GetNodePubkey(conf, 3));

var channels2 = LND.ListChannels(conf, 2);
if (!channels2.ContainsKey(LND.GetNodePubkey(conf, 1)))
{
    var tx = LND.OpenChannel(conf, 2, LND.GetNodePubkey(conf, 1), 100000);
    Console.WriteLine(tx);
    do
    {
        var pending2 = LND.PendingChannels(conf, 2);
        if (pending2["open"].ContainsKey(tx))
            Thread.Sleep(1);
        else
            break;
    } while (true);
}


var preimage = LND.GenerateRandomPreimage();
var hash = LND.ComputePaymentHash(preimage);
var paymentReq = LND.AddHodlInvoice(conf, 1, 1000, "hello", hash);

Console.WriteLine(paymentReq);
Console.WriteLine(LND.DecodeInvoice(conf, 2, paymentReq));

var waiter4inv = LND.SubscribeSingleInvoice(conf, 1, hash);

for (int i = 1; i <= 1; i++)
{
    Console.WriteLine("lnd{" + i.ToString() + "}: State: {" + LND.LookupInvoiceV2(conf, i, hash) + "}");
}

var waiter = LND.SendPaymentV2(conf, 2, paymentReq, 10);

{
    Task<bool> task = null;
    do
    {
        var st = await LND.AwaitForSubscribeSingleInvoiceReturn(waiter4inv, 0.1, task);
        if (st.Item1 != null)
            Console.WriteLine(st.Item1);
        else
            Console.WriteLine(".");
        if (st.Item1 == "Accepted")
            break;
        task = st.Item2;
    } while (true);
}

LND.SettleInvoice(conf, 1, preimage);
while (LND.LookupInvoiceV2(conf, 1, hash) != "Settled")
{
    Thread.Sleep(1);
}

{
    Task<bool> task = null;
    do
    {
        var st = await LND.AwaitForSendPaymentV2Return(waiter, 0.1, task);
        if (st.Item1 != null)
            Console.WriteLine(st.Item1);
        else
            Console.WriteLine(".");
        if (st.Item1 == "Succeeded")
            break;
        task = st.Item2;
    } while (true);
}

var paymentReq2 = LND.AddInvoice(conf, 1, 1000, "hello");
Console.WriteLine(paymentReq2);
Console.WriteLine(LND.DecodeInvoice(conf, 2, paymentReq2));
Console.WriteLine(LND.SendPayment(conf, 2, paymentReq2));

var channels21 = LND.ListChannels(conf, 2);
foreach (var chanx in channels21.Values)
{
    foreach (var tx in chanx.Keys)
        LND.CloseChannel(conf, 2, tx);
}

foreach (var chanx in channels21.Values)
{
    foreach (var tx in chanx.Keys)
        do
        {
            var pending2 = LND.PendingChannels(conf, 2);
            if (pending2["waitingclose"].ContainsKey(tx))
                Thread.Sleep(1);
            else
                break;
        } while (true);
}

for (int i = 1; i <= 3; i++)
    Console.WriteLine("lnd{" + i.ToString() + "}: Balance: {" + LND.GetWalletBalance(conf, i) + "}");

Console.WriteLine("End!");

