using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using CryptoToolkit;
using GigGossipFrames;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using NBitcoin;
using NBitcoin.RPC;
using NBitcoin.Secp256k1;
using NGeoHash;
using NGigGossip4Nostr;
using Nominatim.API.Address;
using Nominatim.API.Geocoders;
using Nominatim.API.Models;
using Sharprompt;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

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
    ILogger logger;
    GigGossipNode gigGossipNode;
    HttpClient httpClient = new HttpClient();
    GigGossipNodeEventSource gigGossipNodeEventSource = new GigGossipNodeEventSource();
    QuerySearcher querySearcher;
    ForwardGeocoder forwardGeocoder;

    public RideShareCLIApp(ILogger logger, string id, string script, IConfigurationRoot config)
    {
        if (id == null)
            id = Prompt.Input<string>("Node Id");

        this.settings = new Settings(id, script, config);
        this.logger = logger;

        var webInterface = new Nominatim.API.Web.NominatimWebInterface(new DefaultHttpClientFactory());
        this.querySearcher = new QuerySearcher(webInterface);
        this.forwardGeocoder = new ForwardGeocoder(webInterface);

        SecureStorage.InitializeDefault(
            settings.NodeSettings.SecureStorageConnectionString.
            Replace("$HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).
            Replace("$ID", id));

    }

    enum SecureStorageKeysEnum
    {
        PrivateKey,
        NodeMode,
        PhoneNumber,
    }

    public enum CommandEnum
    {
        [Display(Name = "Exit")]
        Exit,
        [Display(Name = "DriverMode")]
        DriverMode,
        [Display(Name = "RequestRide")]
        RequestRide,
    }

    public enum NodeModeEnum
    {
        [Display(Name = "Rider")]
        Rider,
        [Display(Name = "Router")]
        Router,
        [Display(Name = "Driver")]
        Driver,
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

    public async Task<NodeModeEnum> GetNodeModeAsync()
    {
        return Enum.Parse<NodeModeEnum>(await SecureStorage.Default.GetAsync(SecureStorageKeysEnum.NodeMode.ToString(), NodeModeEnum.Rider.ToString()));
    }

    public async Task SetNodeModeAsync(NodeModeEnum mode)
    {
        await SecureStorage.Default.SetAsync(SecureStorageKeysEnum.NodeMode.ToString(), mode.ToString());
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

    public async Task<Location> PickLocationAsync()
    {
        Dictionary<string, string> countryCodes;
        var country = Prompt.Select("Country", GetCountryList(out countryCodes));

        string address = null;
        while (true)
        {
            string query = Prompt.Input<string>("Address");
            var places = await querySearcher.Search(new SearchQueryRequest
            {
                CountryCodeSearch = countryCodes[country],
                queryString = query,
                DedupeResults = true
            });
            var potentials = new List<string>() { "<retry>" };
            potentials.AddRange(from p in places select p.DisplayName);
            var place = Prompt.Select("Please select", potentials);
            if (place != "<retry>")
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
            else if (cmd == CommandEnum.DriverMode)
            {
                var mode = await GetNodeModeAsync();
                logger.LogInformation("Current Node Mode is " + mode);
                var newMode = Prompt.Select<NodeModeEnum>("Select new mode");
                if (mode != newMode)
                    await SetNodeModeAsync(newMode);
            }
            else if (cmd == CommandEnum.RequestRide)
            {
                var mode = await GetNodeModeAsync();
                if (mode != NodeModeEnum.Rider)
                {
                    logger.LogWarning("Current Driver Mode is " + mode);
                }
                else
                {
                    logger.LogInformation("Pickup location");
                    var fromLocation = await PickLocationAsync();
                    logger.LogInformation("Dropoff location");
                    var toLocation = await PickLocationAsync();
                    var waitingTimeForPickupMinutes = Prompt.Input<int>("Waiting Time For Pickup In Minutes");
                    var btr = await RequestRide(fromLocation, toLocation, settings.NodeSettings.GeohashPrecision, waitingTimeForPickupMinutes);
                }
            }
        }
    }


    async Task StartAsync()
    {
        var privateKey = await GetPrivateKeyAsync();
        if (privateKey == null)
        {
            var mnemonic = Crypto.GenerateMnemonic().Split(" ");
            logger.LogInformation($"Initializing private key for {settings.Id}");
            logger.LogInformation(string.Join(" ", mnemonic));
            privateKey = Crypto.DeriveECPrivKeyFromMnemonic(string.Join(" ", mnemonic));
            await SetPrivateKeyAsync(privateKey);
        }
        else
        {
            logger.LogInformation($"Loading private key for {settings.Id}");
        }

        logger.LogInformation(privateKey.AsHex());

        var mode = await SecureStorage.Default.GetAsync(SecureStorageKeysEnum.NodeMode.ToString(), NodeModeEnum.Rider.ToString());
        logger.LogInformation("Node mode is " + mode);

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
            gigGossipNodeEventSource.GigGossipNodeEvents);

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
