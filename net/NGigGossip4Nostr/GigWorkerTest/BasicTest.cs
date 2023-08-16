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
    SettlerAdminSettings settlerAdminSettings;

    public BasicTest(string[] args)
    {
        this.args = args;
        var config = GetConfigurationRoot(".giggossip", "basictest.conf");
        gigWorkerSettings = config.GetSection("gigworker").Get<NodeSettings>();
        customerSettings = config.GetSection("customer").Get<NodeSettings>();
        settlerAdminSettings = config.GetSection("settleradmin").Get<SettlerAdminSettings>();
    }


    HttpClient httpClient = new HttpClient();
    SimpleSettlerSelector settlerSelector = new SimpleSettlerSelector();

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

        public void OnNewResponse(GigGossipNode me, ReplyPayload replyPayload, string networkInvoice)
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
                me.AcceptBraodcast(peerPublicKey, broadcastFrame,
                    new AcceptBroadcastResponse()
                    {
                        Message = Encoding.Default.GetBytes($"mynameis={me.PublicKey}"),
                        Fee = 4321,
                        SettlerServiceUri = settlerUri,
                        MyCertificate = selectedCertificate
                    });
            }
        }

        public void OnNewResponse(GigGossipNode me, ReplyPayload replyPayload, string networkInvoice)
        {
        }

        public void OnResponseReady(GigGossipNode me, ReplyPayload replyPayload, string key)
        {
        }
    }

    public class CustomerGossipNodeEvents : IGigGossipNodeEvents
    {
        public CustomerGossipNodeEvents()
        {
        }

        public void OnAcceptBroadcast(GigGossipNode me, string peerPublicKey, POWBroadcastFrame broadcastFrame)
        {
        }

        public void OnNewResponse(GigGossipNode me, ReplyPayload replyPayload, string networkInvoice)
        {
            me.AcceptResponse(replyPayload, networkInvoice);
        }

        public void OnResponseReady(GigGossipNode me, ReplyPayload replyPayload, string key)
        {
            var message = Crypto.SymmetricDecrypt<byte[]>(
                Convert.FromHexString(key),
                replyPayload.EncryptedReplyMessage);
            Trace.TraceInformation(Encoding.Default.GetString(message));
        }
    }

    public async Task Run()
    {
        var settlerPrivKey = Context.Instance.CreateECPrivKey(Convert.FromHexString(settlerAdminSettings.PrivateKey));
        var settlerPubKey = settlerPrivKey.CreateXOnlyPubKey();
        var settlerClient = settlerSelector.GetSettlerClient(settlerAdminSettings.SettlerOpenApi);
        var gtok = await settlerClient.GetTokenAsync(settlerPubKey.AsHex());
        var token = Crypto.MakeSignedTimedToken(settlerPrivKey, DateTime.Now, gtok);
        var val = Convert.ToBase64String(Encoding.Default.GetBytes("ok"));

        var gigWorker = new GigGossipNode(
            Context.Instance.CreateECPrivKey(Convert.FromHexString(gigWorkerSettings.PrivateKey)),
            gigWorkerSettings.GetNostrRelays()
            );

        await settlerClient.GiveUserPropertyAsync(
                gigWorker.PublicKey, token,
                "drive", val,
                (DateTime.Now + TimeSpan.FromDays(1)).ToLongDateString()
             );

        var gigWorkerCert = Crypto.DeserializeObject<Certificate>(
            await settlerClient.IssueCertificateAsync(
                gigWorker.PublicKey, token, new List<string> { "drive" }));

        var customer = new GigGossipNode(
            Context.Instance.CreateECPrivKey(Convert.FromHexString(customerSettings.PrivateKey)),
            customerSettings.GetNostrRelays()
            );

        await settlerClient.GiveUserPropertyAsync(
            customer.PublicKey, token,
            "ride", val,
            (DateTime.Now + TimeSpan.FromDays(1)).ToLongDateString()
         );

        var customerCert = Crypto.DeserializeObject<Certificate>(
            await settlerClient.IssueCertificateAsync(
                customer.PublicKey, token, new List<string> { "ride" }));


        await gigWorker.Init(
            gigWorkerSettings.PriceAmountForRouting,
            TimeSpan.FromMilliseconds(gigWorkerSettings.BroadcastConditionsTimeoutMs),
            gigWorkerSettings.BroadcastConditionsPowScheme,
            gigWorkerSettings.BroadcastConditionsPowComplexity,
            TimeSpan.FromMilliseconds(gigWorkerSettings.TimestampToleranceMs),
            TimeSpan.FromSeconds(gigWorkerSettings.InvoicePaymentTimeoutSec),
            gigWorkerSettings.GetLndWalletClient(httpClient),
            settlerSelector);

        //await gigWorker.LoadCertificates(gigWorkerSettings.SettlerOpenApi);

        await customer.Init(
            customerSettings.PriceAmountForRouting,
            TimeSpan.FromMilliseconds(customerSettings.BroadcastConditionsTimeoutMs),
            customerSettings.BroadcastConditionsPowScheme,
            customerSettings.BroadcastConditionsPowComplexity,
            TimeSpan.FromMilliseconds(customerSettings.TimestampToleranceMs),
            TimeSpan.FromSeconds(customerSettings.InvoicePaymentTimeoutSec),
            customerSettings.GetLndWalletClient(httpClient),
            settlerSelector);

        //await customer.LoadCertificates(customerSettings.SettlerOpenApi);

        gigWorker.Start(new GigWorkerGossipNodeEvents(gigWorkerSettings.SettlerOpenApi, gigWorkerCert));
        customer.Start(new CustomerGossipNodeEvents());

        gigWorker.AddContact(new NostrContact() { PublicKey = customer.PublicKey, Petname = "Customer", Relay = "" });
        customer.AddContact(new NostrContact() { PublicKey = gigWorker.PublicKey, Petname = "GigWorker", Relay = "" });

        {
            var fromGh = GeoHash.Encode(latitude: 42.6, longitude: -5.6, numberOfChars: 7);
            var toGh = GeoHash.Encode(latitude: 42.5, longitude: -5.6, numberOfChars: 7);

            customer.BroadcastTopic(new TaxiTopic()
            {
                FromGeohash = fromGh,
                ToGeohash = toGh,
                PickupAfter = DateTime.Now,
                DropoffBefore = DateTime.Now.AddMinutes(20)
            },
            customerCert);

        }

        while (true)
        {
            Thread.Sleep(1000);
        }

    }
}

public class SettlerAdminSettings
{
    public Uri SettlerOpenApi { get; set; }
    public string PrivateKey { get; set; }
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