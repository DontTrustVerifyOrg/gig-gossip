using System;
using NGigGossip4Nostr;
using System.Diagnostics;
using System.Text;
using CryptoToolkit;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using NBitcoin.Secp256k1;
using NGigTaxiLib;
using System.Reflection;
using NGeoHash;
using NBitcoin.RPC;
using GigLNDWalletAPIClient;
using GigWorkerTest;

namespace GigWorkerComplexTest;

public class ComplexTest
{
    string[] args;

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
    NodeSettings gridNodeSettings;
    SettlerAdminSettings settlerAdminSettings;
    BitcoinSettings bitcoinSettings;
    ApplicationSettings applicationSettings;

    public ComplexTest(string[] args)
    {
        this.args = args;
        var config = GetConfigurationRoot(".giggossip", "complextest.conf");
        gridNodeSettings = config.GetSection("gridnode").Get<NodeSettings>();
        settlerAdminSettings = config.GetSection("settleradmin").Get<SettlerAdminSettings>();
        bitcoinSettings = config.GetSection("bitcoin").Get<BitcoinSettings>();
        applicationSettings = config.GetSection("application").Get<ApplicationSettings>();
    }


    HttpClient httpClient = new HttpClient();
    SimpleSettlerSelector settlerSelector = new SimpleSettlerSelector();

    public bool IsRunning { get; set; } = true;

