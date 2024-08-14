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
using GigGossipSettlerAPIClient;
using System.Net.Http;
using Microsoft.AspNetCore.SignalR.Client;
using NetworkClientToolkit;

namespace GigWorkerMediumTest;

public static class MainThreadControl
{
    public static int Counter { get; set; } = 0;
    public static object Ctrl = new object();
}

public class MediumTest
{
   
    NodeSettings gigWorkerSettings, customerSettings, gossiperSettings;
    SettlerAdminSettings settlerAdminSettings;
    BitcoinSettings bitcoinSettings;
    ApplicationSettings applicationSettings;

    public MediumTest(IConfigurationRoot config)
    {
        gigWorkerSettings = config.GetSection("gigworker").Get<NodeSettings>();
        customerSettings = config.GetSection("customer").Get<NodeSettings>();
        gossiperSettings = config.GetSection("gossiper").Get<NodeSettings>();
        settlerAdminSettings = config.GetSection("settleradmin").Get<SettlerAdminSettings>();
        bitcoinSettings = config.GetSection("bitcoin").Get<BitcoinSettings>();
        applicationSettings = config.GetSection("application").Get<ApplicationSettings>();
    }


    public async Task RunAsync()
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
        var settlerClient = new SettlerAPIRetryWrapper(settlerAdminSettings.SettlerOpenApi.AbsoluteUri, new HttpClient(), new DefaultRetryPolicy());
        var gtok = SettlerAPIResult.Get<Guid>(await settlerClient.GetTokenAsync(settlerPubKey.AsHex(),CancellationToken.None));
        var token = Crypto.MakeSignedTimedToken(settlerPrivKey, DateTime.UtcNow, gtok);
        var val = Convert.ToBase64String(Encoding.Default.GetBytes("ok"));

        var gigWorker = new GigGossipNode(
            gigWorkerSettings.ConnectionString,
            gigWorkerSettings.PrivateKey.AsECPrivKey(),
            gigWorkerSettings.ChunkSize,
            new DefaultRetryPolicy(),
            () => new HttpClient(),
            true,
            gigWorkerSettings.LoggerOpenApi
            );

        SettlerAPIResult.Check(await settlerClient.GiveUserPropertyAsync(
                token, gigWorker.PublicKey,
                "drive", val, val,
                24, CancellationToken.None
             ));

        var gossipers = new List<GigGossipNode>();
        for (int i = 0; i < applicationSettings.NumberOfGossipers; i++)
        {
            var gossiper = new GigGossipNode(
                gossiperSettings.ConnectionString,
                Crypto.GeneratECPrivKey(),
                gossiperSettings.ChunkSize,
                new DefaultRetryPolicy(),
                () => new HttpClient(),
                true,
                gossiperSettings.LoggerOpenApi
                );
            gossipers.Add(gossiper);
        }

        var customer = new GigGossipNode(
            customerSettings.ConnectionString,
            customerSettings.PrivateKey.AsECPrivKey(),
            customerSettings.ChunkSize,
            new DefaultRetryPolicy(),
            () => new HttpClient(),
            true,
            customerSettings.LoggerOpenApi
            );


        SettlerAPIResult.Check(await settlerClient.GiveUserPropertyAsync(
            token, customer.PublicKey,
            "ride", val, val,
            24, CancellationToken.None
         ));

        await gigWorker.StartAsync(
            gigWorkerSettings.Fanout,
            gigWorkerSettings.PriceAmountForRouting,
            TimeSpan.FromMilliseconds(gigWorkerSettings.TimestampToleranceMs),
            TimeSpan.FromSeconds(gigWorkerSettings.InvoicePaymentTimeoutSec),
            gigWorkerSettings.GetNostrRelays(),
            new GigWorkerGossipNodeEvents {  SettlerUri =gigWorkerSettings.SettlerOpenApi, FeeLimit = gigWorkerSettings.FeeLimit },
            gigWorkerSettings.GigWalletOpenApi,
            gigWorkerSettings.SettlerOpenApi
            );
        gigWorker.ClearContacts();

        foreach(var node in gossipers)
        {
            await node.StartAsync(
                gossiperSettings.Fanout,
                gossiperSettings.PriceAmountForRouting,
                TimeSpan.FromMilliseconds(gossiperSettings.TimestampToleranceMs),
                TimeSpan.FromSeconds(gossiperSettings.InvoicePaymentTimeoutSec),
                gossiperSettings.GetNostrRelays(),
                new NetworkEarnerNodeEvents { FeeLimit = gossiperSettings.FeeLimit },
                gossiperSettings.GigWalletOpenApi,
                gossiperSettings.SettlerOpenApi);
            node.ClearContacts();
        }

