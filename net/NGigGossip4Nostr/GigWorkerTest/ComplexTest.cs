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
using NBitcoin.Protocol;
using GigGossipSettlerAPIClient;
using System.Net.Http;
using Microsoft.AspNetCore.SignalR.Client;
using NetworkClientToolkit;

namespace GigWorkerComplexTest;

public static class MainThreadControl
{
    public static int Counter { get; set; } = 0;
    public static object Ctrl = new object();
}

public class ComplexTest
{

    NodeSettings gridNodeSettings;
    SettlerAdminSettings settlerAdminSettings;
    BitcoinSettings bitcoinSettings;
    ApplicationSettings applicationSettings;

    public ComplexTest(IConfigurationRoot config)
    {
        gridNodeSettings = config.GetSection("gridnode").Get<NodeSettings>();
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
        var gtok = SettlerAPIResult.Get<Guid>(await settlerClient.GetTokenAsync(settlerPubKey.AsHex(), CancellationToken.None));
        var token = Crypto.MakeSignedTimedToken(settlerPrivKey, DateTime.UtcNow, gtok);
        var val = Convert.ToBase64String(Encoding.Default.GetBytes("ok"));

        var gridShape = applicationSettings.GetGridShape();
        var gridShapeIter = from x in gridShape select Enumerable.Range(0, x);

        var nod_name_f = (IEnumerable<int> nod_idx) => "GridNode<" + string.Join(",", nod_idx.Select(i => i.ToString()).ToList()) + ">";

        var things = new Dictionary<string, GigGossipNode>();
        foreach (var nod_idx in gridShapeIter.MultiCartesian())
        {
            var nn = nod_name_f(nod_idx);
            things[nn] = new GigGossipNode(
                gridNodeSettings.ConnectionString,
                Crypto.GeneratECPrivKey(),
                gridNodeSettings.ChunkSize,
                new DefaultRetryPolicy(),
                () => new HttpClient(),
                true,
                gridNodeSettings.LoggerOpenApi
            );
            things[nn].ClearContacts();
        }

        var rnd = new Random();
        var thingsList = new Queue<KeyValuePair<string,GigGossipNode>>(things.ToList().OrderBy(a => rnd.Next()));

        for (int i = 0; i < applicationSettings.NumMessages; i++)
        {
            var kv = thingsList.Dequeue();
            var gigWorker = kv.Value;
            SettlerAPIResult.Check(await settlerClient.GiveUserPropertyAsync(
                    token, gigWorker.PublicKey,
                    "drive", val, val,
                    24, CancellationToken.None
                 ));

            await gigWorker.StartAsync (
                gridNodeSettings.Fanout,
                gridNodeSettings.PriceAmountForRouting,
                TimeSpan.FromMilliseconds(gridNodeSettings.TimestampToleranceMs),
                TimeSpan.FromSeconds(gridNodeSettings.InvoicePaymentTimeoutSec),
                gridNodeSettings.GetNostrRelays(),
                new GigWorkerGossipNodeEvents { SettlerUri = gridNodeSettings.SettlerOpenApi, FeeLimit = gridNodeSettings.FeeLimit },
                gridNodeSettings.GigWalletOpenApi,
                gridNodeSettings.SettlerOpenApi
                );
        }

        
        var customers = new List<GigGossipNode>();
        for (int i = 0; i < applicationSettings.NumMessages; i++)
        {
            var kv = thingsList.Dequeue();
            var customer = kv.Value;
            SettlerAPIResult.Check(await settlerClient.GiveUserPropertyAsync(
                token, customer.PublicKey,
                "ride", val, val,
                24, CancellationToken.None
             ));

            await customer.StartAsync(
                gridNodeSettings.Fanout,
                gridNodeSettings.PriceAmountForRouting,
                TimeSpan.FromMilliseconds(gridNodeSettings.TimestampToleranceMs),
                TimeSpan.FromSeconds(gridNodeSettings.InvoicePaymentTimeoutSec),
                 gridNodeSettings.GetNostrRelays(),
                new CustomerGossipNodeEvents { FeeLimit = gridNodeSettings.FeeLimit },
                gridNodeSettings.GigWalletOpenApi,
                gridNodeSettings.SettlerOpenApi);
            customers.Add(customer);
        }
        foreach (var node in thingsList)
        {
            await node.Value.StartAsync(
                gridNodeSettings.Fanout,
                gridNodeSettings.PriceAmountForRouting,
                TimeSpan.FromMilliseconds(gridNodeSettings.TimestampToleranceMs),
                TimeSpan.FromSeconds(gridNodeSettings.InvoicePaymentTimeoutSec),
                gridNodeSettings.GetNostrRelays(),
                new NetworkEarnerNodeEvents { FeeLimit = gridNodeSettings.FeeLimit },
                gridNodeSettings.GigWalletOpenApi,
                gridNodeSettings.SettlerOpenApi);
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

        async Task TopupNodeAsync(GigGossipNode node, long minAmout,long topUpAmount)
        {
            var ballanceOfCustomer = WalletAPIResult.Get<long>(await node.GetWalletClient().GetBalanceAsync(await node.MakeWalletAuthToken(), CancellationToken.None));
            if (ballanceOfCustomer < minAmout)
            {
                var newBitcoinAddressOfCustomer = WalletAPIResult.Get<string>(await node.GetWalletClient().NewAddressAsync(await node.MakeWalletAuthToken(), CancellationToken.None));
                bitcoinClient.SendToAddress(NBitcoin.BitcoinAddress.Create(newBitcoinAddressOfCustomer, bitcoinSettings.GetNetwork()), new NBitcoin.Money(topUpAmount));
            }
        }

        var minAmount = 1000000;
        var topUpAmount = 10000000;

        foreach (var nod_idx in gridShapeIter.MultiCartesian())
        {
            var node_name = nod_name_f(nod_idx);
            await TopupNodeAsync(things[node_name], minAmount, topUpAmount);
        }

        bitcoinClient.Generate(10); // generate some blocks

        do
        {
        outer:
            foreach (var node in things.Values)
                if (WalletAPIResult.Get<long>(await node.GetWalletClient().GetBalanceAsync(await node.MakeWalletAuthToken(), CancellationToken.None)) < minAmount)
                {
                    Thread.Sleep(1000);
                    goto outer;
                }
            break;
        } while (true);

        foreach (var customer in customers)
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
            new string[] { "ride" },
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

        foreach (var nod_idx in gridShapeIter.MultiCartesian())
        {
            var node_name = nod_name_f(nod_idx);
            await things[node_name].StopAsync();
        }
    }
}


public class NetworkEarnerNodeEvents : IGigGossipNodeEvents
{
    public required long FeeLimit;

