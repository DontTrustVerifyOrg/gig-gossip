using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Threading;
using CryptoToolkit;
using GigGossipSettlerAPIClient;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using NBitcoin;
using NBitcoin.RPC;
using NBitcoin.Secp256k1;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Sharprompt;
using Spectre.Console;
using NGeoHash;
using GigGossip;

namespace GigGossipSettlerCLI;

public class GigGossipSettlerCLI
{

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

    UserSettings userSettings;
    ISettlerAPI settlerClient;
    IGigStatusClient gigStatusClient;
    IPreimageRevealClient preimageRevealClient;
    CancellationTokenSource CancellationTokenSource = new();

    static string[] AccessRightsLabels = new string[] {"Valid","KYC","Screening","Disputes","AccessCodes","AccessRights","Anonymous","ValidUser","KnownUser","Operator","Admin","Owner"};
    static string DefaultCountryCode = "AU";

    static IConfigurationRoot GetConfigurationRoot(string? basePath, string[] args, string defaultFolder, string iniName)
    {
        if (basePath == null)
        {
            basePath = Environment.GetEnvironmentVariable("GIGGOSSIP_BASEDIR");
            if (basePath == null)
                basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), defaultFolder);
        }
        var builder = new ConfigurationBuilder();
        builder.SetBasePath(basePath)
               .AddIniFile(iniName)
               .AddEnvironmentVariables()
               .AddCommandLine(args);

        return builder.Build();
    }

    public GigGossipSettlerCLI(string[] args, string baseDir, string sfx)
    {
        if (sfx == null)
            sfx = AnsiConsole.Prompt(new TextPrompt<string>("Enter the [orange1]config suffix[/]?").AllowEmpty());

        sfx = (string.IsNullOrWhiteSpace(sfx)) ? "" : "_" + sfx;

        IConfigurationRoot config = GetConfigurationRoot(baseDir, args, ".giggossip", "settlercli" + sfx + ".conf");

        this.userSettings = config.GetSection("user").Get<UserSettings>();

        var baseUrl = userSettings.GigSettlerOpenApi;
        settlerClient = new SettlerAPIRetryWrapper(baseUrl, new HttpClient(), new DefaultRetryPolicy());

        gigStatusClient = settlerClient.CreateGigStatusClient();
        preimageRevealClient = settlerClient.CreatePreimageRevealClient();
    }

    public enum CommandEnum
    {
        [Display(Name = "Exit App")]
        Exit,
        [Display(Name = "Refresh")]
        Refresh,
        [Display(Name = "Get CA Public Key")]
        GetCaPublicKey,
        [Display(Name = "My Public Key")]
        MyPublicKey,
        [Display(Name = "Is Certificate Revoked")]
        IsCertificateRevoked,
        [Display(Name = "Grant Access Rights")]
        GrantAccessRights,
        [Display(Name = "Revoke Access Rights")]
        RevokeAccessRights,
        [Display(Name = "Get Access Rights")]
        GetAccessRights,
        [Display(Name = "Address Autocomple")]
        AddressAutocomplete,
        [Display(Name = "Get Route")]
        GetRoute,
        [Display(Name = "Address Geocode")]
        AddressGeocode,
        [Display(Name = "Location Geocode")]
        LocationGeocode,
        [Display(Name = "Issue New Access Code")]
        IssueNewAccessCode,
        [Display(Name = "Validate Access Code")]
        ValidateAccessCode,
        [Display(Name = "Revoke Access Code")]
        RevokeAccessCode,
        [Display(Name = "Get Memo From Access Code")]
        GetMemoFromAccessCode,
        [Display(Name = "Give User Property")]
        GiveUserProperty,
        [Display(Name = "Give User File")]
        GiveUserFile,
        [Display(Name = "Revoke User Property")]
        RevokeUserProperty,
        [Display(Name = "Save User Trace Property")]
        SaveUserTraceProperty,
        [Display(Name = "Verify Channel")]
        VerifyChannel,
        [Display(Name = "Submit Channel Secret")]
        SubmitChannelSecret,
        [Display(Name = "Is Channel Verified")]
        IsChannelVerified,
        [Display(Name = "Generate Reply Payment Preimage")]
        GenerateReplyPaymentPreimage,
        [Display(Name = "Generate Related Preimage")]
        GenerateRelatedPreimage,
        [Display(Name = "Validate Related Payment Hashes")]
        ValidateRelatedPaymentHashes,
        [Display(Name = "Reveal Preimage")]
        RevealPreimage,
        [Display(Name = "Get Gig Status")]
        GetGigStatus,
//        [Display(Name = "Generate Request Payload")]
 //       GenerateRequestPayload,
 //       [Display(Name = "Generate Settlement Trust")]
  //      GenerateSettlementTrust,
        [Display(Name = "Encrypt Object For CertificateId")]
        EncryptObjectForCertificateId,
        [Display(Name = "Manage Dispute")]
        ManageDispute,
        [Display(Name = "Cancel Gig")]
        CancelGig,
        
    }

    public async Task<string> MakeToken()
    {
        var ecpriv = userSettings.UserPrivateKey.AsECPrivKey();
        string pubkey = ecpriv.CreateXOnlyPubKey().AsHex();
        var guid = SettlerAPIResult.Get<Guid>(await settlerClient.GetTokenAsync(pubkey, CancellationToken.None));
        return AuthToken.Create(ecpriv, DateTime.UtcNow, guid);
    }

    enum ClipType
    {
        Invoice = 0,
        PaymentHash = 1,
        Preimage = 2,
        CertificateId = 3,
        PublicKey = 4,
        AccessCode = 5,
        GigId = 6,
        RequestPayload = 7,
        SettlementTrust = 8,
        Object = 9,
        Geohash = 10,
        Address = 11,
    }

    private void ToClipboard(ClipType clipType, string value,bool push = false)
    {
        var clip = TextCopy.ClipboardService.GetText();
        if (string.IsNullOrWhiteSpace(clip) || !clip.StartsWith("GigGossipClipboard\n"))
        {
            var ini = new List<string>() { "GigGossipClipboard" };
            for (var i = 0; i <= Enum.GetValues(typeof(ClipType)).Cast<int>().Max(); i++)
                ini.Add("");
            clip = string.Join("\n", ini);
        }
        var clarr = clip.Split("\n");
        if(push)
            clarr[((int)clipType) + 1] = clarr[((int)clipType) + 1] +"\0" + value;
        else
            clarr[((int)clipType) + 1] = value;
        TextCopy.ClipboardService.SetText(string.Join("\n", clarr));
    }

    private string FromClipboard(ClipType clipType,bool pophead=false)
    {
        var clip = TextCopy.ClipboardService.GetText();
        if (string.IsNullOrWhiteSpace(clip) || !clip.StartsWith("GigGossipClipboard\n"))
            return clip;
        var clarr = clip.Split("\n");
        if(pophead)
        {
            var parts = clarr[((int)clipType) + 1].Split("\0");
            if (parts.Length == 0)
                return "";
            var ret = parts[0];
            clarr[((int)clipType) + 1] = string.Join("\0", parts.Skip(1));
            return ret;
        }
        else
            return clarr[((int)clipType) + 1];
    }

    Thread gigStatusMonitorThread;
    Thread preimageRevealMonitorThread;
    public async Task RunAsync()
    {
        gigStatusMonitorThread = new Thread(async () =>
        {
            await gigStatusClient.ConnectAsync(await MakeToken(), CancellationToken.None);
            try
            {
                await foreach (var gigupd in gigStatusClient.StreamAsync(await MakeToken(), CancellationTokenSource.Token))
                {
                    AnsiConsole.MarkupLine("[yellow]Gig Status:" + gigupd + "[/]");
                }
            }
            catch (OperationCanceledException)
            {
                //stream closed
                return;
            };
        });
        gigStatusMonitorThread.Start();

        preimageRevealMonitorThread = new Thread(async () =>
        {
            await preimageRevealClient.ConnectAsync(await MakeToken(), CancellationToken.None);
            try
            {
                await foreach (var preimageupd in preimageRevealClient.StreamAsync(await MakeToken(), CancellationTokenSource.Token))
                {
                    AnsiConsole.MarkupLine("[yellow]Preimage Reveal:" + preimageupd + "[/]");
                }
            }
            catch (OperationCanceledException)
            {
                //stream closed
                return;
            };
        });
        preimageRevealMonitorThread.Start();

        while (true)
        {
            try
            {
                var cmd = Prompt.Select<CommandEnum>("Select command", pageSize: 6);
                if (cmd == CommandEnum.Exit)
                {
                    if (cmd == CommandEnum.Exit)
                        break;
                }
                else if(cmd == CommandEnum.Refresh)
                {
                }
                else if(cmd == CommandEnum.GetCaPublicKey)
                {
                    var capubkey = SettlerAPIResult.Get<string>(await settlerClient.GetCaPublicKeyAsync(CancellationToken.None));
                    ToClipboard(ClipType.PublicKey, capubkey);
                    AnsiConsole.WriteLine(capubkey);                
                }
                else if (cmd == CommandEnum.MyPublicKey)
                {
                    var ecpriv = userSettings.UserPrivateKey.AsECPrivKey();
                    var pubkey = ecpriv.CreateXOnlyPubKey().AsHex();
                    ToClipboard(ClipType.PublicKey, pubkey);
                    AnsiConsole.WriteLine(pubkey);
                }
                else if(cmd == CommandEnum.IsCertificateRevoked)
                {
                    var certificateId = Prompt.Input<Guid>("Enter certificate ID");
                    var isRevoked = SettlerAPIResult.Get<bool>(await settlerClient.IsCertificateRevokedAsync(certificateId, CancellationToken.None));
                    AnsiConsole.WriteLine(isRevoked? "Revoked" : "Valid");
                }
                else if(cmd == CommandEnum.GrantAccessRights)
                {
                    var pubkey = Prompt.Input<string>("Enter user Public Key", FromClipboard(ClipType.PublicKey));
                    var curAccessRights = SettlerAPIResult.Get<string>(await settlerClient.GetAccessRightsAsync(await MakeToken(), pubkey, CancellationToken.None));
                    var accessRights = Prompt.MultiSelect("Enter access rights", AccessRightsLabels,defaultValues: curAccessRights.Split(","));
                    SettlerAPIResult.Check(await settlerClient.GrantAccessRightsAsync(await MakeToken(), pubkey, string.Join(", ", accessRights), CancellationToken.None));
                    AnsiConsole.WriteLine("Granted");
                }
                else if(cmd == CommandEnum.RevokeAccessRights)
                {
                    var pubkey = Prompt.Input<string>("Enter user Public Key", FromClipboard(ClipType.PublicKey));
                    var curAccessRights = SettlerAPIResult.Get<string>(await settlerClient.GetAccessRightsAsync(await MakeToken(), pubkey, CancellationToken.None));
                    var accessRights = Prompt.MultiSelect("Enter access rights", AccessRightsLabels,defaultValues: curAccessRights.Split(","));
                    SettlerAPIResult.Check(await settlerClient.RevokeAccessRightsAsync(await MakeToken(), pubkey, string.Join(", ", accessRights), CancellationToken.None));
                    AnsiConsole.WriteLine("Revoked");
                }
                else if(cmd == CommandEnum.GetAccessRights)
                {
                    var pubkey = Prompt.Input<string>("Enter user Public Key", FromClipboard(ClipType.PublicKey));
                    var accessRights = SettlerAPIResult.Get<string>(await settlerClient.GetAccessRightsAsync(await MakeToken(), pubkey, CancellationToken.None));
                    AnsiConsole.WriteLine(accessRights);
                }
                else if(cmd ==CommandEnum.AddressAutocomplete)
                {
                    var country = Prompt.Input<string>("Country",DefaultCountryCode);
                    var query = Prompt.Input<string>("Start writing address");
                    var suggestions = SettlerAPIResult.Get<List<string>>(await settlerClient.AddressAutocompleteAsync(await MakeToken(), query, country, CancellationToken.None));
                    DrawTable(new string[] { "Suggestion" }, (from s in suggestions select new string[] { s }).ToArray());
                }
                else if(cmd == CommandEnum.GetRoute)
                {
                    var fromGh = Prompt.Input<string>("From Geohash",FromClipboard(ClipType.Geohash,pophead:true));
                    var toGh = Prompt.Input<string>("To Geohash",FromClipboard(ClipType.Geohash,pophead:true));
                    var from = GeoHash.Decode(fromGh);
                    var to = GeoHash.Decode(toGh);
                    var route = SettlerAPIResult.Get<RouteRet>(await settlerClient.GetRouteAsync(await MakeToken(), from.Coordinates.Lat, from.Coordinates.Lon, to.Coordinates.Lat,to.Coordinates.Lon, CancellationToken.None));
                    AnsiConsole.WriteLine($"Route from {fromGh} to {toGh} is {route.Distance} meters long");
                }
                else if(cmd == CommandEnum.AddressGeocode)
                {
                    var country = Prompt.Input<string>("Country",DefaultCountryCode);
                    var address = Prompt.Input<string>("Enter address");
                    var geocode = SettlerAPIResult.Get<GeolocationRet>(await settlerClient.AddressGeocodeAsync(await MakeToken(), address, country, CancellationToken.None));
                    AnsiConsole.WriteLine($"Geocode for {address} is {geocode.Lat},{geocode.Lon}");
                    var geohash = GeoHash.Encode(geocode.Lat, geocode.Lon);
                    AnsiConsole.WriteLine($"Geohash for {address} is {geohash}");
                    ToClipboard(ClipType.Geohash, geohash,  push:true);
                }
                else if(cmd == CommandEnum.LocationGeocode)
                {
                    var geohash = Prompt.Input<string>("Enter Geohash");
                    var geocode = GeoHash.Decode(geohash);
                    var address = SettlerAPIResult.Get<string>(await settlerClient.LocationGeocodeAsync(await MakeToken(), geocode.Coordinates.Lat,geocode.Coordinates.Lon, CancellationToken.None));
                    AnsiConsole.WriteLine($"Geocode for {geohash} is {geocode.Coordinates.Lat},{geocode.Coordinates.Lon}");
                    AnsiConsole.WriteLine($"Address for {geohash} is {address}");
                    ToClipboard(ClipType.Address, address);
                }
                else if(cmd == CommandEnum.IssueNewAccessCode)
                {
                    var length = Prompt.Input<int>("Length", defaultValue: 6);
                    var singleUse = Prompt.Confirm("Single use?", defaultValue: false);
                    var validTillMin = Prompt.Input<int>("Valid till (minutes):",defaultValue:60);
                    var memo = Prompt.Input<string>("Memo:");
                    var accessCode = SettlerAPIResult.Get<string>(await settlerClient.IssueNewAccessCodeAsync(await MakeToken(),length, singleUse, validTillMin, memo, CancellationToken.None));
                    AnsiConsole.WriteLine(accessCode);
                    ToClipboard(ClipType.AccessCode, accessCode);
                }
                else if(cmd == CommandEnum.ValidateAccessCode)
                {
                    var accessCode = Prompt.Input<string>("Enter access code:", FromClipboard(ClipType.AccessCode));
                    var isValid = SettlerAPIResult.Get<bool>(await settlerClient.ValidateAccessCodeAsync(await MakeToken(), accessCode, CancellationToken.None));
                    AnsiConsole.WriteLine($"Access code {accessCode} is {(isValid?"Valid":"Invalid")}");
                }
                else if(cmd == CommandEnum.RevokeAccessCode)
                {
                    var accessCode = Prompt.Input<string>("Enter access code:", FromClipboard(ClipType.AccessCode));
                    SettlerAPIResult.Check(await settlerClient.RevokeAccessCodeAsync(await MakeToken(), accessCode, CancellationToken.None));
                    AnsiConsole.WriteLine($"Access code revoked");
                }
                else if(cmd == CommandEnum.GetMemoFromAccessCode)
                {
                    var accessCode = Prompt.Input<string>("Enter access code:", FromClipboard(ClipType.AccessCode));
                    var memo = SettlerAPIResult.Get<string>(await settlerClient.GetMemoFromAccessCodeAsync(await MakeToken(), accessCode, CancellationToken.None));
                    AnsiConsole.WriteLine(memo);
                }
                else if(cmd == CommandEnum.GiveUserProperty)
                {
                    var pubkey = Prompt.Input<string>("Enter user Public Key:");
                    var property = Prompt.Input<string>("Enter property:");
                    var value = Prompt.Input<string>("Enter value:");
                    var secret = Prompt.Input<string>("Enter secret:");
                    var validHours = Prompt.Input<int>("Valid for (hours):",defaultValue:24);
                    SettlerAPIResult.Check(await settlerClient.GiveUserPropertyAsync(await MakeToken(), pubkey, property, value, secret, validHours, CancellationToken.None)); 
                    AnsiConsole.WriteLine($"Property {property} given to user {pubkey}");
                }
                else if(cmd == CommandEnum.GiveUserFile)
                {
                    var pubkey = Prompt.Input<string>("Enter user Public Key:");
                    var property = Prompt.Input<string>("Enter property:");
                    var valuePath = Prompt.Input<string>("Enter path to file:");
                    var valueStream = new FileStream(valuePath, FileMode.Open, FileAccess.Read);
                    var secretPath = Prompt.Input<string>("Enter path to secret:");
                    var secretStream = new FileStream(secretPath, FileMode.Open, FileAccess.Read);
                    var validHours = Prompt.Input<int>("Valid for (hours):",defaultValue:24);
                    SettlerAPIResult.Check(await settlerClient.GiveUserFileAsync(await MakeToken(), pubkey, property, validHours, new FileParameter(valueStream), new FileParameter(secretStream), CancellationToken.None)); 
                    AnsiConsole.WriteLine($"File {property} given to user {pubkey}");
                }
                else if(cmd == CommandEnum.RevokeUserProperty)
                {
                    var pubkey = Prompt.Input<string>("Enter user Public Key:");
                    var property = Prompt.Input<string>("Enter property:");
                    SettlerAPIResult.Check(await settlerClient.RevokeUserPropertyAsync(await MakeToken(), pubkey, property, CancellationToken.None));
                    AnsiConsole.WriteLine($"Property {property} revoked from user {pubkey}");

                }
                else if(cmd == CommandEnum.SaveUserTraceProperty)
                {
                    var pubkey = Prompt.Input<string>("Enter user Public Key:");
                    var property = Prompt.Input<string>("Enter property:");
                    var value = Prompt.Input<string>("Enter value:");
                    SettlerAPIResult.Check(await settlerClient.SaveUserTracePropertyAsync(await MakeToken(), pubkey, property, value, CancellationToken.None)); 
                    AnsiConsole.WriteLine($"Trace Property {property} saved for user {pubkey}");
                }
                else if(cmd == CommandEnum.VerifyChannel)
                {
                    var pubkey = Prompt.Input<string>("Enter user Public Key:");
                    var property = Prompt.Input<string>("Enter property:");
                    var method = Prompt.Input<string>("Enter method:");
                    var value = Prompt.Input<string>("Enter value:");
                    SettlerAPIResult.Check(await settlerClient.VerifyChannelAsync(await MakeToken(), pubkey, property, method, value, CancellationToken.None));
                    AnsiConsole.WriteLine($"Channel verification for user {pubkey} started");
                }
                else if(cmd == CommandEnum.SubmitChannelSecret)
                {
                    var pubkey = Prompt.Input<string>("Enter user Public Key:");
                    var property = Prompt.Input<string>("Enter property:");
                    var method = Prompt.Input<string>("Enter method:");
                    var value = Prompt.Input<string>("Enter value:");
                    var secret = Prompt.Input<string>("Enter secret:");
                    var retriesLeft = SettlerAPIResult.Get<int>(await settlerClient.SubmitChannelSecretAsync(await MakeToken(), pubkey, property, method, value, secret, CancellationToken.None));
                    if(retriesLeft == -1)
                        AnsiConsole.WriteLine($"Channel verification for user {pubkey} succeeded");
                    else
                        AnsiConsole.WriteLine($"Channel verification for user {pubkey} failed. You have {retriesLeft} retries left");
                }
                else if(cmd == CommandEnum.IsChannelVerified)
                {
                    var pubkey = Prompt.Input<string>("Enter user Public Key:");
                    var property = Prompt.Input<string>("Enter property:");
                    var value = Prompt.Input<string>("Enter value:");
                    var isVerified = SettlerAPIResult.Get<bool>(await settlerClient.IsChannelVerifiedAsync(await MakeToken(), pubkey, property, value, CancellationToken.None));
                    AnsiConsole.WriteLine($"Channel for user {pubkey} is {(isVerified?"Verified":"Not Verified")}");
                }
                else if(cmd == CommandEnum.GenerateReplyPaymentPreimage)
                {
                    var gigId = Prompt.Input<Guid>("Enter GigId:");
                    var replierpubkey = Prompt.Input<string>("Enter replier Public Key:");
                    var preimage = SettlerAPIResult.Get<string>(await settlerClient.GenerateReplyPaymentPreimageAsync(await MakeToken(), gigId, replierpubkey, CancellationToken.None));
                    AnsiConsole.WriteLine(preimage);
                    ToClipboard(ClipType.Preimage, preimage);
                }
                else if(cmd == CommandEnum.GenerateRelatedPreimage)
                {
                    var paymentHash = Prompt.Input<string>("Enter Payment Hash:");
                    var preimage = SettlerAPIResult.Get<string>(await settlerClient.GenerateRelatedPreimageAsync(await MakeToken(), paymentHash, CancellationToken.None));
                    AnsiConsole.WriteLine(preimage);
                    ToClipboard(ClipType.Preimage, preimage);
                }
                else if(cmd == CommandEnum.ValidateRelatedPaymentHashes)
                {
                    var paymentHash1 = Prompt.Input<string>("Enter Payment Hash 1:");
                    var paymentHash2 = Prompt.Input<string>("Enter Payment Hash 2:");
                    var isValid = SettlerAPIResult.Get<bool>(await settlerClient.ValidateRelatedPaymentHashesAsync(await MakeToken(), paymentHash1, paymentHash2, CancellationToken.None));
                    AnsiConsole.WriteLine($"Payment Hashes are {(isValid?"Related":"Not Related")}");
                }
                else if(cmd == CommandEnum.RevealPreimage)
                {
                    var paymentHash = Prompt.Input<string>("Enter Payment Hash:");
                    var preimage = SettlerAPIResult.Get<string>(await settlerClient.RevealPreimageAsync(await MakeToken(), paymentHash, CancellationToken.None));
                    AnsiConsole.WriteLine(preimage);
                    ToClipboard(ClipType.Preimage, preimage);
                }
                else if(cmd == CommandEnum.GetGigStatus)
                {
                    var signedRequestPayloadId = Prompt.Input<Guid>("Enter signed Request Payload Id:");
                    var replierCertificateId = Prompt.Input<Guid>("Enter replier Certificate Id:");
                    var gigStatus = SettlerAPIResult.Get<string>(await settlerClient.GetGigStatusAsync(await MakeToken(), signedRequestPayloadId, replierCertificateId, CancellationToken.None));
                    AnsiConsole.WriteLine($"Gig Status: {gigStatus}");
                }
                else if(cmd == CommandEnum.EncryptObjectForCertificateId)
                {
                    var certificateId = Prompt.Input<Guid>("Enter Certificate Id:");
                    var obj = Prompt.Input<string>("Enter path to object:");
                    var objStream = new FileStream(obj, FileMode.Open, FileAccess.Read);
                    var encrypted = SettlerAPIResult.Get<string>(await settlerClient.EncryptObjectForCertificateIdAsync(certificateId, new FileParameter(objStream), CancellationToken.None));
                }
                else if(cmd == CommandEnum.ManageDispute)
                {
                    var gigId = Prompt.Input<Guid>("Enter GigId:");
                    var replierCertificateId = Prompt.Input<Guid>("Enter replier Certificate Id:");
                    var dispSel = Prompt.Select("What to do", new[] { "Open Dispute", "Close Dispute" })=="Open Dispute";
                    SettlerAPIResult.Check(await settlerClient.ManageDisputeAsync(await MakeToken(), gigId, replierCertificateId, dispSel, CancellationToken.None));
                    AnsiConsole.WriteLine("dispute " + (dispSel?"opened":"closed"));
                }
                else if(cmd == CommandEnum.CancelGig)
                {
                    var gigId = Prompt.Input<Guid>("Enter GigId:");
                    var replierCertificateId = Prompt.Input<Guid>("Enter replier Certificate Id:");
                    SettlerAPIResult.Check(await settlerClient.CancelGigAsync(await MakeToken(), gigId,replierCertificateId, CancellationToken.None));
                    AnsiConsole.WriteLine("Gig cancelled");
                }
                else
                {
                    AnsiConsole.WriteLine("Unknown command");
                }

            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex,
                    ExceptionFormats.ShortenPaths | ExceptionFormats.ShortenTypes |
                    ExceptionFormats.ShortenMethods | ExceptionFormats.ShowLinks);
            }
        }
        CancellationTokenSource.Cancel();
        gigStatusMonitorThread.Join();
        preimageRevealMonitorThread.Join();
    }

    private void DrawTable(string[] columnNames, string[][] rows)
    {
        var table = new Table()
            .Border(TableBorder.Rounded);
        foreach (var c in columnNames)
            table = table.AddColumn(c);
        foreach (var row in rows)
            table = table.AddRow(row);
        AnsiConsole.Write(table);
    }
}

public class UserSettings
{
    public required string GigSettlerOpenApi { get; set; }
    public required string UserPrivateKey { get; set; }
}