        await customer.StartAsync(
            customerSettings.Fanout,
            customerSettings.PriceAmountForRouting,
            TimeSpan.FromMilliseconds(customerSettings.TimestampToleranceMs),
            TimeSpan.FromSeconds(customerSettings.InvoicePaymentTimeoutSec),
            customerSettings.GetNostrRelays(),
            new CustomerGossipNodeEvents { Test = this, FeeLimit = customerSettings.FeeLimit },
            customerSettings.GigWalletOpenApi,
            customerSettings.SettlerOpenApi);
        customer.ClearContacts();

        async Task TopupNode(GigGossipNode node, long minAmout,long topUpAmount)
        {
            var ballanceOfCustomer = WalletAPIResult.Get<long>(await node.GetWalletClient().GetBalanceAsync(await node.MakeWalletAuthToken(), CancellationToken.None));
            if (ballanceOfCustomer < minAmout)
            {
                var newBitcoinAddressOfCustomer = WalletAPIResult.Get<string>(await node.GetWalletClient().NewAddressAsync(await node.MakeWalletAuthToken(), CancellationToken.None));
                bitcoinClient.SendToAddress(NBitcoin.BitcoinAddress.Create(newBitcoinAddressOfCustomer, bitcoinSettings.GetNetwork()), new NBitcoin.Money(topUpAmount));
            }
        }

        bitcoinClient.Generate(10); // generate some blocks

        var minAmount = 1000000;
        var topUpAmount = 10000000;
        await TopupNode(customer, minAmount, topUpAmount);
        foreach (var node in gossipers)
            await TopupNode(node, minAmount, topUpAmount);

        bitcoinClient.Generate(10); // generate some blocks

        do
        {
            if (WalletAPIResult.Get<long>(await customer.GetWalletClient().GetBalanceAsync(await customer.MakeWalletAuthToken(), CancellationToken.None)) >= minAmount)
            {
            outer:
                foreach (var node in gossipers)
                    if (WalletAPIResult.Get<long>(await node.GetWalletClient().GetBalanceAsync(await node.MakeWalletAuthToken(), CancellationToken.None)) < minAmount)
                    {
                        Thread.Sleep(1000);
                        goto outer;
                    }
                break;
            }
        } while (true);

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
                PickupAfter = DateTime.UtcNow,
                DropoffBefore = DateTime.UtcNow.AddMinutes(20)
            },
            new[] {"ride"},
            async (_) => { });

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

        await gigWorker.StopAsync();
        foreach (var node in gossipers)
            await node.StopAsync();
        await customer.StopAsync();
    }
}


public class NetworkEarnerNodeEvents : IGigGossipNodeEvents
{
    public required long FeeLimit;

    public void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, BroadcastFrame broadcastFrame)
    {
        var taxiTopic = Crypto.BinaryDeserializeObject<TaxiTopic>(broadcastFrame.SignedRequestPayload.Value.Topic);
        if (taxiTopic != null)
        {
            if (taxiTopic.FromGeohash.Length >= 7 &&
                   taxiTopic.ToGeohash.Length >= 7 &&
                   taxiTopic.DropoffBefore >= DateTime.UtcNow)
            {
                me.BroadcastToPeersAsync(peerPublicKey, broadcastFrame);
            }
        }
    }

    public void OnNewResponse(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload, string replyInvoice, PayReqRet decodedReplyInvoice, string networkInvoice, PayReqRet decodedNetworkInvoice)
    {
    }
    public void OnResponseReady(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload, string key)
    {
    }
    public void OnResponseCancelled(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload)
    {
    }

    public async void OnNetworkInvoiceAccepted(GigGossipNode me, InvoiceData iac)
    {
        var paymentResult = await me.PayNetworkInvoiceAsync(iac, FeeLimit, CancellationToken.None);
        if (paymentResult != GigLNDWalletAPIErrorCode.Ok)
        {
            Console.WriteLine(paymentResult);
        }
        lock (MainThreadControl.Ctrl)
        {
            MainThreadControl.Counter++;
            Monitor.PulseAll(MainThreadControl.Ctrl);
        }
    }

    public async void OnInvoiceSettled(GigGossipNode me, Uri serviceUri, string paymentHash, string preimage)
    {
        await me.FlowLogger.NewMessageAsync(me.PublicKey, paymentHash, "InvoiceSettled");
        lock (MainThreadControl.Ctrl)
        {
            MainThreadControl.Counter--;
            Monitor.PulseAll(MainThreadControl.Ctrl);
        }
    }

    public void OnPaymentStatusChange(GigGossipNode me, string status, PaymentData paydata)
    {
    }

    public void OnCancelBroadcast(GigGossipNode me, string peerPublicKey, CancelBroadcastFrame broadcastFrame)
    {
    }
    public void OnNetworkInvoiceCancelled(GigGossipNode me, InvoiceData iac)
    {
    }

    public void OnInvoiceAccepted(GigGossipNode me, InvoiceData iac)
    {
    }

    public void OnInvoiceCancelled(GigGossipNode me, InvoiceData iac)
    {
    }

    public void OnNewContact(GigGossipNode me, string pubkey)
    {
    }

    public void OnSettings(GigGossipNode me, string settings)
    {
    }

    public void OnEoseArrived(GigGossipNode me)
    {
    }

    public void OnServerConnectionState(GigGossipNode me, ServerConnectionSource source, ServerConnectionState state, Uri uri)
    {
    }
}

