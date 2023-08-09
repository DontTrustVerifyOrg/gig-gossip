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

// Configuration of LND node 1
var lndIdx1=lndConf.AddNodeConfiguration(
    new LND.MacaroonFile(@"/Users/pawel/work/locallnd/.lnd/data/chain/bitcoin/regtest/admin.macaroon"),
    @"/Users/pawel/work/locallnd/.lnd/tls.cert",
    @"localhost:10009",
    @"localhost:9735"
    );

// Configuration of LND node 2
var lndIdx2 =lndConf.AddNodeConfiguration(
    new LND.MacaroonFile(@"/Users/pawel/work/locallnd/.lnd2/data/chain/bitcoin/regtest/admin.macaroon"),
    @"/Users/pawel/work/locallnd/.lnd2/tls.cert",
    @"localhost:11009",
    @"localhost:9734"
    );

// Configuration of LND node 3
var lndIdx3 =lndConf.AddNodeConfiguration(
    new LND.MacaroonFile(@"/Users/pawel/work/locallnd/.lnd3/data/chain/bitcoin/regtest/admin.macaroon"),
    @"/Users/pawel/work/locallnd/.lnd3/tls.cert",
    @"localhost:11010",
    @"localhost:9736"
    );

var allLndIdxes = new List<int> { lndIdx1, lndIdx2, lndIdx3 };

// Wallet DBs for Nodes
bool deleteDb = true; // should we delete all dbs at start (e.g. schema change)
long txfee = 100;
long maxPaymentFee = 1000;

var lndWalletDBConnectionString1 = "Data Source=lndwallets1.db";
var lndWalletDBConnectionString2 = "Data Source=lndwallets2.db";
var lndWalletDBConnectionString3 = "Data Source=lndwallets3.db";

// Bitcoin Network access client
var bitcoinNetwork = NBitcoin.Network.RegTest;
var bitcoinClient = new RPCClient("lnd:lightning", "127.0.0.1:18332", bitcoinNetwork);
var bitcoinWalletName = "testwallet";

// load bitcoin node wallet
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

btcWallet.Generate(10); // generate some blocks

//wait for nodes synced
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

// Write node infos
foreach (int i in allLndIdxes)
    Console.WriteLine("lnd{" + i.ToString() + "}: Pubkey: {" + LND.GetNodeInfo(lndConf, i) + "}");

//who are peeros of lnd node2
var peersof2 = LND.ListPeers(lndConf, lndIdx2);

//connect peer 2 to peer 1 if not connected
if (peersof2.Peers.Where((p) => p.PubKey == nd1.IdentityPubkey).Count() == 0)
    LND.Connect(lndConf, lndIdx2, lndConf.ListenHost(1), nd1.IdentityPubkey);

//connect peer 2 to peer 3 if not connected
if (peersof2.Peers.Where((p) => p.PubKey == nd3.IdentityPubkey).Count() == 0)
    LND.Connect(lndConf, lndIdx2, lndConf.ListenHost(3), nd3.IdentityPubkey);

//create wallets for all 3 lnd nodes
var wallet1 = new LNDWalletManager(lndWalletDBConnectionString1, lndConf, lndIdx1, nd1, deleteDb: deleteDb);
wallet1.Start();
var wallet2 = new LNDWalletManager(lndWalletDBConnectionString2, lndConf, lndIdx2, nd2, deleteDb: deleteDb);
wallet2.Start();
var wallet3 = new LNDWalletManager(lndWalletDBConnectionString3, lndConf, lndIdx3, nd3, deleteDb: deleteDb);
wallet3.Start();

//setup user accounts
var privkeyUser1FromNode1 = Context.Instance.CreateECPrivKey(Convert.FromHexString("7f4c11a9742721d66e40e321ca70b682c27f7422190c84a187525e69e6038369"));
var pubkeyUser1FromNode1 = privkeyUser1FromNode1.CreateXOnlyPubKey();
var myAccountUser1FromNode1 = wallet1.GetAccount(pubkeyUser1FromNode1);

var privkeyUser1FromNode2 = Context.Instance.CreateECPrivKey(Convert.FromHexString("7f4c11a9742421366e40e321ca50b682c27f7422190c14a487525e69e6048326"));
var pubkeyUser1FromNode2 = privkeyUser1FromNode2.CreateXOnlyPubKey();
var myAccountUser1FromNode2= wallet2.GetAccount(pubkeyUser1FromNode2);

var privkeyUser2FromNode2 = Context.Instance.CreateECPrivKey(Convert.FromHexString("7f4c11a9742421366e40e321ca50b682c27f7482190c14a487525e69e6048326"));
var pubkeyUser2FromNode2 = privkeyUser2FromNode2.CreateXOnlyPubKey();
var myAccountUser2FromNode2 = wallet2.GetAccount(pubkeyUser1FromNode2);


