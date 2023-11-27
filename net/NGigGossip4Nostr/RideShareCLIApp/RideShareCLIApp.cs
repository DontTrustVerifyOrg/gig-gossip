using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using CryptoToolkit;
using GigGossipFrames;
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
using Nominatim.API.Address;
using Nominatim.API.Geocoders;
using Nominatim.API.Models;
using Sharprompt;
using Spectre;
using Spectre.Console;

namespace RideShareCLIApp;

public sealed class DefaultHttpClientFactory : IHttpClientFactory, IDisposable
{
    private readonly Lazy<HttpMessageHandler> _handlerLazy = new(() => new HttpClientHandler());

    public HttpClient CreateClient(string name) => new(_handlerLazy.Value, disposeHandler: false);

    public void Dispose()
    {
        if (_handlerLazy.IsValueCreated)
        {
            _handlerLazy.Value.Dispose();
        }
    }
}

public class RideShareCLIApp
{
    Settings settings;
    GigGossipNode gigGossipNode;
    HttpClient httpClient = new HttpClient();
    IGigGossipNodeEventSource gigGossipNodeEventSource = new GigGossipNodeEventSource();
    QuerySearcher querySearcher;
    ForwardGeocoder forwardGeocoder;

    BroadcastTopicResponse requestedRide = null;

    List<AcceptBroadcastEventArgs> receivedBroadcasts = new();
    List<long> receivedBroadcastsFees = new();
    DataTable receivedBroadcastsTable = null;
    Dictionary<Guid, int> receivedBroadcastIdxesForPayloadIds = new();
    Dictionary<Guid, string> broadcastSecrets = new();
    bool inDriverMode = false;

    List<NewResponseEventArgs> receivedResponses = new();
    DataTable receivedResponsesTable = null;
    Dictionary<string, int> receivedResponsesIdxesForPaymentHashes = new();

    DirectCom directCom;

