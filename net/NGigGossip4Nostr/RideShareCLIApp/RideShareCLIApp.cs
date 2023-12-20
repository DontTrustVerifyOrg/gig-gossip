using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using CryptoToolkit;
using GigGossipFrames;
using GigGossipSettlerAPIClient;
using GigLNDWalletAPIClient;
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
using RideShareFrames;
using Sharprompt;
using Spectre;
using Spectre.Console;

namespace RideShareCLIApp;


public partial class RideShareCLIApp
{
    Settings settings;
    GigGossipNode gigGossipNode;
    HttpClient httpClient = new HttpClient();
    IGigGossipNodeEventSource gigGossipNodeEventSource = new GigGossipNodeEventSource();

    bool inDriverMode = false;
    DirectCom directCom;
    Dictionary<Guid, string> directPubkeys = new();

    public RideShareCLIApp(string id, IConfigurationRoot config)
    {
        if (id == null)
            id = AnsiConsole.Prompt(new TextPrompt<string>("Enter this node [orange1]Id[/]?"));

        this.settings = new Settings(id, config);

        SecureStorage.InitializeDefault(
            settings.NodeSettings.SecureStorageConnectionString.
            Replace("$HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).
            Replace("$ID", id));

        gigGossipNodeEventSource.OnAcceptBroadcast += GigGossipNodeEventSource_OnAcceptBroadcast;
        gigGossipNodeEventSource.OnNetworkInvoiceAccepted += GigGossipNodeEventSource_OnNetworkInvoiceAccepted;
        gigGossipNodeEventSource.OnNewResponse += GigGossipNodeEventSource_OnNewResponse;
        gigGossipNodeEventSource.OnResponseReady += GigGossipNodeEventSource_OnResponseReady;
        gigGossipNodeEventSource.OnInvoiceAccepted += GigGossipNodeEventSource_OnInvoiceAccepted;
        gigGossipNodeEventSource.OnInvoiceCancelled += GigGossipNodeEventSource_OnInvoiceCancelled;
        gigGossipNodeEventSource.OnCancelBroadcast += GigGossipNodeEventSource_OnCancelBroadcast;
        gigGossipNodeEventSource.OnNetworkInvoiceCancelled += GigGossipNodeEventSource_OnNetworkInvoiceCancelled;
        gigGossipNodeEventSource.OnPaymentStatusChange += GigGossipNodeEventSource_OnPaymentStatusChange;
        gigGossipNodeEventSource.OnInvoiceSettled += GigGossipNodeEventSource_OnInvoiceSettled;
        gigGossipNodeEventSource.OnNewContact += GigGossipNodeEventSource_OnNewContact;
    }

    private void GigGossipNodeEventSource_OnInvoiceSettled(object? sender, InvoiceSettledEventArgs e)
    {
    }

    private void GigGossipNodeEventSource_OnPaymentStatusChange(object? sender, PaymentStatusChangeEventArgs e)
    {
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
        [Display(Name = "Exit App")]
        Exit,
        [Display(Name = "Top up")]
        TopUp,
        [Display(Name = "Setup My Info")]
        SetupMyInfo,
        [Display(Name = "Enter Driver Mode")]
        DriverMode,
        [Display(Name = "Request Ride")]
        RequestRide,
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
        await StartAsync();

        var phoneNumber = await GetPhoneNumberAsync();
        if (phoneNumber == null)
        {
            phoneNumber = Prompt.Input<string>("Phone number");
            await ValidatePhoneNumber(phoneNumber);
            var secret = Prompt.Input<string>("Enter code");
            while (true)
            {
                var retries = await SubmitPhoneNumberSecret(phoneNumber, secret);
                if (retries == -1)
                    break;
                else if (retries == 0)
                    throw new Exception("Invalid phone number");
            }
            await SetPhoneNumberAsync(phoneNumber);
        }

        while (true)
        {
            var cmd = Prompt.Select<CommandEnum>("Select command");
            if (cmd == CommandEnum.Exit)
            {
                if (cmd == CommandEnum.Exit)
                    break;
            }
            else if (cmd == CommandEnum.TopUp)
            {
                var ballanceOfCustomer = WalletAPIResult.Get<long>(await gigGossipNode.LNDWalletClient.GetBalanceAsync(gigGossipNode.MakeWalletAuthToken()));
                AnsiConsole.WriteLine("Current amout in satoshis:" + ballanceOfCustomer.ToString());
                var topUpAmount = Prompt.Input<int>("How much top up");
                if (topUpAmount > 0)
                {
                    var newBitcoinAddressOfCustomer = WalletAPIResult.Get<string>(await gigGossipNode.LNDWalletClient.NewAddressAsync(gigGossipNode.MakeWalletAuthToken()));
                    gigGossipNode.LNDWalletClient.TopUpAndMine6BlocksAsync(newBitcoinAddressOfCustomer, topUpAmount);
                }
            }
            else if (cmd == CommandEnum.SetupMyInfo)
            {
                var authToken = await gigGossipNode.MakeSettlerAuthTokenAsync(settings.SettlerAdminSettings.SettlerOpenApi);
                var settlerClient = gigGossipNode.SettlerSelector.GetSettlerClient(settings.SettlerAdminSettings.SettlerOpenApi);
                string name = Prompt.Input<string>("Your Name");
                SettlerAPIResult.Check(await settlerClient.GiveUserPropertyAsync(authToken,
                    (await GetPublicKeyAsync()).AsHex(), "Name",
                    Convert.ToBase64String(Encoding.Default.GetBytes(name)),
                    Convert.ToBase64String(new byte[] { }), DateTime.MaxValue.ToLongDateString()));

                byte[] photo = new byte[] { };
                SettlerAPIResult.Check(await settlerClient.GiveUserPropertyAsync(authToken,
                    (await GetPublicKeyAsync()).AsHex(), "Photo",
                    Convert.ToBase64String(photo),
                    Convert.ToBase64String(new byte[] { }), DateTime.MaxValue.ToLongDateString()));

                string car = Prompt.Input<string>("Your Car");
                SettlerAPIResult.Check(await settlerClient.GiveUserPropertyAsync(authToken,
                    (await GetPublicKeyAsync()).AsHex(), "Car",
                    Convert.ToBase64String(Encoding.Default.GetBytes(car)),
                    Convert.ToBase64String(new byte[] { }), DateTime.MaxValue.ToLongDateString()));

                string trace = GeoHash.Encode(0, 0, 7);
                SettlerAPIResult.Check(await settlerClient.SaveUserTracePropertyAsync(authToken,
                    (await GetPublicKeyAsync()).AsHex(), "Geohash",
                    Convert.ToBase64String(Encoding.Default.GetBytes(trace))));
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
                                await AcceptRideAsync(me.SelectedRowIdx);
                                me.UpdateCell(me.SelectedRowIdx, 0, "sent");
                                me.InactivateRow(me.SelectedRowIdx, false);
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
                var keys = new List<string>(MockData.FakeAddresses.Keys);
                var fromAddress = keys[(int)Random.Shared.NextInt64(MockData.FakeAddresses.Count)];
                var toAddress = keys[(int)Random.Shared.NextInt64(MockData.FakeAddresses.Count)];

                var fromLocation = new GeoLocation(MockData.FakeAddresses[fromAddress].Latitude, MockData.FakeAddresses[fromAddress].Longitude);
                var toLocation = new GeoLocation(MockData.FakeAddresses[toAddress].Latitude, MockData.FakeAddresses[toAddress].Longitude);
                int waitingTimeForPickupMinutes = 12;

                receivedResponseIdxesForPaymentHashes = new();
                receivedResponsesForPaymentHashes = new();
                driverIdxesForPaymentHashes = new();
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
                        me.Exit();
                    }
                };
                requestedRide = await RequestRide(fromAddress, fromLocation, toAddress, toLocation, settings.NodeSettings.GeohashPrecision, waitingTimeForPickupMinutes);
                receivedResponsesTable.Start();
            }
        }
    }

    private async void GigGossipNodeEventSource_OnNetworkInvoiceAccepted(object? sender, NetworkInvoiceAcceptedEventArgs e)
    {
        AnsiConsole.WriteLine("Network Invoice Accepted");
        var paymentResult = await e.GigGossipNode.PayNetworkInvoiceAsync(e.InvoiceData);
        if (paymentResult != GigLNDWalletAPIErrorCode.Ok)
        {
            Console.WriteLine(paymentResult);
        }
    }

    private void GigGossipNodeEventSource_OnNewContact(object? sender, NewContactEventArgs e)
    {
        AnsiConsole.WriteLine("New contact :" + e.PublicKey);
    }

    async Task StartAsync()
    {
        var privateKey = await GetPrivateKeyAsync();
        if (privateKey == null)
        {
            var mnemonic = Crypto.GenerateMnemonic().Split(" ");
            AnsiConsole.WriteLine($"Initializing private key for {settings.Id}");
            AnsiConsole.WriteLine(string.Join(" ", mnemonic));
            privateKey = Crypto.DeriveECPrivKeyFromMnemonic(string.Join(" ", mnemonic));
            await SetPrivateKeyAsync(privateKey);
        }
        else
        {
            AnsiConsole.WriteLine($"Loading private key for {settings.Id}");
        }

        gigGossipNode = new GigGossipNode(
            settings.NodeSettings.ConnectionString.Replace("$ID", settings.Id),
            privateKey,
            settings.NodeSettings.ChunkSize);

        gigGossipNode.Init(
            settings.NodeSettings.Fanout,
            settings.NodeSettings.PriceAmountForRouting,
            TimeSpan.FromMilliseconds(settings.NodeSettings.TimestampToleranceMs),
            TimeSpan.FromSeconds(settings.NodeSettings.InvoicePaymentTimeoutSec),
            settings.NodeSettings.GetLndWalletClient(httpClient));

        await gigGossipNode.StartAsync(
            settings.NodeSettings.GetNostrRelays(),
            ((GigGossipNodeEventSource) gigGossipNodeEventSource).GigGossipNodeEvents);

        directCom = new DirectCom(await GetPrivateKeyAsync(), settings.NodeSettings.ChunkSize);
        directCom.RegisterFrameType<AckFrame>();
        directCom.RegisterFrameType<LocationFrame>();
        directCom.OnDirectMessage += DirectCom_OnDirectMessage;

        AnsiConsole.WriteLine("privkey:" + privateKey.AsHex());
        AnsiConsole.WriteLine("pubkey :" + gigGossipNode.PublicKey);

        var ballanceOfCustomer = WalletAPIResult.Get<long>(await gigGossipNode.LNDWalletClient.GetBalanceAsync(gigGossipNode.MakeWalletAuthToken()));
        AnsiConsole.WriteLine("Current amout in satoshis:" + ballanceOfCustomer.ToString());

        var contactList = gigGossipNode.LoadContactList();
        AnsiConsole.WriteLine("Contacts:");
        foreach (var contact in contactList)
            AnsiConsole.WriteLine("contact :" + contact);
    }

    private async void DirectCom_OnDirectMessage(object? sender, DirectMessageEventArgs e)
    {
        if (e.Frame is LocationFrame locationFrame)
        {
            if (e.IsNew)
            {
                if (directPubkeys.ContainsKey(locationFrame.SignedRequestPayloadId))
                {
                    var pubkey = directPubkeys[locationFrame.SignedRequestPayloadId];
                    if (pubkey == e.SenderPublicKey)
                    {
                        if (inDriverMode)
                            await OnRiderLocation(e.SenderPublicKey, locationFrame);
                        else
                            await OnDriverLocation(e.SenderPublicKey, locationFrame);
                    }
                }
            }
        }
        else if (e.Frame is AckFrame ackframe)
        {
            await OnAckFrame(e.SenderPublicKey, ackframe);
        }
    }

    async Task ValidatePhoneNumber(string phoneNumber)
    {
        var authToken = await gigGossipNode.MakeSettlerAuthTokenAsync(settings.SettlerAdminSettings.SettlerOpenApi);
        var settlerClient = gigGossipNode.SettlerSelector.GetSettlerClient(settings.SettlerAdminSettings.SettlerOpenApi);
        SettlerAPIResult.Check(await settlerClient.VerifyChannelAsync(authToken, (await GetPublicKeyAsync()).AsHex(), "PhoneNumber", "SMS", phoneNumber));
    }

    async Task<int> SubmitPhoneNumberSecret(string phoneNumber, string secret)
    {
        var authToken = await gigGossipNode.MakeSettlerAuthTokenAsync(settings.SettlerAdminSettings.SettlerOpenApi);
        var settlerClient = gigGossipNode.SettlerSelector.GetSettlerClient(settings.SettlerAdminSettings.SettlerOpenApi);
        return SettlerAPIResult.Get<int>(await settlerClient.SubmitChannelSecretAsync(authToken, (await GetPublicKeyAsync()).AsHex(), "PhoneNumber", "SMS", phoneNumber, secret));
    }



}
