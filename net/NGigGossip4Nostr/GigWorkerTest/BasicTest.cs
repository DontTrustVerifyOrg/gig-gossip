﻿using System;
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
using Microsoft.AspNetCore.SignalR.Client;
using NetworkClientToolkit;
using GigDebugLoggerAPIClient;

namespace GigWorkerBasicTest;

public static class MainThreadControl
{
    public static bool IsRunning { get; set; } = true;
    public static object Ctrl = new object();
}

public class BasicTest
{


    LogWrapper<BasicTest> TRACE = FlowLoggerFactory.Trace<BasicTest>();

    NodeSettings gigWorkerSettings, customerSettings;
    SettlerAdminSettings settlerAdminSettings;
    BitcoinSettings bitcoinSettings;
    ApplicationSettings applicationSettings;

    public BasicTest(IConfigurationRoot config)
    {
        gigWorkerSettings = config.GetSection("gigworker").Get<NodeSettings>();
        customerSettings = config.GetSection("customer").Get<NodeSettings>();
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
                    "drive", val,val,
                    24, CancellationToken.None
                ));

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
                new GigWorkerGossipNodeEvents { SettlerUri = gigWorkerSettings.SettlerOpenApi, FeeLimit = gigWorkerSettings.FeeLimit },
                gigWorkerSettings.GigWalletOpenApi,
                gigWorkerSettings.SettlerOpenApi);

            await customer.StartAsync(
                customerSettings.Fanout,
                customerSettings.PriceAmountForRouting,
                TimeSpan.FromMilliseconds(customerSettings.TimestampToleranceMs),
                TimeSpan.FromSeconds(customerSettings.InvoicePaymentTimeoutSec),
                customerSettings.GetNostrRelays(),
                new CustomerGossipNodeEvents { FeeLimit = customerSettings.FeeLimit},
                customerSettings.GigWalletOpenApi,
                customerSettings.SettlerOpenApi);

            var ballanceOfCustomer = WalletAPIResult.Get<long>(await customer.GetWalletClient().GetBalanceAsync(await customer.MakeWalletAuthToken(), CancellationToken.None));

            while (ballanceOfCustomer == 0)
            {
                var newBitcoinAddressOfCustomer = WalletAPIResult.Get<string>(await customer.GetWalletClient().NewAddressAsync(await customer.MakeWalletAuthToken(), CancellationToken.None));
                Console.WriteLine(newBitcoinAddressOfCustomer);

                bitcoinClient.SendToAddress(NBitcoin.BitcoinAddress.Create(newBitcoinAddressOfCustomer, bitcoinSettings.GetNetwork()), new NBitcoin.Money(10000000ul));

                bitcoinClient.Generate(10); // generate some blocks

                do
                {
                    if (WalletAPIResult.Get<long>(await customer.GetWalletClient().GetBalanceAsync(await customer.MakeWalletAuthToken(), CancellationToken.None)) > 0)
                        break;
                    Thread.Sleep(1000);
                } while (true);

                ballanceOfCustomer = WalletAPIResult.Get<long>(await customer.GetWalletClient().GetBalanceAsync(await customer.MakeWalletAuthToken(), CancellationToken.None));
            }



            gigWorker.ClearContacts();
            customer.ClearContacts();

            gigWorker.AddContact(customer.PublicKey,"Customer" );
            customer.AddContact(gigWorker.PublicKey, "GigWorker");

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

            while (MainThreadControl.IsRunning)
            {
                lock (MainThreadControl.Ctrl)
                    Monitor.Wait(MainThreadControl.Ctrl);
            }

            await gigWorker.StopAsync();
            await customer.StopAsync();
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
            var taxiTopic = Crypto.BinaryDeserializeObject<TaxiTopic>(
                broadcastFrame.SignedRequestPayload.Value.Topic);

            if (taxiTopic != null)
            {
                await me.AcceptBroadcastAsync(peerPublicKey, broadcastFrame,
                    new AcceptBroadcastResponse()
                    {
                        Properties = new string[] {"drive" },
                        Message = Encoding.Default.GetBytes(me.PublicKey),
                        Fee = 4321,
                        SettlerServiceUri = SettlerUri
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
            var paymentResult=await me.PayNetworkInvoiceAsync(iac, FeeLimit, CancellationToken.None);
            if (paymentResult != GigLNDWalletAPIErrorCode.Ok)
                Console.WriteLine(paymentResult);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnInvoiceSettled(GigGossipNode me, Uri serviceUri, string paymentHash, string preimage)
    {
        using var TL = TRACE.Log().Args(me, serviceUri, paymentHash, preimage);
        try
        {
            TL.NewNote(me.PublicKey, "InvoiceSettled");
            lock (MainThreadControl.Ctrl)
            {
                MainThreadControl.IsRunning = false;
                Monitor.PulseAll(MainThreadControl.Ctrl);
            }
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public void OnNewResponse(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload, string replyInvoice, PayReqRet decodedReplyInvoice, string networkInvoice, PayReqRet decodedNetworkInvoice)
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
    public void OnResponseReady(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload, string key)
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
    public void OnResponseCancelled(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload)
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

    public void OnPaymentStatusChange(GigGossipNode me, string status, PaymentData paydata)
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

    public void OnInvoiceAccepted(GigGossipNode me, InvoiceData iac)
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

    public void OnInvoiceCancelled(GigGossipNode me, InvoiceData iac)
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
}

public class CustomerGossipNodeEvents : IGigGossipNodeEvents
{
    LogWrapper<CustomerGossipNodeEvents> TRACE = FlowLoggerFactory.Trace<CustomerGossipNodeEvents>();
    public required long FeeLimit;

    public CustomerGossipNodeEvents()
    {
    }

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

    public void OnInvoiceSettled(GigGossipNode me, Uri serviceUri, string paymentHash, string preimage)
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

    public async void OnNewResponse(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload, string replyInvoice, PayReqRet decodedReplyInvoice, string networkInvoice, PayReqRet decodedNetworkInvoice)
    {
        using var TL = TRACE.Log().Args(me, replyPayload, replyInvoice, decodedReplyInvoice, networkInvoice, decodedNetworkInvoice);
        try
        {
            TL.NewNote(me.PublicKey, "AcceptResponse");
            var paymentResult = await me.AcceptResponseAsync(replyPayload, replyInvoice, decodedReplyInvoice, networkInvoice, decodedNetworkInvoice, FeeLimit, CancellationToken.None);
            if(paymentResult!= GigLNDWalletAPIErrorCode.Ok)
                Console.WriteLine(paymentResult);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }
    public async void OnResponseReady(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload, string key)
    {
        using var TL = TRACE.Log().Args(me, replyPayload, key);
        try
        {
            var message = Encoding.Default.GetString(Crypto.SymmetricBytesDecrypt(
            key.AsBytes(),
            replyPayload.Value.EncryptedReplyMessage));
            TL.Info(message);
            TL.NewNote(me.PublicKey, "OnResponseReady");
            TL.NewConnected(message, me.PublicKey, "connected");
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }
    public void OnResponseCancelled(GigGossipNode me, Certificate<ReplyPayloadValue> replyPayload)
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

    public void OnPaymentStatusChange(GigGossipNode me, string status, PaymentData paydata)
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

    public void OnInvoiceAccepted(GigGossipNode me, InvoiceData iac)
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

    public void OnInvoiceCancelled(GigGossipNode me, InvoiceData iac)
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
}

public class ApplicationSettings
{
    public required string FlowLoggerPath { get; set; }
}

public class SettlerAdminSettings
{
    public required Uri SettlerOpenApi { get; set; }
    public required string PrivateKey { get; set; }
}

public class NodeSettings
{
    public required string ConnectionString { get; set; }
    public required Uri GigWalletOpenApi { get; set; }
    public required string NostrRelays { get; set; }
    public required string PrivateKey { get; set; }
    public required Uri LoggerOpenApi { get; set; }
    public required Uri SettlerOpenApi { get; set; }
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