    public RideShareCLIApp(string id, IConfigurationRoot config)
    {
        if (id == null)
            id = AnsiConsole.Prompt(new TextPrompt<string>("Enter this node [orange1]Id[/]?"));

        this.settings = new Settings(id, config);

        var webInterface = new Nominatim.API.Web.NominatimWebInterface(new DefaultHttpClientFactory());
        this.querySearcher = new QuerySearcher(webInterface);
        this.forwardGeocoder = new ForwardGeocoder(webInterface);

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
        [Display(Name = "Mine some blocks")]
        MineBlocks,
        [Display(Name = "Top up")]
        TopUp,
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

    public static List<string> GetCountryList(out Dictionary<string, string> isoCodes)
    {
        isoCodes = new Dictionary<string, string>();
        List<string> cultureList = new List<string>();

        CultureInfo[] cultures = CultureInfo.GetCultures(CultureTypes.SpecificCultures);

        foreach (CultureInfo culture in cultures)
        {
            RegionInfo region = new RegionInfo(culture.Name);

            if (!(cultureList.Contains(region.EnglishName)))
            {
                cultureList.Add(region.EnglishName);
                isoCodes.Add(region.EnglishName, region.TwoLetterISORegionName);
            }
        }
        return cultureList;
    }

    public async Task<Location> PickLocationAsync(string reason)
    {
        Dictionary<string, string> countryCodes;
        var country = Prompt.Select($"Country of {reason}", GetCountryList(out countryCodes));

        string address = null;
        while (true)
        {
            string query = Prompt.Input<string>($"Search for {reason} address");
            var places = await querySearcher.Search(new SearchQueryRequest
            {
                CountryCodeSearch = countryCodes[country],
                queryString = query,
                DedupeResults = true
            });
            var potentials = new List<string>() { "<cancel>" };
            potentials.AddRange(from p in places select p.DisplayName);
            var place = Prompt.Select($"Select valid {reason} address", potentials);
            if (place != "<cancel>")
                break;
        }
        var geocodeResponses = await forwardGeocoder.Geocode(new ForwardGeocodeRequest { Country = country, StreetAddress = address });
        return new Location(geocodeResponses[0].Latitude, geocodeResponses[0].Longitude);
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
            else if(cmd == CommandEnum.MineBlocks)
            {
                var bitcoinClient = settings.BitcoinSettings.NewRPCClient();

                // load bitcoin node wallet
                RPCClient? bitcoinWalletClient;
                try
                {
                    bitcoinWalletClient = bitcoinClient.LoadWallet(settings.BitcoinSettings.WalletName); ;
                }
                catch (RPCException exception) when (exception.RPCCode == RPCErrorCode.RPC_WALLET_ALREADY_LOADED)
                {
                    bitcoinWalletClient = bitcoinClient.SetWalletContext(settings.BitcoinSettings.WalletName);
                }

                var numbl = Prompt.Input<int>("How many blocks?");
                bitcoinWalletClient.Generate(numbl); 
            }
            else if(cmd == CommandEnum.TopUp)
            {
                var bitcoinClient = settings.BitcoinSettings.NewRPCClient();

                var ballanceOfCustomer = await gigGossipNode.LNDWalletClient.GetBalanceAsync(gigGossipNode.MakeWalletAuthToken());
                AnsiConsole.WriteLine("Current amout in satoshis:" + ballanceOfCustomer.ToString());
                var topUpAmount = Prompt.Input<int>("How much top up");
                if(topUpAmount > 0)
                {
                    var newBitcoinAddressOfCustomer = await gigGossipNode.LNDWalletClient.NewAddressAsync(gigGossipNode.MakeWalletAuthToken());
                    bitcoinClient.SendToAddress(NBitcoin.BitcoinAddress.Create(newBitcoinAddressOfCustomer, settings.BitcoinSettings.GetNetwork()), new NBitcoin.Money(topUpAmount));
                }
            }
            else if (cmd == CommandEnum.DriverMode)
            {
                inDriverMode = true;
                AnsiConsole.MarkupLine("Listening for ride requests.");
                AnsiConsole.MarkupLine("Press [orange1]ENTER[/] to make selection,");
                AnsiConsole.MarkupLine("[yellow]RIGHT[/] to increase fee.");
                AnsiConsole.MarkupLine("[yellow]LEFT[/] to decrease fee.");
                AnsiConsole.MarkupLine("[blue]ESC[/] to leave the driver mode.");


                receivedBroadcasts = new();
                receivedBroadcastsFees = new();
                receivedBroadcastIdxesForPayloadIds = new();
                receivedBroadcastsTable = new DataTable(new string[] { "Sent", "Order", "MyFee" });
                receivedBroadcastsTable.OnKeyPressed+= async (o,e)=>
                    {
                        var me = (DataTable)o;
                        if (e.Key == ConsoleKey.Enter)
                        {
                            if (me.GetCell(me.SelectedRowIdx, 0) != "sent")
                            {
                                await AcceptRideAsync(me.SelectedRowIdx);
                                me.UpdateCell(me.SelectedRowIdx, 0, "sent");
                            }
                        }
                        if (e.Key == ConsoleKey.LeftArrow)
                        {
                            if (me.GetCell(me.SelectedRowIdx,0) != "sent")
                            {
                                receivedBroadcastsFees[me.SelectedRowIdx] -= 1;
                                me.UpdateCell(me.SelectedRowIdx, 2, receivedBroadcastsFees[me.SelectedRowIdx].ToString());
                            }
                        }
                        if(e.Key == ConsoleKey.RightArrow)
                        {
                            if (me.GetCell(me.SelectedRowIdx, 0) != "sent")
                            {
                                receivedBroadcastsFees[me.SelectedRowIdx] += 1;
                                me.UpdateCell(me.SelectedRowIdx, 2, receivedBroadcastsFees[me.SelectedRowIdx].ToString());
                            }
                        }
                        if(e.Key== ConsoleKey.Escape)
                        {
                            me.Exit();
                        }
                    };

                receivedBroadcastsTable.Start();
                inDriverMode = false;
            }
            else if (cmd == CommandEnum.RequestRide)
            {
                /*
                AnsiConsole.WriteLine("Pickup location");
                var fromLocation = await PickLocationAsync("pickup");
                AnsiConsole.WriteLine("Dropoff location");
                var toLocation = await PickLocationAsync("dropoff");
                var waitingTimeForPickupMinutes = Prompt.Input<int>("Waiting Time For Pickup In Minutes");
                */
                var fromLocation = new Location(0, 0);
                var toLocation = new Location(1, 1);
                int waitingTimeForPickupMinutes = 12;
                requestedRide = await RequestRide(fromLocation, toLocation, settings.NodeSettings.GeohashPrecision, waitingTimeForPickupMinutes);

                receivedResponses = new();
                receivedResponsesTable = new DataTable(new string[] {"Order", "Fee" });
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
                receivedResponsesTable.Start();
            }
        }
    }

    const string DATE_FORMAT = "'yyyy'-'MM'-'dd'T'HH':'mm'";

    private static string describeBroadcast(AcceptBroadcastEventArgs e)
    {
        var taxiTopic = Crypto.DeserializeObject<RideTopic>(e.BroadcastFrame.SignedRequestPayload.Value.Topic);
        return taxiTopic.FromGeohash + "(" + taxiTopic.PickupAfter.ToString(DATE_FORMAT) + "+" + ((int)(taxiTopic.PickupBefore- taxiTopic.PickupAfter).TotalMinutes).ToString() + ") ->" + taxiTopic.ToGeohash;
    }

