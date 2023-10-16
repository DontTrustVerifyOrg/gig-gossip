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

namespace GigWorkerMediumTest;

public static class MainThreadControl
{
    public static int Counter { get; set; } = 0;
    public static object Ctrl = new object();
}

public class MediumTest
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
    NodeSettings gigWorkerSettings, customerSettings, gossiperSettings;
    SettlerAdminSettings settlerAdminSettings;
    BitcoinSettings bitcoinSettings;
    ApplicationSettings applicationSettings;

    public MediumTest(string[] args)
    {
        this.args = args;
        var config = GetConfigurationRoot(".giggossip", "mediumtest.conf");
        gigWorkerSettings = config.GetSection("gigworker").Get<NodeSettings>();
        customerSettings = config.GetSection("customer").Get<NodeSettings>();
        gossiperSettings = config.GetSection("gossiper").Get<NodeSettings>();
        settlerAdminSettings = config.GetSection("settleradmin").Get<SettlerAdminSettings>();
        bitcoinSettings = config.GetSection("bitcoin").Get<BitcoinSettings>();
        applicationSettings = config.GetSection("application").Get<ApplicationSettings>();
    }


    HttpClient httpClient = new HttpClient();

    public async Task RunAsync()
    {
        FlowLogger.Start(applicationSettings.FlowLoggerPath.Replace("$HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));

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

        var settlerSelector = new SimpleSettlerSelector();
        var settlerPrivKey = settlerAdminSettings.PrivateKey.AsECPrivKey();
        var settlerPubKey = settlerPrivKey.CreateXOnlyPubKey();
        var settlerClient = settlerSelector.GetSettlerClient(settlerAdminSettings.SettlerOpenApi);
        var gtok = await settlerClient.GetTokenAsync(settlerPubKey.AsHex());
        var token = Crypto.MakeSignedTimedToken(settlerPrivKey, DateTime.Now, gtok);
        var val = Convert.ToBase64String(Encoding.Default.GetBytes("ok"));

        FlowLogger.SetupParticipantWithAutoAlias(Encoding.Default.GetBytes(settlerAdminSettings.SettlerOpenApi.AbsoluteUri).AsHex(), "settler", false);

        var gigWorker = new GigGossipNode(
            gigWorkerSettings.ConnectionString,
            gigWorkerSettings.PrivateKey.AsECPrivKey(),
            gigWorkerSettings.GetNostrRelays(),
            gigWorkerSettings.ChunkSize
            );

        FlowLogger.SetupParticipant(gigWorker.PublicKey, "GigWorker", true);

        await settlerClient.GiveUserPropertyAsync(
                token, gigWorker.PublicKey,
                "drive", val,
                (DateTime.Now + TimeSpan.FromDays(1)).ToLongDateString()
             );

        var gigWorkerCert = Crypto.DeserializeObject<Certificate>(
            await settlerClient.IssueCertificateAsync(
                 token, gigWorker.PublicKey, new List<string> { "drive" }));


        var gossipers = new List<GigGossipNode>();
        for (int i = 0; i < applicationSettings.NumberOfGossipers; i++)
        {
            var gossiper = new GigGossipNode(
                gossiperSettings.ConnectionString,
                Crypto.GeneratECPrivKey(),
                gossiperSettings.GetNostrRelays(),
                gossiperSettings.ChunkSize
                );
            gossipers.Add(gossiper);
            FlowLogger.SetupParticipant(gossiper.PublicKey, "Gossiper" + i.ToString(), true);
        }

        var customer = new GigGossipNode(
            customerSettings.ConnectionString,
            customerSettings.PrivateKey.AsECPrivKey(),
            customerSettings.GetNostrRelays(),
            customerSettings.ChunkSize
            );

        FlowLogger.SetupParticipant(customer.PublicKey, "Customer", true);

        await settlerClient.GiveUserPropertyAsync(
            token, customer.PublicKey,
            "ride", val,
            (DateTime.Now + TimeSpan.FromDays(1)).ToLongDateString()
         );

        var customerCert = Crypto.DeserializeObject<Certificate>(
             await settlerClient.IssueCertificateAsync(
                token, customer.PublicKey, new List<string> { "ride" }));


        gigWorker.Init(
            gigWorkerSettings.Fanout,
            gigWorkerSettings.PriceAmountForRouting,
            TimeSpan.FromMilliseconds(gigWorkerSettings.BroadcastConditionsTimeoutMs),
            gigWorkerSettings.BroadcastConditionsPowScheme,
            gigWorkerSettings.BroadcastConditionsPowComplexity,
            TimeSpan.FromMilliseconds(gigWorkerSettings.TimestampToleranceMs),
            TimeSpan.FromSeconds(gigWorkerSettings.InvoicePaymentTimeoutSec),
            gigWorkerSettings.GetLndWalletClient(httpClient));
        //await gigWorker.LoadCertificates(gigWorkerSettings.SettlerOpenApi);


        foreach(var node in gossipers)
        {
            node.Init(
            gossiperSettings.Fanout,
            gossiperSettings.PriceAmountForRouting,
            TimeSpan.FromMilliseconds(gossiperSettings.BroadcastConditionsTimeoutMs),
            gossiperSettings.BroadcastConditionsPowScheme,
            gossiperSettings.BroadcastConditionsPowComplexity,
            TimeSpan.FromMilliseconds(gossiperSettings.TimestampToleranceMs),
            TimeSpan.FromSeconds(gossiperSettings.InvoicePaymentTimeoutSec),
            gossiperSettings.GetLndWalletClient(httpClient));
        }

        customer.Init(
            customerSettings.Fanout,
            customerSettings.PriceAmountForRouting,
            TimeSpan.FromMilliseconds(customerSettings.BroadcastConditionsTimeoutMs),
            customerSettings.BroadcastConditionsPowScheme,
            customerSettings.BroadcastConditionsPowComplexity,
            TimeSpan.FromMilliseconds(customerSettings.TimestampToleranceMs),
            TimeSpan.FromSeconds(customerSettings.InvoicePaymentTimeoutSec),
            customerSettings.GetLndWalletClient(httpClient));

        //await customer.LoadCertificates(customerSettings.SettlerOpenApi);

        async void TopupNode(GigGossipNode node, long minAmout,long topUpAmount)
        {
            var ballanceOfCustomer = await node.LNDWalletClient.GetBalanceAsync(node.MakeWalletAuthToken());
            if (ballanceOfCustomer < minAmout)
            {
                var newBitcoinAddressOfCustomer = await node.LNDWalletClient.NewAddressAsync(node.MakeWalletAuthToken());
                bitcoinClient.SendToAddress(NBitcoin.BitcoinAddress.Create(newBitcoinAddressOfCustomer, bitcoinSettings.GetNetwork()), new NBitcoin.Money(topUpAmount));
            }
        }

        var minAmount = 1000000;
        var topUpAmount = 10000000;
        TopupNode(customer, minAmount, topUpAmount);
        foreach (var node in gossipers)
            TopupNode(node, minAmount, topUpAmount);

        bitcoinClient.Generate(10); // generate some blocks

        do
        {
            if (await customer.LNDWalletClient.GetBalanceAsync(customer.MakeWalletAuthToken()) >= minAmount)
            {
            outer:
                foreach (var node in gossipers)
                    if (await node.LNDWalletClient.GetBalanceAsync(node.MakeWalletAuthToken()) < minAmount)
                    {
                        Thread.Sleep(1000);
                        goto outer;
                    }
                break;
            }
        } while (true);

        await gigWorker.StartAsync(new GigWorkerGossipNodeEvents(gigWorkerSettings.SettlerOpenApi, gigWorkerCert));
        gigWorker.ClearContacts();
        foreach (var node in gossipers)
        {
            await node.StartAsync(new NetworkEarnerNodeEvents());
            node.ClearContacts();
        }
        await customer.StartAsync(new CustomerGossipNodeEvents(this));
        customer.ClearContacts();

        gigWorker.AddContact( gossipers[0].PublicKey, "Gossiper0");
        gossipers[0].AddContact( gigWorker.PublicKey, "GigWorker");

        for (int i = 0; i < gossipers.Count; i++)
            for (int j = 0; j < gossipers.Count; j++)
            {
                if (i == j)
                    continue;
                gossipers[i].AddContact(gossipers[j].PublicKey, "Gossiper" + j.ToString());
            }

        customer.AddContact(gossipers[gossipers.Count-1].PublicKey, "Gossiper"+(gossipers.Count - 1).ToString());
        gossipers[gossipers.Count - 1].AddContact(customer.PublicKey, "Customer");

        {
            var fromGh = GeoHash.Encode(latitude: 42.6, longitude: -5.6, numberOfChars: 7);
            var toGh = GeoHash.Encode(latitude: 42.5, longitude: -5.6, numberOfChars: 7);

            await customer.BroadcastTopicAsync(new TaxiTopic()
            {
                FromGeohash = fromGh,
                ToGeohash = toGh,
                PickupAfter = DateTime.Now,
                DropoffBefore = DateTime.Now.AddMinutes(20)
            },
            customerCert);

        }

        while (MainThreadControl.Counter == 0)
        {
            lock (MainThreadControl.Ctrl)
                Monitor.Wait(MainThreadControl.Ctrl);
        }
        while (MainThreadControl.Counter > 0)
        {
            lock (MainThreadControl.Ctrl)
                Monitor.Wait(MainThreadControl.Ctrl);
        }

        gigWorker.Stop();
        foreach (var node in gossipers)
            node.Stop();
        customer.Stop();

        FlowLogger.Stop();
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
                me.BroadcastToPeersAsync(peerPublicKey, broadcastFrame);
            }
        }
    }

    public void OnNewResponse(GigGossipNode me, ReplyPayload replyPayload, string replyInvoice, PayReq decodedReplyInvoice, string networkInvoice, PayReq decodedNetworkInvoice)
    {
    }

    public void OnResponseReady(GigGossipNode me, ReplyPayload replyPayload, string key)
    {
    }

    public async void OnNetworkInvoiceAccepted(GigGossipNode me, InvoiceAcceptedData iac)
    {
        await me.PayNetworkInvoiceAsync(iac);
        lock (MainThreadControl.Ctrl)
        {
            MainThreadControl.Counter++;
            Monitor.PulseAll(MainThreadControl.Ctrl);
        }
    }

    public void OnInvoiceSettled(GigGossipNode me, Uri serviceUri, string paymentHash, string preimage)
    {
        FlowLogger.NewMessage(me.PublicKey, paymentHash, "InvoiceSettled");
        lock (MainThreadControl.Ctrl)
        {
            MainThreadControl.Counter--;
            Monitor.PulseAll(MainThreadControl.Ctrl);
        }
    }

    public void OnPaymentStatusChange(GigGossipNode me, string status, PaymentData paydata)
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

    public async void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, POWBroadcastFrame broadcastFrame)
    {
        var taxiTopic = Crypto.DeserializeObject<TaxiTopic>(
            broadcastFrame.BroadcastPayload.SignedRequestPayload.Topic);

        if (taxiTopic != null)
        {
            await me.AcceptBroadcastAsync( peerPublicKey, broadcastFrame,
                new AcceptBroadcastResponse()
                {
                    Message = Encoding.Default.GetBytes(me.PublicKey),
                    Fee = 4321,
                    SettlerServiceUri = settlerUri,
                    MyCertificate = selectedCertificate
                });
            FlowLogger.NewEvent(me.PublicKey, "AcceptBraodcast");
        }
    }

    public async void OnNetworkInvoiceAccepted(GigGossipNode me, InvoiceAcceptedData iac)
    {
        await me.PayNetworkInvoiceAsync(iac);
        lock (MainThreadControl.Ctrl)
        {
            MainThreadControl.Counter++;
            Monitor.PulseAll(MainThreadControl.Ctrl);
        }
    }

    public void OnInvoiceSettled(GigGossipNode me, Uri serviceUri, string paymentHash, string preimage)
    {
        FlowLogger.NewMessage(me.PublicKey, paymentHash, "InvoiceSettled");
        lock (MainThreadControl.Ctrl)
        {
            MainThreadControl.Counter--;
            Monitor.PulseAll(MainThreadControl.Ctrl);
        }
    }

    public void OnNewResponse(GigGossipNode me, ReplyPayload replyPayload, string replyInvoice, PayReq decodedReplyInvoice, string networkInvoice, PayReq decodedNetworkInvoice)
    {
    }

    public void OnPaymentStatusChange(GigGossipNode me, string status, PaymentData paydata)
    {
    }

    public void OnResponseReady(GigGossipNode me, ReplyPayload replyPayload, string key)
    {
    }
}

