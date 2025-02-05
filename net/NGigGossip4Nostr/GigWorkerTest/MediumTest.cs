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
using GigGossipSettlerAPIClient;
using System.Net.Http;
using Microsoft.AspNetCore.SignalR.Client;
using NetworkClientToolkit;
using System.Text.Json;
using GigDebugLoggerAPIClient;
using GigGossip;

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

    LogWrapper<MediumTest> TRACE = FlowLoggerFactory.Trace<MediumTest>();

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
            var gtok = SettlerAPIResult.Get<Guid>(await settlerClient.GetTokenAsync(settlerPubKey.AsHex(),CancellationToken.None));
            var token = AuthToken.Create(settlerPrivKey, DateTime.UtcNow, gtok);
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
                var balanceOfCustomer = WalletAPIResult.Get<AccountBalanceDetails>(await node.GetWalletClient().GetBalanceAsync(await node.MakeWalletAuthToken(), CancellationToken.None)).AvailableAmount;
                if (balanceOfCustomer < minAmout)
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
                if (WalletAPIResult.Get<AccountBalanceDetails>(await customer.GetWalletClient().GetBalanceAsync(await customer.MakeWalletAuthToken(), CancellationToken.None)).AvailableAmount >= minAmount)
                {
                outer:
                    foreach (var node in gossipers)
                        if (WalletAPIResult.Get<AccountBalanceDetails>(await node.GetWalletClient().GetBalanceAsync(await node.MakeWalletAuthToken(), CancellationToken.None)).AvailableAmount < minAmount)
                        {
                            Thread.Sleep(1000);
                            goto outer;
                        }
                    break;
                }
            } while (true);

            gigWorker.UpdateContact( gossipers[0].PublicKey, DateTime.Now);
            gossipers[0].UpdateContact( gigWorker.PublicKey, DateTime.Now);

            for (int i = 0; i < gossipers.Count; i++)
                for (int j = 0; j < gossipers.Count; j++)
                {
                    if (i == j)
                        continue;
                    gossipers[i].UpdateContact(gossipers[j].PublicKey, DateTime.Now);
                }

            customer.UpdateContact(gossipers[gossipers.Count-1].PublicKey, DateTime.Now);
            gossipers[gossipers.Count - 1].UpdateContact(customer.PublicKey, DateTime.Now);

            {
                var fromGh = GeoHash.Encode(latitude: 42.6, longitude: -5.6, numberOfChars: 7);
                var toGh = GeoHash.Encode(latitude: 42.5, longitude: -5.6, numberOfChars: 7);

                await customer.BroadcastTopicAsync(new RideShareTopic()
                {
                    FromGeohash = fromGh,
                    ToGeohash = toGh,
                    PickupAfter = DateTime.UtcNow.AsUnixTimestamp(),
                    PickupBefore = DateTime.UtcNow.AddMinutes(20).AsUnixTimestamp()
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
            if (broadcastFrame.JobRequest.Header.Topic.ValueCase == JobTopic.ValueOneofCase.RideShare)
            {
                var taxiTopic = broadcastFrame.JobRequest.Header.Topic.RideShare;
                if (taxiTopic.FromGeohash.Length >= 7 &&
                    taxiTopic.ToGeohash.Length >= 7 &&
                    taxiTopic.PickupBefore.AsUtcDateTime() >= DateTime.UtcNow)
                {
                    await me.BroadcastToPeersAsync(peerPublicKey, broadcastFrame);
                }
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

    LogWrapper<GigWorkerGossipNodeEvents> TRACE = FlowLoggerFactory.Trace<GigWorkerGossipNodeEvents>();
    public required Uri SettlerUri;
    public required long FeeLimit;

    public async void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, BroadcastFrame broadcastFrame)
    {
        using var TL = TRACE.Log().Args(me, peerPublicKey, broadcastFrame);
        try
        {
            if (broadcastFrame.JobRequest.Header.Topic.ValueCase == JobTopic.ValueOneofCase.RideShare)
            {
                var taxiTopic = broadcastFrame.JobRequest.Header.Topic.RideShare;
                await me.AcceptBroadcastAsync( peerPublicKey, broadcastFrame,
                    new AcceptBroadcastResponse()
                    {
                        Properties = new string[] { "drive"},
                        Reply =   new Reply
                        {
                            RideShare = new RideShareReply
                            {
                                PublicKey = me.PublicKey.AsPublicKey()
                            }
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

    public async void OnNewResponse(GigGossipNode me, JobReply replyPayload, string replyInvoice, PaymentRequestRecord decodedReplyInvoice, string networkInvoice, PaymentRequestRecord decodedNetworkInvoice)
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
    public required MediumTest Test;
    public required long FeeLimit;

    LogWrapper<CustomerGossipNodeEvents> TRACE = FlowLoggerFactory.Trace<CustomerGossipNodeEvents>();

    public void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, BroadcastFrame broadcastFrame)
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

    Timer timer=null;
    int old_cnt = 0;

    public async void OnNewResponse(GigGossipNode me, JobReply replyPayload, string replyInvoice, PaymentRequestRecord decodedReplyInvoice, string networkInvoice, PaymentRequestRecord decodedNetworkInvoice)
    {
        using var TL = TRACE.Log().Args(me, replyPayload, replyInvoice, decodedReplyInvoice, networkInvoice, decodedNetworkInvoice);
        try
        {
            lock (this)
            {
                if (timer == null)
                    timer = new Timer(async (o) => {
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
                    },null,5000, Timeout.Infinite);
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

    public void OnReplyInvoiceSettled(GigGossipNode me, Uri serviceUri, string paymentHash, string preimage)
    {
        using var TL = TRACE.Log().Args(me, serviceUri, paymentHash, preimage);
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