    public void Run()
    {

        var bitcoinClient = bitcoinSettings.NewRPCClient();

        // load bitcoin node wallet
        RPCClient? bitcoinWalletClient;
        try
        {
            bitcoinWalletClient = bitcoinClient.LoadWallet(bitcoinSettings.WalletName); ;
        }
        catch (RPCException exception ) when (exception.RPCCode== RPCErrorCode.RPC_WALLET_ALREADY_LOADED)
        {
            bitcoinWalletClient = bitcoinClient.SetWalletContext(bitcoinSettings.WalletName);
        }

        bitcoinWalletClient.Generate(10); // generate some blocks


        var settlerPrivKey = settlerAdminSettings.PrivateKey.AsECPrivKey();
        var settlerPubKey = settlerPrivKey.CreateXOnlyPubKey();
        var settlerClient = settlerSelector.GetSettlerClient(settlerAdminSettings.SettlerOpenApi);
        var gtok = settlerClient.GetTokenAsync(settlerPubKey.AsHex()).Result;
        var token = Crypto.MakeSignedTimedToken(settlerPrivKey, DateTime.Now, gtok);
        var val = Convert.ToBase64String(Encoding.Default.GetBytes("ok"));

        var gridShape = applicationSettings.GetGridShape();
        var gridShapeIter = from x in gridShape select Enumerable.Range(0, x);

        var nod_name_f = (IEnumerable<int> nod_idx) => "GridNode<" + string.Join(",", nod_idx.Select(i => i.ToString()).ToList()) + ">";

        var things = new Dictionary<string, GigGossipNode>();
        foreach (var nod_idx in gridShapeIter.MultiCartesian())
        {
            things[nod_name_f(nod_idx)] = new GigGossipNode(
                gridNodeSettings.ConnectionString,
                Crypto.GeneratECPrivKey(),
                gridNodeSettings.GetNostrRelays(),
                gridNodeSettings.ChunkSize
            );
        }

        var already = new HashSet<string>();
        foreach (var nod_idx in gridShapeIter.MultiCartesian())
        {
            var node_name = nod_name_f(nod_idx);
            for (int k = 0; k < nod_idx.Length; k++)
            {
                var nod1_idx = nod_idx.Select((x, i) => i == k ? (x + 1) % gridShape[k] : x);
                var node_name_1 = nod_name_f(nod1_idx);
                if (already.Contains(node_name + ":" + node_name_1))
                    continue;
                if (already.Contains(node_name_1 + ":" + node_name))
                    continue;

                things[node_name].AddContact(things[node_name_1].PublicKey, node_name_1);
                things[node_name_1].AddContact(things[node_name].PublicKey, node_name);
                already.Add(node_name + ":" + node_name_1);
                already.Add(node_name_1 + ":" + node_name);
                Console.WriteLine(node_name + "<->" + node_name_1);
            }
        }

        var rnd = new Random();
        var thingsList = new Queue<GigGossipNode>(things.Values.OrderBy(a => rnd.Next()));

        for (int i = 0; i < applicationSettings.NumMessages; i++)
        {
            var gigWorker = thingsList.Dequeue();
            settlerClient.GiveUserPropertyAsync(
                    token, gigWorker.PublicKey,
                    "drive", val,
                    (DateTime.Now + TimeSpan.FromDays(1)).ToLongDateString()
                 ).Wait();
            var gigWorkerCert = Crypto.DeserializeObject<Certificate>(
                settlerClient.IssueCertificateAsync(
                     token, gigWorker.PublicKey, new List<string> { "drive" }).Result);

            gigWorker.Init(
                gridNodeSettings.Fanout,
                gridNodeSettings.PriceAmountForRouting,
                TimeSpan.FromMilliseconds(gridNodeSettings.BroadcastConditionsTimeoutMs),
                gridNodeSettings.BroadcastConditionsPowScheme,
                gridNodeSettings.BroadcastConditionsPowComplexity,
                TimeSpan.FromMilliseconds(gridNodeSettings.TimestampToleranceMs),
                TimeSpan.FromSeconds(gridNodeSettings.InvoicePaymentTimeoutSec),
                gridNodeSettings.GetLndWalletClient(httpClient),
                settlerSelector);
            //await gigWorker.LoadCertificates(gigWorkerSettings.SettlerOpenApi);

            gigWorker.Start(new GigWorkerGossipNodeEvents(gridNodeSettings.SettlerOpenApi, gigWorkerCert));

        }

        var customers = new List<Tuple<GigGossipNode, Certificate>>();
        for (int i = 0; i < applicationSettings.NumMessages; i++)
        {
            var customer = thingsList.Dequeue();
            settlerClient.GiveUserPropertyAsync(
                token, customer.PublicKey,
                "ride", val,
                (DateTime.Now + TimeSpan.FromDays(1)).ToLongDateString()
             ).Wait();

            var customerCert = Crypto.DeserializeObject<Certificate>(
                 settlerClient.IssueCertificateAsync(
                    token, customer.PublicKey, new List<string> { "ride" }).Result);

            customer.Init(
                gridNodeSettings.Fanout,
                gridNodeSettings.PriceAmountForRouting,
                TimeSpan.FromMilliseconds(gridNodeSettings.BroadcastConditionsTimeoutMs),
                gridNodeSettings.BroadcastConditionsPowScheme,
                gridNodeSettings.BroadcastConditionsPowComplexity,
                TimeSpan.FromMilliseconds(gridNodeSettings.TimestampToleranceMs),
                TimeSpan.FromSeconds(gridNodeSettings.InvoicePaymentTimeoutSec),
                gridNodeSettings.GetLndWalletClient(httpClient),
                settlerSelector);
            //await gigWorker.LoadCertificates(gigWorkerSettings.SettlerOpenApi);
            customer.Start(new CustomerGossipNodeEvents(this));
            customers.Add(Tuple.Create(customer, customerCert));
        }
        foreach (var node in thingsList)
        {
            node.Init(
                gridNodeSettings.Fanout,
                gridNodeSettings.PriceAmountForRouting,
                TimeSpan.FromMilliseconds(gridNodeSettings.BroadcastConditionsTimeoutMs),
                gridNodeSettings.BroadcastConditionsPowScheme,
                gridNodeSettings.BroadcastConditionsPowComplexity,
                TimeSpan.FromMilliseconds(gridNodeSettings.TimestampToleranceMs),
                TimeSpan.FromSeconds(gridNodeSettings.InvoicePaymentTimeoutSec),
                gridNodeSettings.GetLndWalletClient(httpClient),
                settlerSelector);
            //await node.LoadCertificates(gigWorkerSettings.SettlerOpenApi);        
            node.Start(new NetworkEarnerNodeEvents());
        }

        void TopupNode(GigGossipNode node, long minAmout,long topUpAmount)
        {
            var ballanceOfCustomer = node.LNDWalletClient.GetBalanceAsync(node.MakeWalletAuthToken()).Result;
            if (ballanceOfCustomer < minAmout)
            {
                var newBitcoinAddressOfCustomer = node.LNDWalletClient.NewAddressAsync(node.MakeWalletAuthToken()).Result;
                bitcoinClient.SendToAddress(NBitcoin.BitcoinAddress.Create(newBitcoinAddressOfCustomer, bitcoinSettings.GetNetwork()), new NBitcoin.Money(topUpAmount));
            }
        }

        var minAmount = 1000000;
        var topUpAmount = 10000000;

        foreach (var nod_idx in gridShapeIter.MultiCartesian())
        {
            var node_name = nod_name_f(nod_idx);
            TopupNode(things[node_name], minAmount, topUpAmount);
        }

        bitcoinClient.Generate(10); // generate some blocks

        do
        {
        outer:
            foreach (var node in things.Values)
                if (node.LNDWalletClient.GetBalanceAsync(node.MakeWalletAuthToken()).Result < minAmount)
                {
                    Thread.Sleep(1000);
                    goto outer;
                }
            break;
        } while (true);

        foreach (var customercert in customers)
        {
            var fromGh = GeoHash.Encode(latitude: 42.6, longitude: -5.6, numberOfChars: 7);
            var toGh = GeoHash.Encode(latitude: 42.5, longitude: -5.6, numberOfChars: 7);

            customercert.Item1.BroadcastTopic(new TaxiTopic()
            {
                FromGeohash = fromGh,
                ToGeohash = toGh,
                PickupAfter = DateTime.Now,
                DropoffBefore = DateTime.Now.AddMinutes(20)
            },
            customercert.Item2);

        }

        while (this.IsRunning)
        {
            lock(this)
            {
                Monitor.Wait(this);
            }
        }
        foreach (var nod_idx in gridShapeIter.MultiCartesian())
        {
            var node_name = nod_name_f(nod_idx);
            things[node_name].Stop();
        }

    }
}


