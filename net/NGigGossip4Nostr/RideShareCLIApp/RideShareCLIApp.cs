﻿using System;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Timers;
using CryptoToolkit;
using GigGossipFrames;
using GigGossipSettlerAPIClient;
using GigLNDWalletAPIClient;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using NBitcoin;
using NBitcoin.Protocol;
using NBitcoin.RPC;
using NBitcoin.Secp256k1;
using NGeoHash;
using NGigGossip4Nostr;
using ProtoBuf.Serializers;
using RideShareFrames;
using Sharprompt;
using Spectre;
using Spectre.Console;
using SQLitePCL;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;
using static NBitcoin.Scripting.OutputDescriptor;

namespace RideShareCLIApp;

public sealed class DefaultRetryPolicy : IRetryPolicy
{
    private static TimeSpan?[] DefaultBackoffTimes = new TimeSpan?[]
    {
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
//        TimeSpan.FromSeconds(10),
//        TimeSpan.FromSeconds(30),
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

public partial class RideShareCLIApp
{
    GigDebugLoggerAPIClient.LogWrapper<RideShareCLIApp> TRACE = GigDebugLoggerAPIClient.FlowLoggerFactory.Trace<RideShareCLIApp>();
    Settings settings;
    GigGossipNode gigGossipNode;
    IGigGossipNodeEventSource gigGossipNodeEventSource = new GigGossipNodeEventSource();

    bool inDriverMode = false;
    System.Timers.Timer directTimer;
    Dictionary<Guid, string> directPubkeys = new();
    string privkeypassed;

    CancellationTokenSource CancellationTokenSource = new();

    static IConfigurationRoot GetConfigurationRoot(string? basePath, string[] args, string defaultFolder, string iniName)
    {
        if (basePath == null)
        {
            basePath = Environment.GetEnvironmentVariable("GIGGOSSIP_BASEDIR");
            if (basePath == null)
                basePath = Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]);
        }
        var builder = new ConfigurationBuilder();
        builder.SetBasePath(basePath)
               .AddIniFile(iniName)
               .AddEnvironmentVariables()
               .AddCommandLine(args);

        return builder.Build();
    }