public class CustomerGossipNodeEvents : IGigGossipNodeEvents
{
    MediumTest test;
    public CustomerGossipNodeEvents(MediumTest test)
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
                    timer.Change(Timeout.Infinite, Timeout.Infinite);
                    var new_cnt = me.GetReplyPayloads(replyPayload.SignedRequestPayload.PayloadId).Count();
                    if (new_cnt == old_cnt)
                    {
                        var resps = me.GetReplyPayloads(replyPayload.SignedRequestPayload.PayloadId).ToList();
                        resps.Sort((a, b) => (int)(Crypto.DeserializeObject<PayReq>(a.DecodedNetworkInvoice).NumSatoshis - Crypto.DeserializeObject<PayReq>(b.DecodedNetworkInvoice).NumSatoshis));
                        var win = resps[0];
                        me.AcceptResponseAsync(
                            Crypto.DeserializeObject<ReplyPayload>(win.TheReplyPayload),
                            win.ReplyInvoice,
                            Crypto.DeserializeObject<PayReq>(win.DecodedReplyInvoice),
                            win.NetworkInvoice,
                            Crypto.DeserializeObject<PayReq>(win.DecodedNetworkInvoice));
                        FlowLogger.NewEvent(me.PublicKey, "AcceptResponse");
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
        var message = Encoding.Default.GetString(Crypto.SymmetricDecrypt<byte[]>(
            key.AsBytes(),
            replyPayload.EncryptedReplyMessage));
        Trace.TraceInformation(message);
        FlowLogger.NewEvent(me.PublicKey, "OnResponseReady");
        FlowLogger.NewConnected(message, me.PublicKey, "connected");
    }