public class NetworkEarnerNodeEvents : IGigGossipNodeEvents
{
    public void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, POWBroadcastFrame broadcastFrame)
    {
        var taxiTopic = Crypto.DeserializeObject<TaxiTopic>(broadcastFrame.BroadcastPayload.SignedRequestPayload.Topic);
        if (taxiTopic != null)
        {
            if (taxiTopic.FromGeohash.Length >= 7 &&
                   taxiTopic.ToGeohash.Length >= 7 &&
                   taxiTopic.DropoffBefore >= DateTime.Now)
            {
                me.BroadcastToPeers(peerPublicKey, broadcastFrame);
            }
        }
    }

    public void OnNewResponse(GigGossipNode me, ReplyPayload replyPayload, string replyInvoice, PayReq decodedReplyInvoice, string networkInvoice, PayReq decodedNetworkInvoice)
    {
    }

    public void OnResponseReady(GigGossipNode me, ReplyPayload replyPayload, string key)
    {
    }
}

public class GigWorkerGossipNodeEvents : IGigGossipNodeEvents
{
    Uri settlerUri;
    Certificate selectedCertificate;
    public GigWorkerGossipNodeEvents(Uri settlerUri, Certificate selectedCertificate)
    {
        this.settlerUri = settlerUri;
        this.selectedCertificate = selectedCertificate;
    }

    public void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, POWBroadcastFrame broadcastFrame)
    {
        var taxiTopic = Crypto.DeserializeObject<TaxiTopic>(
            broadcastFrame.BroadcastPayload.SignedRequestPayload.Topic);

        if (taxiTopic != null)
        {
            me.AcceptBraodcast( peerPublicKey, broadcastFrame,
                new AcceptBroadcastResponse()
                {
                    Message = Encoding.Default.GetBytes($"mynameis={me.PublicKey}"),
                    Fee = 4321,
                    SettlerServiceUri = settlerUri,
                    MyCertificate = selectedCertificate
                });
        }
    }

    public void OnNewResponse(GigGossipNode me, ReplyPayload replyPayload, string replyInvoice, PayReq decodedReplyInvoice, string networkInvoice, PayReq decodedNetworkInvoice)
    {
    }

    public void OnResponseReady(GigGossipNode me, ReplyPayload replyPayload, string key)
    {
    }
}

public class CustomerGossipNodeEvents : IGigGossipNodeEvents
{
    ComplexTest test;
    public CustomerGossipNodeEvents(ComplexTest test)
    {
        this.test = test;
    }

