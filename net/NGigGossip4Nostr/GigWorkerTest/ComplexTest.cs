using System;
using NGigGossip4Nostr;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using NBitcoin.Secp256k1;
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
using System.Text.Json;
using GigDebugLoggerAPIClient;
using GigGossip;

namespace GigWorkerComplexTest;

public static class MainThreadControl
{
    public static int Counter { get; set; } = 0;
    public static object Ctrl = new object();
}

public class ComplexTest
{

    LogWrapper<ComplexTest> TRACE = FlowLoggerFactory.Trace<ComplexTest>();
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
        using var TL = TRACE.Log();
        try
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
            var token = AuthToken.Create(settlerPrivKey, DateTime.UtcNow, gtok);
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

                    things[node_name].UpdateContact(things[node_name_1].PublicKey, DateTime.UtcNow);
                    things[node_name_1].UpdateContact(things[node_name].PublicKey, DateTime.UtcNow);
                    already.Add(node_name + ":" + node_name_1);
                    already.Add(node_name_1 + ":" + node_name);
                    Console.WriteLine(node_name + "<->" + node_name_1);
                }
            }

            async Task TopupNodeAsync(GigGossipNode node, long minAmout,long topUpAmount)
            {
                var balanceOfCustomer = WalletAPIResult.Get<AccountBalanceDetails>(await node.GetWalletClient().GetBalanceAsync(await node.MakeWalletAuthToken(), CancellationToken.None)).AvailableAmount;
                if (balanceOfCustomer < minAmout)
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
                    if (WalletAPIResult.Get<AccountBalanceDetails>(await node.GetWalletClient().GetBalanceAsync(await node.MakeWalletAuthToken(), CancellationToken.None)).AvailableAmount < minAmount)
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

                await customer.BroadcastTopicAsync(new RideShareTopic()
                {
                    FromGeohash = fromGh,
                    ToGeohash = toGh,
                    PickupAfter = DateTime.UtcNow.AsUnixTimestamp(),
                    PickupBefore = DateTime.UtcNow.AddMinutes(20).AsUnixTimestamp(),
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
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }
}


public class NetworkEarnerNodeEvents : IGigGossipNodeEvents
{
    public required long FeeLimit;

    LogWrapper<NetworkEarnerNodeEvents> TRACE = FlowLoggerFactory.Trace<NetworkEarnerNodeEvents>();

    public async void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, BroadcastFrame broadcastFrame)
    {
        using var TL = TRACE.Log().Args(me, peerPublicKey, broadcastFrame);
        try
        {
            if (broadcastFrame.JobRequest.Header.Topic.ValueCase ==  JobTopic.ValueOneofCase.RideShare)
            {
                var taxiTopic = broadcastFrame.JobRequest.Header.Topic.RideShare;
                if (taxiTopic.FromGeohash.Length >= 7 &&
                    taxiTopic.ToGeohash.Length >= 7 &&
                    taxiTopic.PickupBefore.AsUtcDateTime() >= DateTime.UtcNow)
                {
                    await me.BroadcastToPeersAsync(peerPublicKey, broadcastFrame);
                    TL.NewNote(me.PublicKey, "BroadcastToPeers");
                }
            }
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }


    public async void OnJobInvoiceSettled(GigGossipNode me, InvoiceData iac)
    {
        using var TL = TRACE.Log().Args(me, iac);
        try
        {
            TL.NewMessage(me.PublicKey, iac.PaymentHash, "InvoiceSettled");
            lock (MainThreadControl.Ctrl)
            {
                MainThreadControl.Counter--;
                Monitor.PulseAll(MainThreadControl.Ctrl);
            }
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async void OnNetworkInvoiceSettled(GigGossipNode me, InvoiceData iac)
    {
        using var TL = TRACE.Log().Args(me, iac);
        try
        {
            TL.NewMessage(me.PublicKey, iac.PaymentHash, "NetworkInvoiceSettled");
            lock (MainThreadControl.Ctrl)
            {
                MainThreadControl.Counter--;
                Monitor.PulseAll(MainThreadControl.Ctrl);
            }
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async void OnNetworkInvoiceAccepted(GigGossipNode me, InvoiceData iac)
    {
        using var TL = TRACE.Log().Args(me, iac);
        try
        {
            var paymentResult = await me.PayInvoiceAsync(iac.Invoice, iac.PaymentHash, FeeLimit, CancellationToken.None);
            if (paymentResult != LNDWalletErrorCode.Ok)
            {
                Console.WriteLine(paymentResult);
            }
            lock (MainThreadControl.Ctrl)
            {
                MainThreadControl.Counter++;
                Monitor.PulseAll(MainThreadControl.Ctrl);
            }
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnNewResponse(GigGossipNode me, JobReply replyPayload, string replyInvoice, PaymentRequestRecord decodedReplyInvoice, string networkInvoice, PaymentRequestRecord decodedNetworkInvoice)
    {
        using var TL = TRACE.Log().Args(me, replyPayload, replyInvoice, decodedReplyInvoice, networkInvoice, decodedNetworkInvoice);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }
    public void OnResponseReady(GigGossipNode me, JobReply replyPayload, string key)
    {
        using var TL = TRACE.Log().Args(me, replyPayload, key);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }
    public void OnResponseCancelled(GigGossipNode me, JobReply replyPayload)
    {
        using var TL = TRACE.Log().Args(me, replyPayload);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnPaymentStatusChange(GigGossipNode me, PaymentStatus status, PaymentData paydata)
    {
        using var TL = TRACE.Log().Args(me, status, paydata);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnCancelBroadcast(GigGossipNode me, string peerPublicKey, CancelBroadcastFrame broadcastFrame)
    {
        using var TL = TRACE.Log().Args(me, peerPublicKey, broadcastFrame);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }
    public void OnNetworkInvoiceCancelled(GigGossipNode me, InvoiceData iac)
    {
        using var TL = TRACE.Log().Args(me, iac);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnJobInvoiceAccepted(GigGossipNode me, InvoiceData iac)
    {
        using var TL = TRACE.Log().Args(me, iac);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnJobInvoiceCancelled(GigGossipNode me, InvoiceData iac)
    {
        using var TL = TRACE.Log().Args(me, iac);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnNewContact(GigGossipNode me, string pubkey)
    {
        using var TL = TRACE.Log().Args(me, pubkey);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnSettings(GigGossipNode me, string settings)
    {
        using var TL = TRACE.Log().Args(me, settings);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnEoseArrived(GigGossipNode me)
    {
        using var TL = TRACE.Log().Args(me);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnServerConnectionState(GigGossipNode me, ServerConnectionSource source, ServerConnectionState state, Uri uri)
    {
        using var TL = TRACE.Log().Args(me, source, state, uri);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnLNDInvoiceStateChanged(GigGossipNode me, InvoiceStateChange invoice)
    {
        using var TL = TRACE.Log().Args(me, invoice);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnLNDPaymentStatusChanged(GigGossipNode me, PaymentStatusChanged payment)
    {
        using var TL = TRACE.Log().Args(me, payment);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnLNDNewTransaction(GigGossipNode me, NewTransactionFound newTransaction)
    {
        using var TL = TRACE.Log().Args(me, newTransaction);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnLNDPayoutStateChanged(GigGossipNode me, PayoutStateChanged payout)
    {
        using var TL = TRACE.Log().Args(me, payout);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

}

public class GigWorkerGossipNodeEvents : IGigGossipNodeEvents
{
    public required Uri SettlerUri;
    public required long FeeLimit;

    LogWrapper<GigWorkerGossipNodeEvents> TRACE = FlowLoggerFactory.Trace<GigWorkerGossipNodeEvents>();

    public async void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, BroadcastFrame broadcastFrame)
    {
        using var TL = TRACE.Log().Args(me, peerPublicKey, broadcastFrame);
        try
        {
            if (broadcastFrame.JobRequest.Header.Topic.ValueCase == JobTopic.ValueOneofCase.RideShare)
            {
                var taxiTopic = broadcastFrame.JobRequest.Header.Topic.RideShare;
                await me.AcceptBroadcastAsync(peerPublicKey, broadcastFrame,
                    new AcceptBroadcastResponse()
                    {
                        Properties = new string[] { "drive" },
                        RideShareReply = new RideShareReply
                        {
                            PublicKey = me.PublicKey.AsPublicKey()
                        },
                        Fee = 4321,
                        Country = "PL",
                        Currency = "BTC",
                        SettlerServiceUri = SettlerUri,
                    }, async (_) => { },
                    CancellationToken.None);
                TL.NewNote(me.PublicKey, "AcceptBraodcast");
            }
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }

    }

    public async void OnNetworkInvoiceAccepted(GigGossipNode me, InvoiceData iac)
    {
        using var TL = TRACE.Log().Args(me, iac);
        try
        {
            var paymentResult = await me.PayInvoiceAsync(iac.Invoice,iac.PaymentHash, FeeLimit, CancellationToken.None);
            if (paymentResult != LNDWalletErrorCode.Ok)
            {
                Console.WriteLine(paymentResult);
            }
            lock (MainThreadControl.Ctrl)
            {
                MainThreadControl.Counter++;
                Monitor.PulseAll(MainThreadControl.Ctrl);
            }
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async void OnJobInvoiceSettled(GigGossipNode me, InvoiceData iac)
    {
        using var TL = TRACE.Log().Args(me, iac);
        try
        {
            TL.NewMessage(me.PublicKey, iac.PaymentHash, "InvoiceSettled");
            lock (MainThreadControl.Ctrl)
            {
                MainThreadControl.Counter--;
                Monitor.PulseAll(MainThreadControl.Ctrl);
            }
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async void OnNetworkInvoiceSettled(GigGossipNode me, InvoiceData iac)
    {
        using var TL = TRACE.Log().Args(me, iac);
        try
        {
            TL.NewMessage(me.PublicKey, iac.PaymentHash, "NetworkInvoiceSettled");
            lock (MainThreadControl.Ctrl)
            {
                MainThreadControl.Counter--;
                Monitor.PulseAll(MainThreadControl.Ctrl);
            }
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnNewResponse(GigGossipNode me, JobReply replyPayload, string replyInvoice, PaymentRequestRecord decodedReplyInvoice, string networkInvoice, PaymentRequestRecord decodedNetworkInvoice)
    {
        using var TL = TRACE.Log().Args(me, replyPayload, replyInvoice, decodedReplyInvoice, networkInvoice, decodedNetworkInvoice);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }
    public void OnResponseReady(GigGossipNode me, JobReply replyPayload, string key)
    {
        using var TL = TRACE.Log().Args(me, replyPayload, key);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }
    public void OnResponseCancelled(GigGossipNode me, JobReply replyPayload)
    {
        using var TL = TRACE.Log().Args(me, replyPayload);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnPaymentStatusChange(GigGossipNode me, PaymentStatus status, PaymentData paydata)
    {
        using var TL = TRACE.Log().Args(me, status, paydata);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
        
    }

    public void OnCancelBroadcast(GigGossipNode me, string peerPublicKey, CancelBroadcastFrame broadcastFrame)
    {
        using var TL = TRACE.Log().Args(me, peerPublicKey, broadcastFrame);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }
    public void OnNetworkInvoiceCancelled(GigGossipNode me, InvoiceData iac)
    {
        using var TL = TRACE.Log().Args(me, iac);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnJobInvoiceAccepted(GigGossipNode me, InvoiceData iac)
    {
        using var TL = TRACE.Log().Args(me, iac);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnJobInvoiceCancelled(GigGossipNode me, InvoiceData iac)
    {
        using var TL = TRACE.Log().Args(me, iac);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnNewContact(GigGossipNode me, string pubkey)
    {
        using var TL = TRACE.Log().Args(me, pubkey);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnSettings(GigGossipNode me, string settings)
    {
        using var TL = TRACE.Log().Args(me, settings);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnEoseArrived(GigGossipNode me)
    {
        using var TL = TRACE.Log().Args(me);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnServerConnectionState(GigGossipNode me, ServerConnectionSource source, ServerConnectionState state, Uri uri)
    {
        using var TL = TRACE.Log().Args(me, source, state, uri);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnLNDInvoiceStateChanged(GigGossipNode me, InvoiceStateChange invoice)
    {
        using var TL = TRACE.Log().Args(me, invoice);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnLNDPaymentStatusChanged(GigGossipNode me, PaymentStatusChanged payment)
    {
        using var TL = TRACE.Log().Args(me, payment);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnLNDNewTransaction(GigGossipNode me, NewTransactionFound newTransaction)
    {
        using var TL = TRACE.Log().Args(me, newTransaction);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnLNDPayoutStateChanged(GigGossipNode me, PayoutStateChanged payout)
    {
        using var TL = TRACE.Log().Args(me, payout);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }
}

public class CustomerGossipNodeEvents : IGigGossipNodeEvents
{

    LogWrapper<CustomerGossipNodeEvents> TRACE = FlowLoggerFactory.Trace<CustomerGossipNodeEvents>();
    public required long FeeLimit;

    public void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, BroadcastFrame broadcastFrame)
    {
    }

    Timer timer = null;
    int old_cnt = 0;

    public void OnNewResponse(GigGossipNode me, JobReply replyPayload, string replyInvoice, PaymentRequestRecord decodedReplyInvoice, string networkInvoice, PaymentRequestRecord decodedNetworkInvoice)
    {
        using var TL = TRACE.Log().Args(me, replyPayload, replyInvoice, decodedReplyInvoice, networkInvoice, decodedNetworkInvoice);
        try
        {
            lock (this)
            {
                if (timer == null)
                    timer = new Timer(async (o) =>
                    {
                        timer.Change(Timeout.Infinite, Timeout.Infinite);
                        var resps = me.GetReplyPayloads(replyPayload.Header.JobRequest.Header.JobRequestId.AsGuid());
                        if (resps.Count == old_cnt)
                        {
                            resps.Sort((a, b) => (int)(JsonSerializer.Deserialize<PaymentRequestRecord>(new MemoryStream(a.DecodedNetworkInvoice)).Amount - JsonSerializer.Deserialize<PaymentRequestRecord>(new MemoryStream(b.DecodedNetworkInvoice)).Amount));
                            var win = resps[0];

                            var balance = WalletAPIResult.Get<AccountBalanceDetails>(await me.GetWalletClient().GetBalanceAsync(await me.MakeWalletAuthToken(), CancellationToken.None)).AvailableAmount;

                            LNDWalletErrorCode paymentResult = LNDWalletErrorCode.Ok;

                            if (balance < decodedReplyInvoice.Amount + decodedNetworkInvoice.Amount + FeeLimit * 2)
                            {
                                paymentResult = LNDWalletErrorCode.NotEnoughFunds;
                            }
                            else
                            {
                                var networkPayState = await me.PayInvoiceAsync(networkInvoice, decodedNetworkInvoice.PaymentHash, FeeLimit, CancellationToken.None);
                                if (networkPayState != LNDWalletErrorCode.Ok)
                                    paymentResult = networkPayState;
                                else
                                {
                                    var replyPayState = await me.PayInvoiceAsync(replyInvoice, decodedReplyInvoice.PaymentHash, FeeLimit, CancellationToken.None);
                                    if (replyPayState != LNDWalletErrorCode.Ok)
                                        paymentResult = replyPayState;
                                }
                            }


                            if (paymentResult != LNDWalletErrorCode.Ok)
                            {
                                Console.WriteLine(paymentResult);
                                return;
                            }
                            TL.NewNote(me.PublicKey, "AcceptResponse");
                        }
                        else
                        {
                            old_cnt = resps.Count;
                            timer.Change(5000, Timeout.Infinite);
                        }
                    }, null, 5000, Timeout.Infinite);
            }
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async void OnResponseReady(GigGossipNode me, JobReply replyPayload, string key)
    {
        using var TL = TRACE.Log().Args(me, replyPayload, key);
        try
        {
            var reply = replyPayload.Header.EncryptedReply.Decrypt<Reply>(key.AsBytes());
            Trace.TraceInformation(reply.RideShare.Message);
            TL.NewNote(me.PublicKey, "OnResponseReady");
            TL.NewConnected(reply.RideShare.PublicKey.AsHex(), me.PublicKey, "connected");
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }
    public void OnResponseCancelled(GigGossipNode me, JobReply replyPayload)
    {
        using var TL = TRACE.Log().Args(me, replyPayload);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnNetworkInvoiceAccepted(GigGossipNode me, InvoiceData iac)
    {
        using var TL = TRACE.Log().Args(me, iac);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }


    public async void OnJobInvoiceSettled(GigGossipNode me, InvoiceData iac)
    {
        using var TL = TRACE.Log().Args(me, iac);
        try
        {

        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async void OnNetworkInvoiceSettled(GigGossipNode me, InvoiceData iac)
    {
        using var TL = TRACE.Log().Args(me, iac);
        try
        {

        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }
    public void OnPaymentStatusChange(GigGossipNode me, PaymentStatus status, PaymentData paydata)
    {
        using var TL = TRACE.Log().Args(me, status, paydata);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnCancelBroadcast(GigGossipNode me, string peerPublicKey, CancelBroadcastFrame broadcastFrame)
    {
        using var TL = TRACE.Log().Args(me, peerPublicKey, broadcastFrame);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }
    public void OnNetworkInvoiceCancelled(GigGossipNode me, InvoiceData iac)
    {
        using var TL = TRACE.Log().Args(me, iac);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnJobInvoiceAccepted(GigGossipNode me, InvoiceData iac)
    {
        using var TL = TRACE.Log().Args(me, iac);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnJobInvoiceCancelled(GigGossipNode me, InvoiceData iac)
    {
        using var TL = TRACE.Log().Args(me, iac);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnNewContact(GigGossipNode me, string pubkey)
    {
        using var TL = TRACE.Log().Args(me, pubkey);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnSettings(GigGossipNode me, string settings)
    {
        using var TL = TRACE.Log().Args(me, settings);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnEoseArrived(GigGossipNode me)
    {
        using var TL = TRACE.Log().Args(me);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnServerConnectionState(GigGossipNode me, ServerConnectionSource source, ServerConnectionState state, Uri uri)
    {
        using var TL = TRACE.Log().Args(me, source, state, uri);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnLNDInvoiceStateChanged(GigGossipNode me, InvoiceStateChange invoice)
    {
        using var TL = TRACE.Log().Args(me, invoice);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnLNDPaymentStatusChanged(GigGossipNode me, PaymentStatusChanged payment)
    {
        using var TL = TRACE.Log().Args(me, payment);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnLNDNewTransaction(GigGossipNode me, NewTransactionFound newTransaction)
    {
        using var TL = TRACE.Log().Args(me, newTransaction);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnLNDPayoutStateChanged(GigGossipNode me, PayoutStateChanged payout)
    {
        using var TL = TRACE.Log().Args(me, payout);
        try
        {
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
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