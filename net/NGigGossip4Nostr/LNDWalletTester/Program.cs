// See https://aka.ms/new-console-template for more information

using CryptoToolkit;
using NBitcoin.Secp256k1;
using LNDWallet;
using System.Text.Json;
using LNDClient;
using NBitcoin.RPC;
using Grpc.Core;

// CONFIG

var lndConf = new LND.NodesConfiguration();

var lndIdx1=lndConf.AddNodeConfiguration(
    new LND.MacaroonFile(@"/Users/pawel/work/locallnd/.lnd/data/chain/bitcoin/regtest/admin.macaroon"),
    @"/Users/pawel/work/locallnd/.lnd/tls.cert",
    @"localhost:10009",
    @"localhost:9735"
    );
var lndIdx2=lndConf.AddNodeConfiguration(
    new LND.MacaroonFile(@"/Users/pawel/work/locallnd/.lnd2/data/chain/bitcoin/regtest/admin.macaroon"),
    @"/Users/pawel/work/locallnd/.lnd2/tls.cert",
    @"localhost:11009",
    @"localhost:9734"
    );
var lndIdx3=lndConf.AddNodeConfiguration(
    new LND.MacaroonFile(@"/Users/pawel/work/locallnd/.lnd3/data/chain/bitcoin/regtest/admin.macaroon"),
    @"/Users/pawel/work/locallnd/.lnd3/tls.cert",
    @"localhost:11010",
    @"localhost:9736"
    );

var allLndIdxes = new List<int> { lndIdx1, lndIdx2, lndIdx3 };


var lndWalletDBConnectionString1 = "Data Source=lndwallets1.db";
var lndWalletDBConnectionString2 = "Data Source=lndwallets2.db";
var lndWalletDBConnectionString3 = "Data Source=lndwallets3.db";

var bitcoinNetwork = NBitcoin.Network.RegTest;
var bitcoinClient = new RPCClient("lnd:lightning", "127.0.0.1:18332", bitcoinNetwork);
var bitcoinWalletName = "testwallet";

RPCClient btcWallet = null;
try
{
    btcWallet = bitcoinClient.LoadWallet(bitcoinWalletName); ;
}
catch (RPCException exception)
{
    if (exception.RPCCode == RPCErrorCode.RPC_WALLET_ALREADY_LOADED)
        btcWallet = bitcoinClient.SetWalletContext(bitcoinWalletName);
}

//START


foreach (int i in allLndIdxes)
    Console.WriteLine("lnd{" + i.ToString() + "}: Pubkey: {" + LND.GetNodeInfo(lndConf, i) + "}");

foreach (int i in allLndIdxes)
    Console.WriteLine("lnd{" + i.ToString() + "}: Balance: {" + JsonSerializer.Serialize(LND.GetWalletBalance(lndConf, i)) + "}");

var peersof2 = LND.ListPeers(lndConf, lndIdx2);

Lnrpc.GetInfoResponse nd1, nd2, nd3;
while(true)
{
    nd1 = LND.GetNodeInfo(lndConf, lndIdx1);
    nd2 = LND.GetNodeInfo(lndConf, lndIdx2);
    nd3 = LND.GetNodeInfo(lndConf, lndIdx3);
    if (nd1.SyncedToChain && nd2.SyncedToChain && nd3.SyncedToChain)
        break;
    else
        Thread.Sleep(1000);
}

if (peersof2.Peers.Where((p) => p.PubKey == nd1.IdentityPubkey).Count() == 0)
    LND.Connect(lndConf, lndIdx2, lndConf.ListenHost(1), nd1.IdentityPubkey);

if (peersof2.Peers.Where((p) => p.PubKey == nd3.IdentityPubkey).Count() == 0)
    LND.Connect(lndConf, lndIdx2, lndConf.ListenHost(3), nd3.IdentityPubkey);


bool deleteDb = true;

var globalWallet1 = new LNDWalletManager(lndWalletDBConnectionString1, lndConf, lndIdx1, LND.GetNodeInfo(lndConf, lndIdx1), deleteDb: deleteDb);
var globalWallet2 = new LNDWalletManager(lndWalletDBConnectionString2, lndConf, lndIdx2, LND.GetNodeInfo(lndConf, lndIdx2), deleteDb : deleteDb);

var privkeyUser1FromNode1 = Context.Instance.CreateECPrivKey(Convert.FromHexString("7f4c11a9742721d66e40e321ca70b682c27f7422190c84a187525e69e6038369"));
var pubkeyUser1FromNode1 = privkeyUser1FromNode1.CreateXOnlyPubKey();
var myWalletUser1FromNode1 = globalWallet1.GetAccount(pubkeyUser1FromNode1);
if (myWalletUser1FromNode1 == null)
    myWalletUser1FromNode1 = globalWallet1.CreateAccount(pubkeyUser1FromNode1);