    public void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, POWBroadcastFrame broadcastFrame)
    {
    }

    Timer timer=null;
    int old_cnt = 0;

    public void OnNewResponse(GigGossipNode me, ReplyPayload replyPayload, string replyInvoice, PayReq decodedReplyInvoice, string networkInvoice, PayReq decodedNetworkInvoice)
    {
        lock (this)
        {
            if (timer == null)
                timer = new Timer((o) => {
                    var new_cnt = me.GetReplyPayloads(replyPayload.SignedRequestPayload.PayloadId).Count();
                    if (new_cnt == old_cnt)
                    {
                        var resps = me.GetReplyPayloads(replyPayload.SignedRequestPayload.PayloadId).ToList();
                        resps.Sort((a, b) => (int)(Crypto.DeserializeObject<PayReq>(a.DecodedNetworkInvoice).NumSatoshis - Crypto.DeserializeObject<PayReq>(b.DecodedNetworkInvoice).NumSatoshis));
                        var win = resps[0];
                        me.AcceptResponse(
                            Crypto.DeserializeObject<ReplyPayload>(win.TheReplyPayload),
                            win.ReplyInvoice,
                            Crypto.DeserializeObject<PayReq>(win.DecodedReplyInvoice),
                            win.NetworkInvoice,
                            Crypto.DeserializeObject<PayReq>(win.DecodedNetworkInvoice));
                    }
                    else
                    {
                        old_cnt = new_cnt;
                        timer.Change(5000, Timeout.Infinite);
                    }
                },null,5000, Timeout.Infinite);
        }
    }

    public void OnResponseReady(GigGossipNode me, ReplyPayload replyPayload, string key)
    {
        var message = Crypto.SymmetricDecrypt<byte[]>(
            key.AsBytes(),
            replyPayload.EncryptedReplyMessage);
        Trace.TraceInformation(Encoding.Default.GetString(message));
        lock(test)
        {
            test.IsRunning = false;
            Monitor.PulseAll(test);
        }
    }
}

public class SettlerAdminSettings
{
    public required Uri SettlerOpenApi { get; set; }
    public required string PrivateKey { get; set; }
}

public class ApplicationSettings
{
    public required string GridShape { get; set; }
    public required int NumMessages { get; set; }
    public int[] GetGridShape()
    {
        return (from s in JsonArray.Parse(GridShape).AsArray() select s.GetValue<int>()).ToArray();
    }
}
public class NodeSettings
{
    public required string ConnectionString { get; set; }
    public required Uri GigWalletOpenApi { get; set; }
    public required string NostrRelays { get; set; }
    public required string PrivateKey { get; set; }
    public required Uri SettlerOpenApi { get; set; }
    public long PriceAmountForRouting { get; set; }
    public long BroadcastConditionsTimeoutMs { get; set; }
    public required string BroadcastConditionsPowScheme { get; set; }
    public int BroadcastConditionsPowComplexity { get; set; }
    public long TimestampToleranceMs { get; set; }
    public long InvoicePaymentTimeoutSec { get; set; }
    public int ChunkSize { get; set; }
    public int Fanout { get; set; }

    public string[] GetNostrRelays()
    {
        return (from s in JsonArray.Parse(NostrRelays).AsArray() select s.GetValue<string>()).ToArray();
    }

    public GigLNDWalletAPIClient.swaggerClient GetLndWalletClient(HttpClient httpClient)
    {
        return new GigLNDWalletAPIClient.swaggerClient(GigWalletOpenApi.AbsoluteUri, httpClient);
    }
}


public class BitcoinSettings
{
    public required string AuthenticationString { get; set; }
    public required string HostOrUri { get; set; }
    public required string Network { get; set; }
    public required string WalletName { get; set; }

    public NBitcoin.Network GetNetwork()
    {
        if (Network.ToLower() == "main")
            return NBitcoin.Network.Main;
        if (Network.ToLower() == "testnet")
            return NBitcoin.Network.TestNet;
        if (Network.ToLower() == "regtest")
            return NBitcoin.Network.RegTest;
        throw new NotImplementedException();
    }

    public RPCClient NewRPCClient()
    {
        return new RPCClient(AuthenticationString, HostOrUri, GetNetwork());
    }

}