    public void OnReplyInvoiceSettled(GigGossipNode me, Uri serviceUri, string paymentHash, string preimage)
    {
    }

    public void OnNetworkInvoiceAccepted(GigGossipNode me, InvoiceAcceptedData iac)
    {
    }

    public void OnInvoiceSettled(GigGossipNode me, Uri serviceUri, string paymentHash, string preimage)
    {
    }

    public void OnPaymentStatusChange(GigGossipNode me, string status, PaymentData paydata)
    {
    }
}

public class SettlerAdminSettings
{
    public required Uri SettlerOpenApi { get; set; }
    public required string PrivateKey { get; set; }
}

public class ApplicationSettings
{
    public required string FlowLoggerPath { get; set; }
    public required int NumberOfGossipers { get; set; }
}

public class NodeSettings
{
    public required string ConnectionString { get; set; }
    public required Uri GigWalletOpenApi { get; set; }
    public required string NostrRelays { get; set; }
    public required string PrivateKey { get; set; }
    public required Uri SettlerOpenApi { get; set; }
    public required long PriceAmountForRouting { get; set; }
    public required long BroadcastConditionsTimeoutMs { get; set; }
    public required string BroadcastConditionsPowScheme { get; set; }
    public required int BroadcastConditionsPowComplexity { get; set; }
    public required long TimestampToleranceMs { get; set; }
    public required long InvoicePaymentTimeoutSec { get; set; }
    public required int ChunkSize { get; set; }
    public required int Fanout { get; set; }

    public string[] GetNostrRelays()
    {
        return (from s in JsonArray.Parse(NostrRelays)!.AsArray() select s.GetValue<string>()).ToArray();
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