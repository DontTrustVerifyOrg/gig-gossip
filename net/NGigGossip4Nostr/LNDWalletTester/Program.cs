// See https://aka.ms/new-console-template for more information

using CryptoToolkit;
using NBitcoin.Secp256k1;
using LNDWallet;
using System.Text.Json;
using LNDClient;
using NBitcoin.RPC;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using static LNDClient.LND;
using System.Text.Json.Nodes;

// CONFIG

IConfigurationRoot GetConfigurationRoot(string defaultFolder, string iniName)
{
    var basePath = Environment.GetEnvironmentVariable("GIGGOSSIP_BASEDIR");
    if (basePath == null)
        basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), defaultFolder);
    foreach (var arg in args)
        if (arg.StartsWith("--basedir"))
            basePath = arg.Substring(arg.IndexOf('=') + 1).Trim().Replace("\"", "").Replace("\'", "");

    var builder = new ConfigurationBuilder();
    builder.SetBasePath(basePath)
           .AddIniFile(iniName)
           .AddEnvironmentVariables()
           .AddCommandLine(args);

    return builder.Build();
}

var config = GetConfigurationRoot(".giggossip", "lndwallettest.conf");
var appSettings = config.GetSection("application").Get<ApplicationSettings>();
var btcSetting = config.GetSection("Bitcoion").Get<BitcoinSettings>();

var confs = appSettings.GetNodesConfiguration(config);


// Wallet DBs for Nodes
bool deleteDb = true; // should we delete all dbs at start (e.g. schema change)
long txfee = appSettings.TxFee;
long maxPaymentFee = appSettings.MaxPaymentFee;

var bitcoinClient = btcSetting.NewRPCClient();

// load bitcoin node wallet
RPCClient btcWallet = null;
try
{
    btcWallet = bitcoinClient.LoadWallet(btcSetting.WalletName); ;
}
catch (RPCException exception)
{
    if (exception.RPCCode == RPCErrorCode.RPC_WALLET_ALREADY_LOADED)
        btcWallet = bitcoinClient.SetWalletContext(btcSetting.WalletName);
}


//START

btcWallet.Generate(10); // generate some blocks

//wait for nodes synced
Lnrpc.GetInfoResponse nd1, nd2, nd3;
while(true)
{
    nd1 = LND.GetNodeInfo(confs[0]);
    nd2 = LND.GetNodeInfo(confs[1]);
    nd3 = LND.GetNodeInfo(confs[2]);
    if (nd1.SyncedToChain && nd2.SyncedToChain && nd3.SyncedToChain)
        break;
    else
        Thread.Sleep(1000);
}

// Write node infos
foreach (var conf in confs)
    Console.WriteLine("Pubkey: {" + LND.GetNodeInfo(conf) + "}");

//who are peeros of lnd node2
var peersof2 = LND.ListPeers(confs[1]);

//connect peer 2 to peer 1 if not connected
if (peersof2.Peers.Where((p) => p.PubKey == nd1.IdentityPubkey).Count() == 0)
    LND.Connect(confs[2], confs[1].ListenHost, nd1.IdentityPubkey);

//connect peer 2 to peer 3 if not connected
if (peersof2.Peers.Where((p) => p.PubKey == nd3.IdentityPubkey).Count() == 0)
    LND.Connect(confs[2], confs[2].ListenHost, nd3.IdentityPubkey);

//create wallets for all 3 lnd nodes
var wallet1 = new LNDWalletManager(confs[0].ConnectionString, confs[0], nd1, deleteDb: deleteDb);
wallet1.Start();
var wallet2 = new LNDWalletManager(confs[1].ConnectionString, confs[1], nd2, deleteDb: deleteDb);
wallet2.Start();
var wallet3 = new LNDWalletManager(confs[2].ConnectionString, confs[2], nd3, deleteDb: deleteDb);
wallet3.Start();

//setup user accounts
var privkeyUser1FromNode1 = Context.Instance.CreateECPrivKey(Convert.FromHexString(confs[0].PrivateKey));
var pubkeyUser1FromNode1 = privkeyUser1FromNode1.CreateXOnlyPubKey();
var myAccountUser1FromNode1 = wallet1.GetAccount(pubkeyUser1FromNode1);

var privkeyUser1FromNode2 = Context.Instance.CreateECPrivKey(Convert.FromHexString(confs[1].PrivateKey));
var pubkeyUser1FromNode2 = privkeyUser1FromNode2.CreateXOnlyPubKey();
var myAccountUser1FromNode2= wallet2.GetAccount(pubkeyUser1FromNode2);

var privkeyUser2FromNode2 = Context.Instance.CreateECPrivKey(Convert.FromHexString(confs[2].PrivateKey));
var pubkeyUser2FromNode2 = privkeyUser2FromNode2.CreateXOnlyPubKey();
var myAccountUser2FromNode2 = wallet2.GetAccount(pubkeyUser1FromNode2);


var ballanceOfUser1FromNode2 = myAccountUser1FromNode2.GetAccountBallance();
if (ballanceOfUser1FromNode2 == 0)
{
    var myNewAddrForUser1FromNode2 = myAccountUser1FromNode2.NewAddress(txfee);
    Console.WriteLine(myNewAddrForUser1FromNode2);

    btcWallet.SendToAddress(NBitcoin.BitcoinAddress.Create(myNewAddrForUser1FromNode2, btcSetting.GetNetwork()), new NBitcoin.Money(10000000ul));

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
    nd2 = LND.GetNodeInfo(confs[1]);
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
    Console.WriteLine(LND.DecodeInvoice(confs[1], paymentReq.PaymentRequest));

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
    Console.WriteLine(LND.DecodeInvoice(confs[1], paymentReq.PaymentRequest));

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


public class ApplicationSettings
{
    public string NodeSections { get; set; }
    public long TxFee { get; set; }
    public long MaxPaymentFee { get; set; }
    public List<LndSettings> GetNodesConfiguration(IConfigurationRoot config)
    {
        var lndConf = new List<LndSettings>();
        var sections = (from s in JsonArray.Parse(NodeSections).AsArray() select s.GetValue<string>()).ToList();
        foreach (var sec in sections)
        {
            var sti = config.GetSection(sec).Get<LndSettings>();
            lndConf.Add(sti);
        }
        return lndConf;
    }
}

public class LndSettings : NodeSettings
{
    public string FriendNodes { get; set; }
    public long MaxSatoshisPerChannel { get; set; }
    public string ConnectionString { get; set; }
    public string PrivateKey { get; set; }

    public List<string> GetFriendNodes()
    {
        return (from s in JsonArray.Parse(FriendNodes).AsArray() select s.GetValue<string>()).ToList();
    }
}

public class BitcoinSettings
{
    public string AuthenticationString { get; set; }
    public string HostOrUri { get; set; }
    public string Network { get; set; }
    public string WalletName { get; set; }

    public NBitcoin.Network GetNetwork()
    {
        if (Network.ToLower() == "main")
            return NBitcoin.Network.Main;
        if (Network.ToLower() == "testnet")
            return NBitcoin.Network.TestNet;
        if (Network.ToLower() == "regtest")
            return NBitcoin.Network.RegTest;
        throw new InvalidOperationException();
    }

    public RPCClient NewRPCClient()
    {
        return new RPCClient(AuthenticationString, HostOrUri, GetNetwork());
    }

}