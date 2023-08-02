
using System;
using System.Text.Json;
using Grpc.Core;
using LNDClient;

var conf = new LND.NodesConfiguration();

conf.AddNodeConfiguration(
    new LND.MacaroonFile(@"/Users/pawel/work/locallnd/.lnd/data/chain/bitcoin/regtest/admin.macaroon"),
    @"/Users/pawel/work/locallnd/.lnd/tls.cert",
    @"localhost:10009",
    @"localhost:9735"
    );
conf.AddNodeConfiguration(
    new LND.MacaroonFile(@"/Users/pawel/work/locallnd/.lnd2/data/chain/bitcoin/regtest/admin.macaroon"),
    @"/Users/pawel/work/locallnd/.lnd2/tls.cert",
    @"localhost:11009",
    @"localhost:9734"
    );
conf.AddNodeConfiguration(
    new LND.MacaroonFile(@"/Users/pawel/work/locallnd/.lnd3/data/chain/bitcoin/regtest/admin.macaroon"),
    @"/Users/pawel/work/locallnd/.lnd3/tls.cert",
    @"localhost:11010",
    @"localhost:9736"
    );




for (int i = 1; i <= 3; i++)
    Console.WriteLine("lnd{" + i.ToString() + "}: Pubkey: {" + LND.GetNodeInfo(conf, i) + "}");

for (int i = 1; i <= 3; i++)
    Console.WriteLine("lnd{" + i.ToString() + "}: Balance: {" + JsonSerializer.Serialize(LND.GetWalletBalance(conf, i)) + "}");

var peersof2 = LND.ListPeers(conf, 2);
var nd1 = LND.GetNodeInfo(conf, 1);
var nd3 = LND.GetNodeInfo(conf, 3);

if (peersof2.Peers.Where((p) => p.PubKey == nd1.IdentityPubkey).Count()==0)
    LND.Connect(conf, 2, conf.ListenHost(1), nd1.IdentityPubkey);

if (peersof2.Peers.Where((p) => p.PubKey == nd3.IdentityPubkey).Count() == 0)
    LND.Connect(conf, 2, conf.ListenHost(3), nd3.IdentityPubkey);

var channels2 = LND.ListChannels(conf, 2);
if (channels2.Channels.Where((c) => c.RemotePubkey == nd1.IdentityPubkey).Count() == 0)
{
    var oc2s = LND.OpenChannel(conf, 2, nd1.IdentityPubkey, 100000);
    while (await oc2s.ResponseStream.MoveNext())
    {
        if (oc2s.ResponseStream.Current.ChanOpen != null)
            break;
        else
            Thread.Sleep(1);
    };
}


var preimage = LND.GenerateRandomPreimage();
var hash = LND.ComputePaymentHash(preimage);
var paymentReq1 = LND.AddHodlInvoice(conf, 1, 1000, "hello", hash);
var paymentReq2 = LND.AddHodlInvoice(conf, 1, 1000, "hello", hash);
var paymentReq3 = LND.AddHodlInvoice(conf, 1, 1000, "hello", hash);
Console.WriteLine(paymentReq1.PaymentRequest);
Console.WriteLine(LND.DecodeInvoice(conf, 2, paymentReq1.PaymentRequest));
Console.WriteLine(paymentReq2.PaymentRequest);
Console.WriteLine(LND.DecodeInvoice(conf, 2, paymentReq2.PaymentRequest));
Console.WriteLine(paymentReq3.PaymentRequest);
Console.WriteLine(LND.DecodeInvoice(conf, 2, paymentReq3.PaymentRequest));

var waiter4inv = LND.SubscribeSingleInvoice(conf, 1, hash);

for (int i = 1; i <= 1; i++)
{
    Console.WriteLine("lnd{" + i.ToString() + "}: State: {" + LND.LookupInvoiceV2(conf, i, hash) + "}");
}

var waiter = LND.SendPaymentV2(conf, 2, paymentReq1.PaymentRequest, 10);

while (await waiter4inv.ResponseStream.MoveNext())
{
    if (waiter4inv.ResponseStream.Current.State == Lnrpc.Invoice.Types.InvoiceState.Accepted)
        break;
    else
        Thread.Sleep(1);
};

LND.SettleInvoice(conf, 1, preimage);

while (LND.LookupInvoiceV2(conf, 1, hash).State != Lnrpc.Invoice.Types.InvoiceState.Settled)
    Thread.Sleep(1);


while (await waiter.ResponseStream.MoveNext())
{
    if (waiter.ResponseStream.Current.Status == Lnrpc.Payment.Types.PaymentStatus.Succeeded)
        break;
    else
        Thread.Sleep(1);
};

var paymentReqC = LND.AddInvoice(conf, 1, 1000, "hello");
Console.WriteLine(paymentReq2);
Console.WriteLine(LND.DecodeInvoice(conf, 2, paymentReqC.PaymentRequest));
Console.WriteLine(LND.SendPayment(conf, 2, paymentReqC.PaymentRequest));

var channels21 = LND.ListChannels(conf, 2);
foreach (var chanx in channels21.Channels)
    LND.CloseChannel(conf, 2, chanx.ChannelPoint.Split(':')[0]);



for (int i = 1; i <= 3; i++)
    Console.WriteLine("lnd{" + i.ToString() + "}: Balance: {" + LND.GetWalletBalance(conf, i) + "}");

Console.WriteLine("End!");