    private static string describeResponse(NewResponseEventArgs e)
    {
        return e.ReplyPayloadCert.Properties[0] + "|" + e.ReplyPayloadCert.ServiceUri;
    }

    private async Task AcceptRideAsync(int idx)
    {
        var e = receivedBroadcasts[idx];
        var fee = receivedBroadcastsFees[idx];

        var secret = Crypto.GenerateRandomPreimage().AsHex();
        broadcastSecrets[e.BroadcastFrame.SignedRequestPayload.Id] = secret;

        var reply = new ConnectionReply()
        {
            PublicKey = e.GigGossipNode.PublicKey,
            Relays = e.GigGossipNode.NostrRelays,
            Secret = secret,
        };

        await e.GigGossipNode.AcceptBroadcastAsync(e.PeerPublicKey, e.BroadcastFrame,
                        new AcceptBroadcastResponse()
                        {
                            Properties = settings.NodeSettings.GetDriverProperties(),
                            Message = Crypto.SerializeObject(reply),
                            Fee = fee,
                            SettlerServiceUri = settings.NodeSettings.SettlerOpenApi,
                        });

        receivedBroadcastIdxesForPayloadIds[e.BroadcastFrame.SignedRequestPayload.Id] = idx;

    }

    private async Task AcceptDriverAsync(int idx)
    {
        var e = receivedResponses[idx];
        await e.GigGossipNode.AcceptResponseAsync(e.ReplyPayloadCert, e.ReplyInvoice, e.DecodedReplyInvoice, e.NetworkInvoice, e.DecodedNetworkInvoice);
        await e.GigGossipNode.CancelBroadcastAsync(requestedRide.SignedCancelRequestPayload);
    }

    private async void GigGossipNodeEventSource_OnNetworkInvoiceAccepted(object? sender, NetworkInvoiceAcceptedEventArgs e)
    {
        AnsiConsole.WriteLine("Network Invoice Accepted");
        await e.GigGossipNode.PayNetworkInvoiceAsync(e.InvoiceData);
    }

    private async void GigGossipNodeEventSource_OnAcceptBroadcast(object? sender, AcceptBroadcastEventArgs e)
    {
        var taxiTopic = Crypto.DeserializeObject<RideTopic>(e.BroadcastFrame.SignedRequestPayload.Value.Topic);
        if (inDriverMode)
        {
            if (taxiTopic != null)
            {
                long fee = 100;
                var desc = describeBroadcast(e);
                receivedBroadcastsTable.AddRow(new string[] { "", desc, fee.ToString() });
                receivedBroadcasts.Add(e);
                receivedBroadcastsFees.Add(fee);
                receivedBroadcastIdxesForPayloadIds[e.BroadcastFrame.SignedRequestPayload.Id] = receivedBroadcasts.Count - 1;
                return;
            }
        }
        if (taxiTopic != null)
        {
            if (taxiTopic.FromGeohash.Length <= settings.NodeSettings.GeohashPrecision &&
                   taxiTopic.ToGeohash.Length <= settings.NodeSettings.GeohashPrecision)
            {
                await e.GigGossipNode.BroadcastToPeersAsync(e.PeerPublicKey, e.BroadcastFrame);
            }
        }
    }

    private void GigGossipNodeEventSource_OnNewContact(object? sender, NewContactEventArgs e)
    {
        AnsiConsole.WriteLine("New contact :" + e.PublicKey);
    }

    private async void GigGossipNodeEventSource_OnNewResponse(object? sender, NewResponseEventArgs e)
    {
        if (receivedResponsesTable == null)
            return;
        var desc = describeResponse(e);
        receivedResponses.Add(e);
        receivedResponsesIdxesForPaymentHashes[e.DecodedReplyInvoice.PaymentHash] = receivedResponses.Count - 1;
        var fee = e.DecodedReplyInvoice.NumSatoshis + e.DecodedNetworkInvoice.NumSatoshis;
        receivedResponsesTable.AddRow(new string[] { desc, fee.ToString() });
    }

    private async void GigGossipNodeEventSource_OnResponseReady(object? sender, ResponseReadyEventArgs e)
    {
//        directCom.Stop();
//        await directCom.StartAsync(e.Reply.Relays);
//        await directCom.SendMessageAsync(e.Reply.PublicKey, new AckFrame() { Secret = e.Reply.Secret }, true);
    }


    private async void GigGossipNodeEventSource_OnInvoiceAccepted(object? sender, InvoiceAcceptedEventArgs e)
    {
        if (inDriverMode)
        {
            if (!e.InvoiceData.IsNetworkInvoice)
            {
                var hashes = (from br in e.GigGossipNode.GetAcceptedBroadcasts()
                              select Crypto.DeserializeObject<PayReq>(br.DecodedReplyInvoice).PaymentHash).ToList();

                foreach (var bbr in (from hash in hashes where hash != e.InvoiceData.PaymentHash select hash))
                    e.GigGossipNode.LNDWalletClient.CancelInvoiceAsync(e.GigGossipNode.MakeWalletAuthToken(), bbr);

                receivedBroadcastsTable.Exit();

                directCom.StopAsync();
                await directCom.StartAsync(e.GigGossipNode.NostrRelays);
            }
        }
    }

