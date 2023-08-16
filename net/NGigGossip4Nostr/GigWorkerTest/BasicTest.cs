using System;
using NGigGossip4Nostr;
using System.Diagnostics;
using System.Text;
using CryptoToolkit;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using NBitcoin.Secp256k1;

namespace GigWorkerTest;

public class BasicTest
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
    NodeSettings gigWorkerSettings, customerSettings;

    public BasicTest(string[] args)
    {
        this.args = args;
        var config = GetConfigurationRoot(".giggossip", "basictest.conf");
        gigWorkerSettings = config.GetSection("gigworker").Get<NodeSettings>();
        customerSettings = config.GetSection("customer").Get<NodeSettings>();
    }


    HttpClient httpClient = new HttpClient();
    SimpleSettlerSelector settlerSelector = new SimpleSettlerSelector();
    Customer customer;

    public async Task Run()
    {
        var gigWorker = new GigWorker(
            Context.Instance.CreateECPrivKey(Convert.FromHexString(gigWorkerSettings.PrivateKey)),
            gigWorkerSettings.GetNostrRelays()
            );

        await gigWorker.Init(
            gigWorkerSettings.PriceAmountForRouting,
            TimeSpan.FromMilliseconds(gigWorkerSettings.BroadcastConditionsTimeoutMs),
            gigWorkerSettings.BroadcastConditionsPowScheme,
            gigWorkerSettings.BroadcastConditionsPowComplexity,
            TimeSpan.FromMilliseconds(gigWorkerSettings.TimestampToleranceMs),
            TimeSpan.FromSeconds(gigWorkerSettings.InvoicePaymentTimeoutSec),
            gigWorkerSettings.GetLndWalletClient(httpClient),
            settlerSelector);

        await gigWorker.GenerateMyCert(gigWorkerSettings.SettlerOpenApi);

        customer = new Customer(
            Context.Instance.CreateECPrivKey(Convert.FromHexString(customerSettings.PrivateKey)),
            customerSettings.GetNostrRelays()
            );

        await customer.Init(
            customerSettings.PriceAmountForRouting,
            TimeSpan.FromMilliseconds(customerSettings.BroadcastConditionsTimeoutMs),
            customerSettings.BroadcastConditionsPowScheme,
            customerSettings.BroadcastConditionsPowComplexity,
            TimeSpan.FromMilliseconds(customerSettings.TimestampToleranceMs),
            TimeSpan.FromSeconds(customerSettings.InvoicePaymentTimeoutSec),
            customerSettings.GetLndWalletClient(httpClient),
            settlerSelector);

        await customer.GenerateMyCert(customerSettings.SettlerOpenApi);

         gigWorker.Start();
         customer.Start();

        gigWorker.AddContact(new NostrContact() { PublicKey = customer.PublicKey, Petname = "Customer", Relay = "" });
        customer.AddContact(new NostrContact() { PublicKey = gigWorker.PublicKey, Petname = "GigWorker", Relay = "" });

        customer.OnNewResponse += Customer_OnNewResponse;
        customer.OnResponseReady += Customer_OnResponseReady;


        customer.Go();

        while(true)
        {
            Thread.Sleep(1000);
        }

    }


    private void Customer_OnNewResponse(object? sender, NewResponseEventArgs e)
    {
        (sender as GigGossipNode).AcceptResponse(e.Payload, e.NetworkInvoice);
    }
    // var settler =  settlerSelector.GetSettlerClient(customerSettings.SettlerOpenApi);
    // var token = settlerSelector.GetTokenAsync(customer.PublicKey);
    // var key = settler.RevealSymmetricKeyAsync(customer.PublicKey, token, customer.topicId);

    private async void Customer_OnResponseReady(object? sender, ResponseReadyEventArgs e)
    {
       var message = Crypto.SymmetricDecrypt<byte[]>(Convert.FromHexString(e.SymmetricKey), e.Payload.EncryptedReplyMessage);
       Trace.TraceInformation(Encoding.Default.GetString(message));
    }
}

public class NodeSettings
{
    public Uri GigWalletOpenApi { get; set; }
    public string NostrRelays { get; set; }
    public string PrivateKey { get; set; }
    public Uri SettlerOpenApi { get; set; }
    public long PriceAmountForRouting { get; set; }
    public long BroadcastConditionsTimeoutMs { get; set; }
    public string BroadcastConditionsPowScheme { get; set; }
    public int BroadcastConditionsPowComplexity { get; set; }
    public long TimestampToleranceMs { get; set; }
    public long InvoicePaymentTimeoutSec { get; set; }

    public string[] GetNostrRelays()
    {
        return (from s in JsonArray.Parse(NostrRelays).AsArray() select s.GetValue<string>()).ToArray();
    }

    public GigLNDWalletAPIClient.swaggerClient GetLndWalletClient(HttpClient httpClient)
    {
        return new GigLNDWalletAPIClient.swaggerClient(GigWalletOpenApi.AbsoluteUri, httpClient);
    }
}