public class GigWorkerGossipNodeEvents : IGigGossipNodeEvents
{
    public required Uri SettlerUri;
    public required long FeeLimit;

    public async void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, BroadcastFrame broadcastFrame)
    {
        var taxiTopic = Crypto.BinaryDeserializeObject<TaxiTopic>(
            broadcastFrame.SignedRequestPayload.Value.Topic);

        if (taxiTopic != null)
        {
            await me.AcceptBroadcastAsync( peerPublicKey, broadcastFrame,
                new AcceptBroadcastResponse()
                {
                    Properties = new string[] { "drive"},
                    Message = Encoding.Default.GetBytes(me.PublicKey),
                    Fee = 4321,
                    SettlerServiceUri = SettlerUri,
                }, async (_) => { },
                CancellationToken.None);
            await me.FlowLogger.NewNoteAsync(me.PublicKey, "AcceptBraodcast");
        }
    }

    public async void OnNetworkInvoiceAccepted(GigGossipNode me, InvoiceData iac)
    {
        var paymentResult = await me.PayNetworkInvoiceAsync(iac, FeeLimit, CancellationToken.None);
        if (paymentResult != GigLNDWalletAPIErrorCode.Ok)
        {
            Console.WriteLine(paymentResult);
        }
        lock (MainThreadControl.Ctrl)
        {
            MainThreadControl.Counter++;
            Monitor.PulseAll(MainThreadControl.Ctrl);
        }
    }

    public async void OnInvoiceSettled(GigGossipNode me, Uri serviceUri, string paymentHash, string preimage)
    {
        await me.FlowLogger.NewMessageAsync(me.PublicKey, paymentHash, "InvoiceSettled");
        lock (MainThreadControl.Ctrl)
        {
            MainThreadControl.Counter--;
            Monitor.PulseAll(MainThreadControl.Ctrl);
        }
    }

    public async void OnNewResponse(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload, string replyInvoice, PayReqRet decodedReplyInvoice, string networkInvoice, PayReqRet decodedNetworkInvoice)
    {
    }
    public void OnResponseReady(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload, string key)
    {
    }
    public void OnResponseCancelled(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload)
    {
    }

    public void OnPaymentStatusChange(GigGossipNode me, string status, PaymentData paydata)
    {
    }

    public void OnCancelBroadcast(GigGossipNode me, string peerPublicKey, CancelBroadcastFrame broadcastFrame)
    {
    }

    public void OnNetworkInvoiceCancelled(GigGossipNode me, InvoiceData iac)
    {
    }

    public void OnInvoiceAccepted(GigGossipNode me, InvoiceData iac)
    {
    }

    public void OnInvoiceCancelled(GigGossipNode me, InvoiceData iac)
    {
    }

    public void OnNewContact(GigGossipNode me, string pubkey)
    {
    }

    public void OnSettings(GigGossipNode me, string settings)
    {
    }

    public void OnEoseArrived(GigGossipNode me)
    {
    }

    public void OnServerConnectionState(GigGossipNode me, ServerConnectionSource source, ServerConnectionState state, Uri uri)
    {
    }
}

public class CustomerGossipNodeEvents : IGigGossipNodeEvents
{
    public required MediumTest Test;
    public required long FeeLimit;