    public async void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, BroadcastFrame broadcastFrame)
    {
        var taxiTopic = Crypto.DeserializeObject<TaxiTopic>(broadcastFrame.SignedRequestPayload.Value.Topic);
        if (taxiTopic != null)
        {
            if (taxiTopic.FromGeohash.Length >= 7 &&
                   taxiTopic.ToGeohash.Length >= 7 &&
                   taxiTopic.DropoffBefore >= DateTime.UtcNow)
            {
                await me.BroadcastToPeersAsync(peerPublicKey, broadcastFrame);
                await me.FlowLogger.NewNoteAsync(me.PublicKey, "BroadcastToPeers");
            }
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

    public void OnNewResponse(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload, string replyInvoice, PayReqRet decodedReplyInvoice, string networkInvoice, PayReqRet decodedNetworkInvoice)
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

public class GigWorkerGossipNodeEvents : IGigGossipNodeEvents
{
    public required Uri SettlerUri;
    public required long FeeLimit;

    public async void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, BroadcastFrame broadcastFrame)
    {
        var taxiTopic = Crypto.DeserializeObject<TaxiTopic>(
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
    public void OnNewResponse(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload, string replyInvoice, PayReqRet decodedReplyInvoice, string networkInvoice, PayReqRet decodedNetworkInvoice)
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
    public required long FeeLimit;

    public void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, BroadcastFrame broadcastFrame)
    {
    }

    Timer timer = null;
    int old_cnt = 0;

    public void OnNewResponse(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload, string replyInvoice, PayReqRet decodedReplyInvoice, string networkInvoice, PayReqRet decodedNetworkInvoice)
    {
        lock (this)
        {
            if (timer == null)
                timer = new Timer(async (o) =>
                {
                    timer.Change(Timeout.Infinite, Timeout.Infinite);
                    var resps = me.GetReplyPayloads(replyPayload.Value.SignedRequestPayload.Id);
                    if (resps.Count == old_cnt)
                    {
                        resps.Sort((a, b) => (int)(Crypto.DeserializeObject<PayReqRet>(a.DecodedNetworkInvoice).ValueSat - Crypto.DeserializeObject<PayReqRet>(b.DecodedNetworkInvoice).ValueSat));
                        var win = resps[0];
                        var paymentResult = await me.AcceptResponseAsync(
                            Crypto.DeserializeObject<Certificate<ReplyPayloadValue>>(win.TheReplyPayload),
                            win.ReplyInvoice,
                            Crypto.DeserializeObject<PayReqRet>(win.DecodedReplyInvoice),
                            win.NetworkInvoice,
                            Crypto.DeserializeObject<PayReqRet>(win.DecodedNetworkInvoice),
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
                }, null, 5000, Timeout.Infinite);
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
    public required string GridShape { get; set; }
    public required int NumMessages { get; set; }
    public int[] GetGridShape()
    {
        return (from s in JsonArray.Parse(GridShape)!.AsArray() select s.GetValue<int>()).ToArray();
    }
}
public class NodeSettings
{
    public required string ConnectionString { get; set; }
    public required Uri GigWalletOpenApi { get; set; }
    public required string NostrRelays { get; set; }
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