var ballanceOfUser1FromNode2 = myAccountUser1FromNode2.GetAccountBallance();
if (ballanceOfUser1FromNode2 == 0)
{
    var myNewAddrForUser1FromNode2 = myAccountUser1FromNode2.NewAddress(txfee);
    Console.WriteLine(myNewAddrForUser1FromNode2);

    btcWallet.SendToAddress(NBitcoin.BitcoinAddress.Create(myNewAddrForUser1FromNode2, bitcoinNetwork), new NBitcoin.Money(10000000ul));

    btcWallet.Generate(10); // generate some blocks

    do
    {
        if (myAccountUser1FromNode2.GetAccountBallance() > 0)
            break;
        Thread.Sleep(1000);
    } while (true);

    ballanceOfUser1FromNode2 = myAccountUser1FromNode2.GetAccountBallance();
}

Console.WriteLine(ballanceOfUser1FromNode2.ToString());

//channel oppening

while (true)
{
    nd2 = LND.GetNodeInfo(lndConf, lndIdx2);
    if (nd2.SyncedToChain)
        break;
    else
        Thread.Sleep(1000);
}

var chanptFromNode2ToNode1 = wallet2.OpenChannelSync(nd1.IdentityPubkey, ballanceOfUser1FromNode2);
btcWallet.Generate(10);// generate some blocks
while ((from channel in wallet2.ListChannels(true).Channels where channel.ChannelPoint==chanptFromNode2ToNode1 select channel).Count()==0)
{
    Thread.Sleep(1000);
}

Console.WriteLine(ballanceOfUser1FromNode2.ToString());

// PAY 1->2
{
    Console.WriteLine("Balance of User1 on Node1" + myAccountUser1FromNode1.GetAccountBallance().ToString());
    Console.WriteLine("Balance of User1 on Node2" + myAccountUser1FromNode2.GetAccountBallance().ToString());
    Console.WriteLine("Balance of User2 on Node2" + myAccountUser2FromNode2.GetAccountBallance().ToString());

    var preimage = Crypto.GenerateRandomPreimage();
    var hash = Crypto.ComputePaymentHash(preimage);
    var paymentReq = myAccountUser1FromNode1.AddHodlInvoice(1000, "hello", hash, txfee);

    Console.WriteLine(paymentReq);
    Console.WriteLine(LND.DecodeInvoice(lndConf, lndIdx2, paymentReq.PaymentRequest));

    myAccountUser1FromNode2.SendPayment(paymentReq.PaymentRequest, 600, txfee, maxPaymentFee);

    while(myAccountUser1FromNode1.GetInvoiceState(hash.AsHex()) != InvoiceState.Accepted)
        Thread.Sleep(1000);

    myAccountUser1FromNode1.SettleInvoice(preimage);

    while (myAccountUser1FromNode1.GetInvoiceState(hash.AsHex()) != InvoiceState.Settled)
        Thread.Sleep(1000);

    while (myAccountUser1FromNode2.GetPaymentStatus(hash.AsHex()) != PaymentStatus.Succeeded)
        Thread.Sleep(1000);

}

// PAY 2->2
{
    var preimage = Crypto.GenerateRandomPreimage();
    var hash = Crypto.ComputePaymentHash(preimage);
    var paymentReq = myAccountUser2FromNode2.AddHodlInvoice(1000, "hello", hash, txfee);

    Console.WriteLine(paymentReq);
    Console.WriteLine(LND.DecodeInvoice(lndConf, lndIdx2, paymentReq.PaymentRequest));

    myAccountUser1FromNode2.SendPayment(paymentReq.PaymentRequest, 600, txfee, maxPaymentFee);

    while (myAccountUser2FromNode2.GetInvoiceState(hash.AsHex()) != InvoiceState.Accepted)
        Thread.Sleep(1000);

    myAccountUser2FromNode2.SettleInvoice(preimage);

    while (myAccountUser2FromNode2.GetInvoiceState(hash.AsHex()) != InvoiceState.Settled)
        Thread.Sleep(1000);

    while (myAccountUser1FromNode2.GetPaymentStatus(hash.AsHex()) != PaymentStatus.Succeeded)
        Thread.Sleep(1000);

}

// CLOSE CHANNEL

var btcReturnAddress = btcWallet.GetNewAddress().ToString();

var channelStatusStream = wallet2.CloseChannel(chanptFromNode2ToNode1);
btcWallet.Generate(10);// generate some blocks

while (await channelStatusStream.ResponseStream.MoveNext())
{
    var cur = channelStatusStream.ResponseStream.Current;
    if (cur.UpdateCase== Lnrpc.CloseStatusUpdate.UpdateOneofCase.ChanClose)
        break;
    else
        Thread.Sleep(1);
};

wallet1.Stop();
wallet2.Stop();
wallet3.Stop();