    public RideShareCLIApp(string[] args, string id, string baseDir, string sfx, string privkey)
    {
        if (id == null)
            id = AnsiConsole.Prompt(new TextPrompt<string>("Enter this node [orange1]Id[/]?"));

        if (sfx == null)
            sfx = AnsiConsole.Prompt(new TextPrompt<string>("Enter the [orange1]config suffix[/]?").AllowEmpty());

        sfx = (string.IsNullOrWhiteSpace(sfx)) ? "" : "_" + sfx;

        if (string.IsNullOrWhiteSpace(privkey))
            privkeypassed = AnsiConsole.Prompt(new TextPrompt<string>("Enter the [orange1]private key[/]?").AllowEmpty());
        else
            privkeypassed = privkey;

        IConfigurationRoot config = GetConfigurationRoot(baseDir, args, ".giggossip", "ridesharecli" + sfx + ".conf");

        this.settings = new Settings(id, config);

        SecureStorage.InitializeDefault(
            settings.NodeSettings.SecureStorageConnectionString.
            Replace("$HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).
            Replace("$ID", id));

        gigGossipNodeEventSource.OnAcceptBroadcast += GigGossipNodeEventSource_OnAcceptBroadcast;
        gigGossipNodeEventSource.OnNetworkInvoiceAccepted += GigGossipNodeEventSource_OnNetworkInvoiceAccepted;
        gigGossipNodeEventSource.OnNewResponse += GigGossipNodeEventSource_OnNewResponse;
        gigGossipNodeEventSource.OnResponseReady += GigGossipNodeEventSource_OnResponseReady;
        gigGossipNodeEventSource.OnResponseCancelled += GigGossipNodeEventSource_OnResponseCancelled;
        gigGossipNodeEventSource.OnInvoiceAccepted += GigGossipNodeEventSource_OnInvoiceAccepted;
        gigGossipNodeEventSource.OnInvoiceCancelled += GigGossipNodeEventSource_OnInvoiceCancelled;
        gigGossipNodeEventSource.OnCancelBroadcast += GigGossipNodeEventSource_OnCancelBroadcast;
        gigGossipNodeEventSource.OnNetworkInvoiceCancelled += GigGossipNodeEventSource_OnNetworkInvoiceCancelled;
        gigGossipNodeEventSource.OnPaymentStatusChange += GigGossipNodeEventSource_OnPaymentStatusChange;
        gigGossipNodeEventSource.OnInvoiceSettled += GigGossipNodeEventSource_OnInvoiceSettled;
        gigGossipNodeEventSource.OnNewContact += GigGossipNodeEventSource_OnNewContact;
        gigGossipNodeEventSource.OnServerConnectionState += GigGossipNodeEventSource_OnServerConnectionState;

    }

    private void GigGossipNodeEventSource_OnServerConnectionState(object? sender, ServerConnectionSourceStateEventArgs e)
    {
        AnsiConsole.WriteLine("ServerConnectionState " + e.Source.ToString() + " " + e.State.ToString() + " " + e.Uri?.AbsoluteUri);
    }

    private async void GigGossipNodeEventSource_OnInvoiceSettled(object? sender, InvoiceSettledEventArgs e)
    {
        AnsiConsole.WriteLine("Invoice settled");
        await WriteBalance();
    }

    private async void GigGossipNodeEventSource_OnPaymentStatusChange(object? sender, PaymentStatusChangeEventArgs e)
    {
        AnsiConsole.WriteLine("Payment " + e.Status);
        await WriteBalance();
    }

    private void GigGossipNodeEventSource_OnNetworkInvoiceCancelled(object? sender, NetworkInvoiceCancelledEventArgs e)
    {
    }

    enum SecureStorageKeysEnum
    {
        PrivateKey,
        NodeMode,
        PhoneNumber,
    }

    public enum CommandEnum
    {
        [Display(Name = "Setup My Info")]
        SetupMyInfo,
        [Display(Name = "Top up")]
        TopUp,
        [Display(Name = "New Address")]
        NewAddress,
        [Display(Name = "Enter Driver Mode")]
        DriverMode,
        [Display(Name = "Request Ride")]
        RequestRide,
        [Display(Name = "Reset")]
        Reset,
        [Display(Name = "Exit")]
        Exit,
        [Display(Name = "Enable/Disable Debug Log")]
        DebLog,
    }

    public async Task<ECPrivKey> GetPrivateKeyAsync()
    {
        return (await SecureStorage.Default.GetAsync(SecureStorageKeysEnum.PrivateKey.ToString()))?.AsECPrivKey();
    }

    public async Task<ECXOnlyPubKey> GetPublicKeyAsync()
    {
        return (await SecureStorage.Default.GetAsync(SecureStorageKeysEnum.PrivateKey.ToString()))?.AsECPrivKey().CreateXOnlyPubKey();
    }

    public async Task SetPrivateKeyAsync(ECPrivKey privKey)
    {
        await SecureStorage.Default.SetAsync(SecureStorageKeysEnum.PrivateKey.ToString(), privKey.AsHex());
    }

    public async Task<string> GetPhoneNumberAsync()
    {
        return (await SecureStorage.Default.GetAsync(SecureStorageKeysEnum.PhoneNumber.ToString()));
    }

    public async Task SetPhoneNumberAsync(string phoneNumber)
    {
        await SecureStorage.Default.SetAsync(SecureStorageKeysEnum.PhoneNumber.ToString(), phoneNumber);
    }


    public async Task RunAsync()
    {
        using var TL = TRACE.Log();
        try
        {
        
            ECPrivKey privateKey;
            if (string.IsNullOrWhiteSpace(privkeypassed))
            {
                privateKey = await GetPrivateKeyAsync();
                if (privateKey == null)
                {
                    var mnemonic = Crypto.GenerateMnemonic().Split(" ");
                    AnsiConsole.WriteLine($"Initializing private key for {settings.Id}");
                    AnsiConsole.WriteLine(string.Join(" ", mnemonic));
                    privateKey = Crypto.DeriveECPrivKeyFromMnemonic(string.Join(" ", mnemonic));
                }
            }
            else
            {
                privateKey = await GetPrivateKeyAsync();
                if (privateKey != null && privkeypassed != privateKey.AsHex())
                {
                    if (Prompt.Confirm("Private key already set to different value. Overwrite?"))
                        privateKey = privkeypassed.AsECPrivKey();
                }
                else
                    privateKey = privkeypassed.AsECPrivKey();
            }
            AnsiConsole.WriteLine($"Loading private key for {settings.Id}");
            await SetPrivateKeyAsync(privateKey);

            GigDebugLoggerAPIClient.FlowLoggerFactory.Initialize(false, privateKey.CreateXOnlyPubKey().AsHex(), settings.NodeSettings.LoggerOpenApi, () => new HttpClient());
            gigGossipNode = new GigGossipNode(
                settings.NodeSettings.ConnectionString.Replace("$ID", settings.Id),
                privateKey,
                settings.NodeSettings.ChunkSize,
                new DefaultRetryPolicy(),
                () => new HttpClient(),
                false,
                settings.NodeSettings.LoggerOpenApi
                );

            AnsiConsole.WriteLine("privkey:" + privateKey.AsHex());
            AnsiConsole.WriteLine("pubkey :" + gigGossipNode.PublicKey);

            gigGossipNode.RegisterFrameType<LocationFrame>();
            gigGossipNode.OnDirectMessage += DirectCom_OnDirectMessage;
            directTimer = new System.Timers.Timer(1000);
            directTimer.Elapsed += DirectTimer_Elapsed;

            var phoneNumber = await GetPhoneNumberAsync();
            if (phoneNumber == null)
            {
                phoneNumber = Prompt.Input<string>("Phone number");
                if (!await IsPhoneNumberValidated(phoneNumber))
                {

                    await ValidatePhoneNumber(phoneNumber);
                    while (true)
                    {
                        var secret = Prompt.Input<string>("Enter code");
                        var retries = await SubmitPhoneNumberSecret(phoneNumber, secret);
                        if (retries == -1)
                            break;
                        else if (retries == 0)
                            throw new Exception("Invalid code");
                        else
                            AnsiConsole.WriteLine($"Wrong code retries left {retries}");
                    }
                }
                await SetPhoneNumberAsync(phoneNumber);
            }
            await StartAsync();

            while (true)
            {
                await WriteBalance();
                var cmd = Prompt.Select<CommandEnum>("Select command");
                if (cmd == CommandEnum.Exit)
                {
                    if (cmd == CommandEnum.Exit)
                        break;
                }
                else if (cmd == CommandEnum.TopUp)
                {
                    var topUpAmount = Prompt.Input<int>("How much top up");
                    if (topUpAmount > 0)
                    {
                        var newBitcoinAddressOfCustomer = WalletAPIResult.Get<string>(await gigGossipNode.GetWalletClient().NewAddressAsync(await gigGossipNode.MakeWalletAuthToken(), CancellationTokenSource.Token));
                        WalletAPIResult.Check(await gigGossipNode.GetWalletClient().TopUpAndMine6BlocksAsync(await gigGossipNode.MakeWalletAuthToken(), newBitcoinAddressOfCustomer, topUpAmount, CancellationTokenSource.Token));
                    }
                }
                else if (cmd == CommandEnum.NewAddress)
                {
                    var newBitcoinAddressOfCustomer = WalletAPIResult.Get<string>(await gigGossipNode.GetWalletClient().NewAddressAsync(await gigGossipNode.MakeWalletAuthToken(), CancellationToken.None));
                    AnsiConsole.WriteLine(newBitcoinAddressOfCustomer);
                }
                else if (cmd == CommandEnum.SetupMyInfo)
                {
                    var authToken = await gigGossipNode.MakeSettlerAuthTokenAsync(settings.SettlerAdminSettings.SettlerOpenApi);
                    var settlerClient = gigGossipNode.SettlerSelector.GetSettlerClient(settings.SettlerAdminSettings.SettlerOpenApi);
                    string name = Prompt.Input<string>("Your Name");
                    SettlerAPIResult.Check(await settlerClient.GiveUserPropertyAsync(authToken,
                        (await GetPublicKeyAsync()).AsHex(), "Name",
                        Convert.ToBase64String(Encoding.Default.GetBytes(name)),
                        Convert.ToBase64String(new byte[] { }), 24 * 365 * 10,
                        CancellationTokenSource.Token));

                    byte[] photo = new byte[] { };

                    SettlerAPIResult.Check(await settlerClient.GiveUserFileAsync(authToken,
                        (await GetPublicKeyAsync()).AsHex(), "Photo", 24 * 365 * 10,
                        new FileParameter(new MemoryStream(photo)),
                        new FileParameter(new MemoryStream()),
                        CancellationTokenSource.Token
                    ));

                    string car = Prompt.Input<string>("Your Car");
                    SettlerAPIResult.Check(await settlerClient.GiveUserPropertyAsync(authToken,
                        (await GetPublicKeyAsync()).AsHex(), "Car",
                        Convert.ToBase64String(Encoding.Default.GetBytes(car)),
                        Convert.ToBase64String(new byte[] { }), 24 * 365 * 10,
                        CancellationTokenSource.Token));

                    var randloc = MockData.RandomLocation();
                    string trace = GeoHash.Encode(randloc.Latitude, randloc.Longitude, 7);
                    SettlerAPIResult.Check(await settlerClient.SaveUserTracePropertyAsync(authToken,
                        (await GetPublicKeyAsync()).AsHex(), "Geohash",
                        Convert.ToBase64String(Encoding.Default.GetBytes(trace)),
                        CancellationTokenSource.Token));
                }
                else if (cmd == CommandEnum.DriverMode)
                {
                    inDriverMode = true;
                    AnsiConsole.MarkupLine("Listening for ride requests.");
                    AnsiConsole.MarkupLine("Press [orange1]ENTER[/] to make selection,");
                    AnsiConsole.MarkupLine("[yellow]RIGHT[/] to increase fee.");
                    AnsiConsole.MarkupLine("[yellow]LEFT[/] to decrease fee.");
                    AnsiConsole.MarkupLine("[blue]ESC[/] to leave the driver mode.");


                    receivedBroadcastsForPayloadId = new();
                    receivedBroadcastsFees = new();
                    receivedBroadcastsTable = new DataTable(new string[] { "Sent", "JobId", "NoBrd", "From", "Time", "To", "MyFee" });
                    receivedBroadcastsTable.OnKeyPressed += async (o, e) =>
                        {
                            var me = (DataTable)o;
                            if (e.Key == ConsoleKey.Enter)
                            {
                                if (me.GetCell(me.SelectedRowIdx, 0) != "sent")
                                {
                                    var keys = new List<string>(MockData.FakeAddresses.Keys);
                                    var myAddress = keys[(int)Random.Shared.NextInt64(MockData.FakeAddresses.Count)];
                                    var myStartLocation = new GeoLocation(MockData.FakeAddresses[myAddress].Latitude, MockData.FakeAddresses[myAddress].Longitude);

                                    await AcceptRideAsync(me.SelectedRowIdx, myStartLocation,"Hello from Driver!");
                                    me.UpdateCell(me.SelectedRowIdx, 0, "sent");
                                }
                                else
                                {
                                    await CancelRideAsync(me.SelectedRowIdx);
                                    me.UpdateCell(me.SelectedRowIdx, 0, "");
                                }
                            }
                            if (e.Key == ConsoleKey.LeftArrow)
                            {
                                if (me.GetCell(me.SelectedRowIdx, 0) != "sent")
                                {
                                    var id = Guid.Parse(me.GetCell(me.SelectedRowIdx, 1));
                                    receivedBroadcastsFees[id] -= 1;
                                    me.UpdateCell(me.SelectedRowIdx, 6, receivedBroadcastsFees[id].ToString());
                                }
                            }
                            if (e.Key == ConsoleKey.RightArrow)
                            {
                                if (me.GetCell(me.SelectedRowIdx, 0) != "sent")
                                {
                                    var id = Guid.Parse(me.GetCell(me.SelectedRowIdx, 1));
                                    receivedBroadcastsFees[id] -= 1;
                                    me.UpdateCell(me.SelectedRowIdx, 6, receivedBroadcastsFees[id].ToString());
                                }
                            }
                            if (e.Key == ConsoleKey.Escape)
                            {
                                me.Exit();
                            }
                        };

                    receivedBroadcastsTable.Start();
                }
                else if (cmd == CommandEnum.RequestRide)
                {
                    if (ActiveSignedRequestPayloadId != Guid.Empty)
                    {
                        AnsiConsole.MarkupLine("[red]Ride in progress[/]");
                    }

                    string fromAddress, toAddress;
                    GeoLocation fromLocation, toLocation;

                    if (Prompt.Confirm("Use random", false))
                    {

                        var keys = new List<string>(MockData.FakeAddresses.Keys);

                        while(true)
                        {
                            fromAddress = keys[(int)Random.Shared.NextInt64(MockData.FakeAddresses.Count)];
                            toAddress = keys[(int)Random.Shared.NextInt64(MockData.FakeAddresses.Count)];
                            if(fromAddress != toAddress)
                                break;
                        }
                        
                        fromLocation = new GeoLocation(MockData.FakeAddresses[fromAddress].Latitude, MockData.FakeAddresses[fromAddress].Longitude);
                        toLocation = new GeoLocation(MockData.FakeAddresses[toAddress].Latitude, MockData.FakeAddresses[toAddress].Longitude);
                    }
                    else
                    {
                        fromAddress = await GetAddressAsync("From");
                        toAddress = await GetAddressAsync("To");

                        fromLocation = await GetAddressGeocodeAsync(fromAddress);
                        toLocation = await GetAddressGeocodeAsync(toAddress);
                    }

                    int waitingTimeForPickupMinutes = 12;

                    receivedResponseIdxesForPaymentHashes = new();
                    receivedResponsesForPaymentHashes = new();
                    receivedResponsesTable = new DataTable(new string[] { "PaymentHash", "DriverId", "NoResp", "From", "Time", "To", "DriverFee", "NetworkFee" });
                    receivedResponsesTable.OnKeyPressed += async (o, e) =>
                    {
                        var me = (DataTable)o;
                        if (e.Key == ConsoleKey.Enter)
                        {
                            await AcceptDriverAsync(me.SelectedRowIdx);
                            me.Exit();
                        }
                        if (e.Key == ConsoleKey.Escape)
                        {
                            await CancelBroadcast();
                            me.Exit();
                        }
                    };
                    requestedRide = await RequestRide(fromAddress, fromLocation, toAddress, toLocation, settings.NodeSettings.GeohashPrecision, waitingTimeForPickupMinutes);
                    receivedResponsesTable.Start();
                }
                else if (cmd == CommandEnum.Reset)
                {
                    await this.StopAsync();
                    await this.StartAsync();
                }
                else if (cmd == CommandEnum.DebLog)
                {
                    GigDebugLoggerAPIClient.FlowLoggerFactory.Enabled = !GigDebugLoggerAPIClient.FlowLoggerFactory.Enabled;
                    AnsiConsole.WriteLine("DebugLog is " + (GigDebugLoggerAPIClient.FlowLoggerFactory.Enabled ? "ON" : "OFF"));
                }
            }
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    private async Task<string> GetAddressAsync(string message)
    {
        string address = "";
        bool done = false;
        do
        {
            var query = Prompt.Input<string>(message);
            var props = SettlerAPIResult.Get<List<string>>(await gigGossipNode.SettlerSelector.GetSettlerClient(settings.NodeSettings.SettlerOpenApi)
                .AddressAutocompleteAsync(await gigGossipNode.MakeSettlerAuthTokenAsync(settings.NodeSettings.SettlerOpenApi), query, "au", CancellationTokenSource.Token));

            if (props.Count == 0)
                continue;

            var addrAutocpl = new DataTable(new string[] { "Address" });
            foreach (var prop in props)
                addrAutocpl.AddRow(new string[] { prop });
            addrAutocpl.OnKeyPressed += async (o, e) =>
            {
                var me = (DataTable)o;
                if (e.Key == ConsoleKey.Enter)
                {
                    address = me.GetCell(e.Line, 0);
                    done = true;
                    me.Exit();
                }
                if (e.Key == ConsoleKey.Escape)
                {
                    address = me.GetCell(e.Line, 0);
                    me.Exit();
                }
            };

            addrAutocpl.Start();
        }
        while (!done);

        return address;
    }

    private async Task<GeolocationRet> GetGeolocation(string query)
    {
        return SettlerAPIResult.Get<GeolocationRet>(await gigGossipNode.SettlerSelector.GetSettlerClient(settings.NodeSettings.SettlerOpenApi)
            .AddressGeocodeAsync(await gigGossipNode.MakeSettlerAuthTokenAsync(settings.NodeSettings.SettlerOpenApi), query, "au", CancellationTokenSource.Token));
    }

    private async Task<GeoLocation> GetAddressGeocodeAsync(string query)
    {
        var r = SettlerAPIResult.Get<GeolocationRet>(await gigGossipNode.SettlerSelector.GetSettlerClient(settings.NodeSettings.SettlerOpenApi)
            .AddressGeocodeAsync(await gigGossipNode.MakeSettlerAuthTokenAsync(settings.NodeSettings.SettlerOpenApi), query, "au", CancellationTokenSource.Token));
        return new GeoLocation(r.Lat, r.Lon);
    }

    private async Task<string> GetLocationGeocodeAsync(double lat, double lon)
    {
        return SettlerAPIResult.Get<string>(await gigGossipNode.SettlerSelector.GetSettlerClient(settings.NodeSettings.SettlerOpenApi)
            .LocationGeocodeAsync(await gigGossipNode.MakeSettlerAuthTokenAsync(settings.NodeSettings.SettlerOpenApi), lat, lon, CancellationTokenSource.Token));
    }

    private async Task WriteBalance()
    {
        var ballanceOfCustomer = WalletAPIResult.Get<long>(await gigGossipNode.GetWalletClient().GetBalanceAsync(await gigGossipNode.MakeWalletAuthToken(), CancellationTokenSource.Token));
        AnsiConsole.WriteLine("Current amout in satoshis:" + ballanceOfCustomer.ToString());
    }

    private async void GigGossipNodeEventSource_OnNetworkInvoiceAccepted(object? sender, NetworkInvoiceAcceptedEventArgs e)
    {
        AnsiConsole.WriteLine("Network Invoice Accepted");
        var paymentResult = await e.GigGossipNode.PayNetworkInvoiceAsync(e.InvoiceData, settings.NodeSettings.FeeLimitSat, CancellationTokenSource.Token);
        if (paymentResult != GigLNDWalletAPIErrorCode.Ok)
        {
            Console.WriteLine(paymentResult);
        }
    }

    private void GigGossipNodeEventSource_OnNewContact(object? sender, NewContactEventArgs e)
    {
        AnsiConsole.WriteLine("New contact :" + e.PublicKey);
    }

    async Task StopAsync()
    {
        directTimer.Stop();
        await gigGossipNode.StopAsync();
    }

    async Task StartAsync()
    {

        await gigGossipNode.StartAsync(
            settings.NodeSettings.Fanout,
            settings.NodeSettings.PriceAmountForRouting,
            TimeSpan.FromMilliseconds(settings.NodeSettings.TimestampToleranceMs),
            TimeSpan.FromSeconds(settings.NodeSettings.InvoicePaymentTimeoutSec),
            settings.NodeSettings.GetNostrRelays(),
            ((GigGossipNodeEventSource)gigGossipNodeEventSource).GigGossipNodeEvents,
            settings.NodeSettings.GigWalletOpenApi,
            settings.NodeSettings.SettlerOpenApi);

        var ballanceOfCustomer = WalletAPIResult.Get<long>(await gigGossipNode.GetWalletClient().GetBalanceAsync(await gigGossipNode.MakeWalletAuthToken(), CancellationTokenSource.Token));
        AnsiConsole.WriteLine("Current amout in satoshis:" + ballanceOfCustomer.ToString());

        gigGossipNode.LoadContactList();
        var contactList = gigGossipNode.GetContactList(24);
        AnsiConsole.WriteLine("Contacts:");
        foreach (var contact in contactList)
            AnsiConsole.WriteLine("contact :" + contact);
    }

    LocationFrame myLastLocationFrame = null;

    private async void DirectTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        string pubkey = null;
        Guid requestPayloadId = Guid.Empty;
        DateTime lastSeen = DateTime.MinValue;
        if (inDriverMode)
        {
            if (ActiveSignedRequestPayloadId != Guid.Empty)
                if (directPubkeys.ContainsKey(ActiveSignedRequestPayloadId))
                {
                    pubkey = directPubkeys[ActiveSignedRequestPayloadId];
                    requestPayloadId = ActiveSignedRequestPayloadId;
                    lastSeen = lastRiderSeenAt;
                }
        }
        else
        {
            if (requestedRide != null)
                if (directPubkeys.ContainsKey(requestedRide.SignedRequestPayload.Id))
                {
                    pubkey = directPubkeys[requestedRide.SignedRequestPayload.Id];
                    requestPayloadId = requestedRide.SignedRequestPayload.Id;
                    lastSeen = lastDriverSeenAt;
                }
        }

        if (pubkey != null)
        {
            if (myLastLocationFrame != null)
            {
                AnsiConsole.MarkupLine($"{pubkey} last seen {(DateTime.UtcNow - lastSeen).Seconds} seconds ago");

                await gigGossipNode.SendMessageAsync(pubkey,
                    myLastLocationFrame, true);
            }
        }
    }

    private async void DirectCom_OnDirectMessage(object? sender, DirectMessageEventArgs e)
    {
        if (e.Frame is LocationFrame locationFrame)
        {
            if (inDriverMode)
                await OnRiderLocation(e.SenderPublicKey, locationFrame);
            else
                await OnDriverLocation(e.SenderPublicKey, locationFrame);
        }
    }

    async Task<bool> IsPhoneNumberValidated(string phoneNumber)
    {
        var authToken = await gigGossipNode.MakeSettlerAuthTokenAsync(settings.SettlerAdminSettings.SettlerOpenApi);
        var settlerClient = gigGossipNode.SettlerSelector.GetSettlerClient(settings.SettlerAdminSettings.SettlerOpenApi);
        return SettlerAPIResult.Get<bool>(await settlerClient.IsChannelVerifiedAsync(authToken, (await GetPublicKeyAsync()).AsHex(), "PhoneNumber", phoneNumber, CancellationTokenSource.Token));
    }

    async Task ValidatePhoneNumber(string phoneNumber)
    {
        var authToken = await gigGossipNode.MakeSettlerAuthTokenAsync(settings.SettlerAdminSettings.SettlerOpenApi);
        var settlerClient = gigGossipNode.SettlerSelector.GetSettlerClient(settings.SettlerAdminSettings.SettlerOpenApi);
        SettlerAPIResult.Check(await settlerClient.VerifyChannelAsync(authToken, (await GetPublicKeyAsync()).AsHex(), "PhoneNumber", "SMS", phoneNumber, CancellationTokenSource.Token));
    }

    async Task<int> SubmitPhoneNumberSecret(string phoneNumber, string secret)
    {
        var authToken = await gigGossipNode.MakeSettlerAuthTokenAsync(settings.SettlerAdminSettings.SettlerOpenApi);
        var settlerClient = gigGossipNode.SettlerSelector.GetSettlerClient(settings.SettlerAdminSettings.SettlerOpenApi);
        return SettlerAPIResult.Get<int>(await settlerClient.SubmitChannelSecretAsync(authToken, (await GetPublicKeyAsync()).AsHex(), "PhoneNumber", "SMS", phoneNumber, secret, CancellationTokenSource.Token));
    }



}
