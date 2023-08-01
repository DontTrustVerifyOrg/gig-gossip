// See https://aka.ms/new-console-template for more information
using Microsoft.Data.Sqlite;

using CryptoToolkit;
using NBitcoin.Secp256k1;
using LNDWallet;
using System.Text.Json;
using LNDClient;
using NBitcoin.RPC;
using Grpc.Core;
using LITClient;

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

var litConf = new LIT.NodesConfiguration();
var litIdx1=litConf.AddNodeConfiguration(
    @"/Users/pawel/work/locallnd/.lit/regtest/lit.macaroon",
    @"/Users/pawel/work/locallnd/.lit/tls.cert",
    @"localhost:8443"
    );
var litIdx2 = litConf.AddNodeConfiguration(
    @"/Users/pawel/work/locallnd/.lit2/regtest/lit.macaroon",
    @"/Users/pawel/work/locallnd/.lit2/tls.cert",
    @"localhost:8444"
    );
var litIdx3 = litConf.AddNodeConfiguration(
    @"/Users/pawel/work/locallnd/.lit3/regtest/lit.macaroon",
    @"/Users/pawel/work/locallnd/.lit3/tls.cert",
    @"localhost:8445"
    );

var lndWalletDBConnectionString1 = "Data Source=lndwallets1.db";
var lndWalletDBConnectionString2 = "Data Source=lndwallets2.db";
var lndWalletDBConnectionString3 = "Data Source=lndwallets3.db";

var bitcoinClient = new RPCClient("lnd:lightning", "127.0.0.1:18332", NBitcoin.Network.RegTest);

var btcWalletName = "testwallet";


//START


foreach (int i in allLndIdxes)
    Console.WriteLine("lnd{" + i.ToString() + "}: Pubkey: {" + LND.GetNodeInfo(lndConf, i) + "}");

foreach (int i in allLndIdxes)
    Console.WriteLine("lnd{" + i.ToString() + "}: Balance: {" + JsonSerializer.Serialize(LND.GetWalletBalance(lndConf, i)) + "}");

var peersof2 = LND.ListPeers(lndConf, lndIdx2);
var nd1 = LND.GetNodeInfo(lndConf, lndIdx1);
var nd3 = LND.GetNodeInfo(lndConf, lndIdx3);

if (peersof2.Peers.Where((p) => p.PubKey == nd1.IdentityPubkey).Count() == 0)
    LND.Connect(lndConf, lndIdx2, lndConf.ListenHost(1), nd1.IdentityPubkey);

if (peersof2.Peers.Where((p) => p.PubKey == nd3.IdentityPubkey).Count() == 0)
    LND.Connect(lndConf, lndIdx2, lndConf.ListenHost(3), nd3.IdentityPubkey);


bool deleteDb = false;

var globalWallet1 = new LNDWalletManager(lndWalletDBConnectionString1, lndConf, lndIdx1, litConf, litIdx1, LND.GetNodeInfo(lndConf, lndIdx1), deleteDb: deleteDb);
var globalWallet2 = new LNDWalletManager(lndWalletDBConnectionString2, lndConf, lndIdx2, litConf, litIdx2, LND.GetNodeInfo(lndConf, lndIdx2), deleteDb : deleteDb);

var privkeyUser1FromNode1 = Context.Instance.CreateECPrivKey(Convert.FromHexString("7f4c11a9742721d66e40e321ca50b682c27f7422190c84a187525e69e6038369"));
var pubkeyUser1FromNode1 = privkeyUser1FromNode1.CreateXOnlyPubKey();
var myWalletUser1FromNode1 = globalWallet1.GetAccount(pubkeyUser1FromNode1);
if (myWalletUser1FromNode1 == null)
    myWalletUser1FromNode1 = globalWallet1.CreateAccount(pubkeyUser1FromNode1, 1000000);

var privkeyUser1FromNode2 = Context.Instance.CreateECPrivKey(Convert.FromHexString("7f4c11a9742721366e40e321ca50b682c27f7422190c14a487525e69e6048326"));
var pubkeyUser1FromNode2 = privkeyUser1FromNode2.CreateXOnlyPubKey();
var myWalletUser1FromNode2= globalWallet2.GetAccount(pubkeyUser1FromNode2);
if(myWalletUser1FromNode2==null)
    myWalletUser1FromNode2=globalWallet2.CreateAccount(pubkeyUser1FromNode2, 1000000);


var ballanceOfUser1FromNode2 = globalWallet2.GetAccountOnChainBalance(pubkeyUser1FromNode2.AsHex(),6);
if (ballanceOfUser1FromNode2 == 0)
{
    var myNewAddrForUser1FromNode2 = globalWallet2.NewAddress(pubkeyUser1FromNode2.AsHex());
    Console.WriteLine(myNewAddrForUser1FromNode2);
    do
    {
        if (globalWallet2.GetAccountOnChainBalance(pubkeyUser1FromNode2.AsHex(), 6)>0)
            break;
        Thread.Sleep(1000);
    } while (true);

    ballanceOfUser1FromNode2 = globalWallet2.GetAccountOnChainBalance(pubkeyUser1FromNode2.AsHex(), 6);
}

//channel oppening
var chanptFromNode2ToNode1 = globalWallet2.OpenChannel(nd1.IdentityPubkey, 100000);
while((from channel in globalWallet2.ListChannels(true).Channels where channel.ChannelPoint==chanptFromNode2ToNode1 select channel).Count()==0)
{
    Thread.Sleep(1000);
}

var paymentReq1 = myWalletUser1FromNode1.AddInvoice(1000, "hello");


var preimage = LND.GenerateRandomPreimage();
var hash = LND.ComputePaymentHash(preimage);
var paymentReq = myWalletUser1FromNode1.AddHodlInvoice(1000, "hello", hash);

Console.WriteLine(paymentReq.PaymentRequest);
Console.WriteLine(LND.DecodeInvoice(lndConf, lndIdx2, paymentReq.PaymentRequest));

var invoiceStatusStream = LND.SubscribeSingleInvoice(lndConf, 1, hash);

var paymentStatusStream = myWalletUser1FromNode2.SendPayment(paymentReq.PaymentRequest, 600);

while (await invoiceStatusStream.ResponseStream.MoveNext())
{
    if (invoiceStatusStream.ResponseStream.Current.State == Lnrpc.Invoice.Types.InvoiceState.Accepted)
        break;
    else
        Thread.Sleep(1);
};

LND.SettleInvoice(lndConf, lndIdx1, preimage);

while (LND.LookupInvoiceV2(lndConf, 1, hash).State != Lnrpc.Invoice.Types.InvoiceState.Settled)
    Thread.Sleep(1);

while (await paymentStatusStream.ResponseStream.MoveNext())
{
    if (paymentStatusStream.ResponseStream.Current.Status == Lnrpc.Payment.Types.PaymentStatus.Succeeded)
        break;
    else
        Thread.Sleep(1);
};


RPCClient btcWallet = null;
try
{
    btcWallet = bitcoinClient.LoadWallet(btcWalletName); ;
}
catch (RPCException exception)
{
    if (exception.RPCCode == RPCErrorCode.RPC_WALLET_ALREADY_LOADED)
        btcWallet = bitcoinClient.SetWalletContext(btcWalletName);
}
var btcReturnAddress = btcWallet.GetNewAddress().ToString();

var channelStatusStream = globalWallet2.CloseChannel(chanptFromNode2ToNode1, btcReturnAddress);

while (await channelStatusStream.ResponseStream.MoveNext())
{
    if (channelStatusStream.ResponseStream.Current.ChanClose.Success)
        break;
    else
        Thread.Sleep(1);
};

