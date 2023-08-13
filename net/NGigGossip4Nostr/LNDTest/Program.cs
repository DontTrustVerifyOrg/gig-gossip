
using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Core;
using LNDClient;
using Microsoft.Extensions.Configuration;
using static LNDClient.LND;

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

var config = GetConfigurationRoot(".giggossip", "lndtest.conf");
var lndNodeSettings = config.GetSection("lndnodes").Get<LndNodesSettings>();

var confs = lndNodeSettings.GetNodesConfiguration(config);

foreach (var conf in confs)
    Console.WriteLine(" Pubkey: {" + LND.GetNodeInfo(conf) + "}");

var peersof2 = LND.ListPeers(confs[1]);
var nd1 = LND.GetNodeInfo(confs[0]);
var nd3 = LND.GetNodeInfo(confs[2]);

if (peersof2.Peers.Where((p) => p.PubKey == nd1.IdentityPubkey).Count()==0)
    LND.Connect(confs[1], confs[0].ListenHost, nd1.IdentityPubkey);

if (peersof2.Peers.Where((p) => p.PubKey == nd3.IdentityPubkey).Count() == 0)
    LND.Connect(confs[1], confs[2].ListenHost, nd3.IdentityPubkey);

var channels2 = LND.ListChannels(confs[1]);
if (channels2.Channels.Where((c) => c.RemotePubkey == nd1.IdentityPubkey).Count() == 0)
{
    var oc2s = LND.OpenChannel(confs[1], nd1.IdentityPubkey, 100000);
    while (await oc2s.ResponseStream.MoveNext())
    {
        if (oc2s.ResponseStream.Current.ChanOpen != null)
            break;
        else
            Thread.Sleep(1);
    };
}


var preimage = CryptoToolkit.Crypto.GenerateRandomPreimage();
var hash = CryptoToolkit.Crypto.ComputePaymentHash(preimage);
var paymentReq1 = LND.AddHodlInvoice(confs[0], 1000, "hello", hash);
var paymentReq2 = LND.AddHodlInvoice(confs[0], 1000, "hello", hash);
var paymentReq3 = LND.AddHodlInvoice(confs[0], 1000, "hello", hash);
Console.WriteLine(paymentReq1.PaymentRequest);
Console.WriteLine(LND.DecodeInvoice(confs[1], paymentReq1.PaymentRequest));
Console.WriteLine(paymentReq2.PaymentRequest);
Console.WriteLine(LND.DecodeInvoice(confs[1], paymentReq2.PaymentRequest));
Console.WriteLine(paymentReq3.PaymentRequest);
Console.WriteLine(LND.DecodeInvoice(confs[1], paymentReq3.PaymentRequest));

var waiter4inv = LND.SubscribeSingleInvoice(confs[0], hash);

foreach (var conf in confs)
{
    Console.WriteLine("State: {" + LND.LookupInvoiceV2(conf, hash) + "}");
}

var waiter = LND.SendPaymentV2(confs[1], paymentReq1.PaymentRequest, 10,1000);

while (await waiter4inv.ResponseStream.MoveNext())
{
    if (waiter4inv.ResponseStream.Current.State == Lnrpc.Invoice.Types.InvoiceState.Accepted)
        break;
    else
        Thread.Sleep(1);
};

LND.SettleInvoice(confs[0], preimage);

while (LND.LookupInvoiceV2(confs[0], hash).State != Lnrpc.Invoice.Types.InvoiceState.Settled)
    Thread.Sleep(1);


while (await waiter.ResponseStream.MoveNext())
{
    if (waiter.ResponseStream.Current.Status == Lnrpc.Payment.Types.PaymentStatus.Succeeded)
        break;
    else
        Thread.Sleep(1);
};

var paymentReqC = LND.AddInvoice(confs[0], 1000, "hello");
Console.WriteLine(paymentReq2);
Console.WriteLine(LND.DecodeInvoice(confs[1], paymentReqC.PaymentRequest));
Console.WriteLine(LND.SendPayment(confs[1], paymentReqC.PaymentRequest));

var channels21 = LND.ListChannels(confs[1]);
foreach (var chanx in channels21.Channels)
    LND.CloseChannel(confs[1], chanx.ChannelPoint.Split(':')[0]);


public class LndNodesSettings
{
    public string NodeSections { get; set; }
    public List<LndSettings> GetNodesConfiguration(IConfigurationRoot config)
    {
        var lndConf = new List<LndSettings>();
        var sections = (from s in JsonArray.Parse(NodeSections).AsArray() select s.GetValue<string>()).ToList();
        foreach (var sec in sections)
        {
            var sti = config.GetSection("sec").Get<LndSettings>();
            lndConf.Add(sti);
        }
        return lndConf;
    }
}

public class LndSettings: NodeSettings
{
    public string FriendNodes { get; set; }
    public long MaxSatoshisPerChannel { get; set; }

    public List<string> GetFriendNodes()
    {
        return (from s in JsonArray.Parse(FriendNodes).AsArray() select s.GetValue<string>()).ToList();
    }
}