var privkeyUser1FromNode2 = Context.Instance.CreateECPrivKey(Convert.FromHexString("7f4c11a9742421366e40e321ca50b682c27f7422190c14a487525e69e6048326"));
var pubkeyUser1FromNode2 = privkeyUser1FromNode2.CreateXOnlyPubKey();
var myWalletUser1FromNode2= globalWallet2.GetAccount(pubkeyUser1FromNode2);
if(myWalletUser1FromNode2==null)
    myWalletUser1FromNode2=globalWallet2.CreateAccount(pubkeyUser1FromNode2);

var privkeyUser2FromNode2 = Context.Instance.CreateECPrivKey(Convert.FromHexString("7f4c11a9742421366e40e321ca50b682c27f7482190c14a487525e69e6048326"));
var pubkeyUser2FromNode2 = privkeyUser2FromNode2.CreateXOnlyPubKey();
var myWalletUser2FromNode2 = globalWallet2.GetAccount(pubkeyUser1FromNode2);
if (myWalletUser2FromNode2 == null)
    myWalletUser2FromNode2 = globalWallet2.CreateAccount(pubkeyUser1FromNode2);

ulong txfee = 100;

var ballanceOfUser1FromNode2 = myWalletUser1FromNode2.GetChannelFundingAmount(6);
if (ballanceOfUser1FromNode2 == 0)
{
    var myNewAddrForUser1FromNode2 = myWalletUser1FromNode2.NewAddress(txfee);
    Console.WriteLine(myNewAddrForUser1FromNode2);

    btcWallet.SendToAddress(NBitcoin.BitcoinAddress.Create(myNewAddrForUser1FromNode2, bitcoinNetwork), new NBitcoin.Money(10000000ul));

    btcWallet.Generate(10);

    do
    {
        if (myWalletUser1FromNode2.GetChannelFundingAmount(6) > 0)
            break;
        Thread.Sleep(1000);
    } while (true);

    ballanceOfUser1FromNode2 = myWalletUser1FromNode2.GetChannelFundingAmount(6);
}

//channel oppening
btcWallet.Generate(10);

var chanptFromNode2ToNode1 = globalWallet2.OpenChannel(nd1.IdentityPubkey, ballanceOfUser1FromNode2);
btcWallet.Generate(10);
while ((from channel in globalWallet2.ListChannels(true).Channels where channel.ChannelPoint==chanptFromNode2ToNode1 select channel).Count()==0)
{
    Thread.Sleep(1000);
}

// PAY 1->2
{
    var preimage = LND.GenerateRandomPreimage();
    var hash = LND.ComputePaymentHash(preimage);
    var paymentReq = myWalletUser1FromNode1.AddHodlInvoice(1000, "hello", hash, txfee);

    Console.WriteLine(paymentReq);
    Console.WriteLine(LND.DecodeInvoice(lndConf, lndIdx2, paymentReq.PaymentRequest));

    var payment12 = myWalletUser1FromNode2.SendPayment(paymentReq.PaymentRequest, 600, txfee);

    await myWalletUser1FromNode1.WaitForInvoiceCondition(hash.AsHex(),(inv) => inv.State == Lnrpc.Invoice.Types.InvoiceState.Accepted);

    var settlement11 = myWalletUser1FromNode1.SettleInvoice(preimage);

    await settlement11.WaitForCondition((inv) => inv.State == Lnrpc.Invoice.Types.InvoiceState.Settled, CancellationToken.None);

    await payment12.WaitForCondition((pay) => pay.Status == Lnrpc.Payment.Types.PaymentStatus.Succeeded, CancellationToken.None);

}

// PAY 2->2
{
    var preimage = LND.GenerateRandomPreimage();
    var hash = LND.ComputePaymentHash(preimage);
    var paymentReq = myWalletUser2FromNode2.AddHodlInvoice(1000, "hello", hash, txfee);

    Console.WriteLine(paymentReq);
    Console.WriteLine(LND.DecodeInvoice(lndConf, lndIdx2, paymentReq.PaymentRequest));

    var payment12 = myWalletUser1FromNode2.SendPayment(paymentReq.PaymentRequest, 600, txfee);

    await myWalletUser2FromNode2.WaitForInvoiceCondition(hash.AsHex(),(inv) => inv.State == Lnrpc.Invoice.Types.InvoiceState.Accepted);

    var settlement22 = myWalletUser2FromNode2.SettleInvoice(preimage);

    await settlement22.WaitForCondition((inv) => inv.State == Lnrpc.Invoice.Types.InvoiceState.Settled, CancellationToken.None);

    await payment12.WaitForCondition((pay) => pay.Status == Lnrpc.Payment.Types.PaymentStatus.Succeeded, CancellationToken.None);

}
// CLOSE CHANNEL

var btcReturnAddress = btcWallet.GetNewAddress().ToString();

var channelStatusStream = globalWallet2.CloseChannel(chanptFromNode2ToNode1);
btcWallet.Generate(10);

while (await channelStatusStream.ResponseStream.MoveNext())
{
    var cur = channelStatusStream.ResponseStream.Current;
    if (cur.UpdateCase== Lnrpc.CloseStatusUpdate.UpdateOneofCase.ChanClose)
        break;
    else
        Thread.Sleep(1);
};