    public void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, BroadcastFrame broadcastFrame)
    {
    }

    Timer timer=null;
    int old_cnt = 0;

    public async void OnNewResponse(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload, string replyInvoice, PayReqRet decodedReplyInvoice, string networkInvoice, PayReqRet decodedNetworkInvoice)
    {
        lock (this)
        {
            if (timer == null)
                timer = new Timer(async (o) => {
                    timer.Change(Timeout.Infinite, Timeout.Infinite);
                    var resps = me.GetReplyPayloads(replyPayload.Value.SignedRequestPayload.Id);
                    if (resps.Count == old_cnt)
                    {
                        resps.Sort((a, b) => (int)(Crypto.JsonSnappyDeserializeObject<PayReqRet>(a.DecodedNetworkInvoice).ValueSat - Crypto.BinaryDeserializeObject<PayReqRet>(b.DecodedNetworkInvoice).ValueSat));
                        var win = resps[0];
                        var paymentResult = await me.AcceptResponseAsync(
                            Crypto.BinaryDeserializeObject<Certificate<ReplyPayloadValue>>(win.TheReplyPayload),
                            win.ReplyInvoice,
                            Crypto.JsonSnappyDeserializeObject<PayReqRet>(win.DecodedReplyInvoice),
                            win.NetworkInvoice,
                            Crypto.JsonSnappyDeserializeObject<PayReqRet>(win.DecodedNetworkInvoice),
                            FeeLimit,
                            CancellationToken.None);
                        if (paymentResult != GigLNDWalletAPIErrorCode.Ok)
                        {
                            Console.WriteLine(paymentResult);
                            return;
                        }
                        await me.FlowLogger.NewNoteAsync(me.PublicKey, "AcceptResponse");
                    }
                    else
                    {
                        old_cnt = resps.Count;
                        timer.Change(5000, Timeout.Infinite);
                    }
                },null,5000, Timeout.Infinite);
        }
    }

    public async void OnResponseReady(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload, string key)
    {
        var message = Encoding.Default.GetString(Crypto.SymmetricDecrypt<byte[]>(
            key.AsBytes(),
            replyPayload.Value.EncryptedReplyMessage));
        Trace.TraceInformation(message);
        await me.FlowLogger.NewNoteAsync(me.PublicKey, "OnResponseReady");
        await me.FlowLogger.NewConnectedAsync(message, me.PublicKey, "connected");
    }
    public void OnResponseCancelled(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload)
    {
    }

    public void OnReplyInvoiceSettled(GigGossipNode me, Uri serviceUri, string paymentHash, string preimage)
    {
    }

    public void OnNetworkInvoiceAccepted(GigGossipNode me, InvoiceData iac)
    {
    }

    public void OnInvoiceSettled(GigGossipNode me, Uri serviceUri, string paymentHash, string preimage)
    {
    }

    public void OnPaymentStatusChange(GigGossipNode me, string status, PaymentData paydata)
    {
    }

    public void OnCancelBroadcast(GigGossipNode me, string peerPublicKey, CancelBroadcastFrame broadcastFrame)
    {
    }
    public void OnNetworkInvoiceCancelled(GigGossipNode me, InvoiceData iac)
    {
    }

    public void OnInvoiceAccepted(GigGossipNode me, InvoiceData iac)
    {
    }

    public void OnInvoiceCancelled(GigGossipNode me, InvoiceData iac)
    {
    }

    public void OnNewContact(GigGossipNode me, string pubkey)
    {
    }

    public void OnSettings(GigGossipNode me, string settings)
    {
    }

    public void OnEoseArrived(GigGossipNode me)
    {
    }

    public void OnServerConnectionState(GigGossipNode me, ServerConnectionSource source, ServerConnectionState state, Uri uri)
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
    public required Uri LoggerOpenApi { get; set; }
    public required long PriceAmountForRouting { get; set; }
    public required long TimestampToleranceMs { get; set; }
    public required long InvoicePaymentTimeoutSec { get; set; }
    public required int ChunkSize { get; set; }
    public required int Fanout { get; set; }
    public required long FeeLimit { get; set; }

    public string[] GetNostrRelays()
    {
        return (from s in JsonArray.Parse(NostrRelays)!.AsArray() select s.GetValue<string>()).ToArray();
    }

    public IWalletAPI GetLndWalletClient(HttpClient httpClient, IRetryPolicy retryPolicy)
    {
        return new WalletAPIRetryWrapper(GigWalletOpenApi.AbsoluteUri, httpClient, retryPolicy);
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


public sealed class DefaultRetryPolicy : IRetryPolicy
{
    private static TimeSpan?[] DefaultBackoffTimes = new TimeSpan?[]
    {
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        null
    };

    TimeSpan?[] backoffTimes;

    public DefaultRetryPolicy()
    {
        this.backoffTimes = DefaultBackoffTimes;
    }

    public DefaultRetryPolicy(TimeSpan?[] customBackoffTimes)
    {
        this.backoffTimes = customBackoffTimes;
    }

    public TimeSpan? NextRetryDelay(RetryContext context)
    {
        if (context.PreviousRetryCount >= this.backoffTimes.Length)
            return null;

        return this.backoffTimes[context.PreviousRetryCount];
    }
}