    private void GigGossipNodeEventSource_OnInvoiceCancelled(object? sender, InvoiceCancelledEventArgs e)
    {
        if (!receivedResponsesIdxesForPaymentHashes.ContainsKey(e.InvoiceData.PaymentHash))
            return;
        if (!e.InvoiceData.IsNetworkInvoice)
        {
            var idx = receivedResponsesIdxesForPaymentHashes[e.InvoiceData.PaymentHash];
            receivedResponses.RemoveAt(idx);
            receivedResponsesTable.RemoveRow(idx);
            receivedResponsesIdxesForPaymentHashes.Remove(e.InvoiceData.PaymentHash);
        }
    }

    private void GigGossipNodeEventSource_OnCancelBroadcast(object? sender, CancelBroadcastEventArgs e)
    {
        if (!receivedBroadcastIdxesForPayloadIds.ContainsKey(e.CancelBroadcastFrame.SignedCancelRequestPayload.Id))
            return;
        var idx = receivedBroadcastIdxesForPayloadIds[e.CancelBroadcastFrame.SignedCancelRequestPayload.Id];
        receivedBroadcasts.RemoveAt(idx);
        receivedBroadcastsFees.RemoveAt(idx);
        receivedBroadcastIdxesForPayloadIds.Remove(e.CancelBroadcastFrame.SignedCancelRequestPayload.Id);
        receivedBroadcastsTable.RemoveRow(idx);
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
        directCom.RegisterFrameType<LocationFrame>();
        directCom.OnDirectMessage += DirectCom_OnDirectMessage;

        AnsiConsole.WriteLine("privkey:" + privateKey.AsHex());
        AnsiConsole.WriteLine("pubkey :" + gigGossipNode.PublicKey);

        var ballanceOfCustomer = await gigGossipNode.LNDWalletClient.GetBalanceAsync(gigGossipNode.MakeWalletAuthToken());
        AnsiConsole.WriteLine("Current amout in satoshis:" + ballanceOfCustomer.ToString());

        var contactList = gigGossipNode.LoadContactList();
        AnsiConsole.WriteLine("Contacts:");
        foreach (var contact in contactList)
            AnsiConsole.WriteLine("contact :" + contact);
    }

    private void DirectCom_OnDirectMessage(object? sender, DirectMessageEventArgs e)
    {
        if (e.Frame is LocationFrame locationFrame)
        {
        }
        else if (e.Frame is AckFrame ackframe)
        {
        }
    }

    async Task ValidatePhoneNumber(string phoneNumber)
    {
        var authToken = await gigGossipNode.MakeSettlerAuthTokenAsync(settings.SettlerAdminSettings.SettlerOpenApi);
        var settlerClient = gigGossipNode.SettlerSelector.GetSettlerClient(settings.SettlerAdminSettings.SettlerOpenApi);
        await settlerClient.VerifyChannelAsync(authToken, (await GetPublicKeyAsync()).AsHex(), "PhoneNumber", "SMS", phoneNumber);
    }

    async Task<int> SubmitPhoneNumberSecret(string phoneNumber, string secret)
    {
        var authToken = await gigGossipNode.MakeSettlerAuthTokenAsync(settings.SettlerAdminSettings.SettlerOpenApi);
        var settlerClient = gigGossipNode.SettlerSelector.GetSettlerClient(settings.SettlerAdminSettings.SettlerOpenApi);
        return await settlerClient.SubmitChannelSecretAsync(authToken, (await GetPublicKeyAsync()).AsHex(), "PhoneNumber", "SMS", phoneNumber, secret);
    }

    async Task<BroadcastTopicResponse> RequestRide(Location fromLocation, Location toLocation, int precision, int waitingTimeForPickupMinutes)
    {
        var fromGh = GeoHash.Encode(latitude: fromLocation.Latitude, longitude: fromLocation.Longitude, numberOfChars: precision);
        var toGh = GeoHash.Encode(latitude: toLocation.Latitude, longitude: toLocation.Longitude, numberOfChars: precision);

        return await gigGossipNode.BroadcastTopicAsync(
            topic: new RideTopic
            {
                FromGeohash = fromGh,
                ToGeohash = toGh,
                PickupAfter = DateTime.Now,
                PickupBefore = DateTime.Now.AddMinutes(waitingTimeForPickupMinutes),
            },
            settings.NodeSettings.SettlerOpenApi,
            settings.NodeSettings.GetRiderProperties());

    }

}
