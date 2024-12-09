
using GigGossipSettler;
using GigGossipSettler.Exceptions;
using GigGossipSettlerAPI;
using GigLNDWalletAPIClient;
using NBitcoin.Secp256k1;
using System.Text;
using Spectre.Console;
using Microsoft.AspNetCore.Mvc;
using TraceExColor;
using Quartz.Spi;
using Microsoft.AspNetCore.DataProtection;
using NBitcoin;
using Newtonsoft.Json.Linq;
using System.Xml.Linq;
using GoogleApi.Entities.Places.QueryAutoComplete.Request;
using GoogleApi.Entities.Places.Details.Request;
using GoogleApi;
using GoogleApi.Entities.Places.AutoComplete.Request;
using GoogleApi.Entities.Maps.Geocoding.Common.Enums;
using NBitcoin.Protocol;
using Microsoft.AspNetCore.SignalR.Client;
using NetworkToolkit;
using GoogleApi.Entities.Maps.Directions.Request;
using GoogleApi.Entities.Maps.Common;
using GigGossip;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Interfaces;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Writers;
using System.Text.Json;
using Enum = System.Enum;
using Type = System.Type;
using System.Text.Json.Nodes;
using System;

#pragma warning disable 1591

TraceEx.TraceInformation("[[lime]]Starting[[/]] ...");

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.EnableAnnotations();
    c.UseAllOfToExtendReferenceSchemas();
    c.DocumentFilter<CustomModelDocumentFilter<PreimageReveal>>();
    c.DocumentFilter<CustomModelDocumentFilter<GigStatusKey>>();
    c.SchemaFilter<NSwagEnumExtensionSchemaFilter>();
    c.SchemaFilter<EnumFilter>();
});
builder.Services.AddSignalR();
builder.Services.AddProblemDetails();
var app = builder.Build();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//app.UseHttpsRedirection();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHsts();


IConfigurationRoot GetConfigurationRoot(string defaultFolder, string iniName)
{
    var basePath = Environment.GetEnvironmentVariable("GIGGOSSIP_BASEDIR");
    if (basePath == null)
        basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), defaultFolder);
    foreach (var arg in args)
        if (arg.StartsWith("--basedir"))
            basePath = arg.Substring(arg.IndexOf('=') + 1).Trim().Replace("\"", "").Replace("\'", "");
        else if (arg.StartsWith("--cfg"))
            iniName = arg.Substring(arg.IndexOf('=') + 1).Trim().Replace("\"", "").Replace("\'", "");

    var builder = new ConfigurationBuilder();
    builder.SetBasePath(basePath)
           .AddIniFile(iniName)
           .AddEnvironmentVariables()
           .AddCommandLine(args);

    return builder.Build();
}

var config = GetConfigurationRoot(".giggossip", "settler.conf");
var settlerSettings = config.GetSection("settler").Get<SettlerSettings>();

Dictionary<(string, string), CaPricing> pricings = new();
foreach (var prcname in settlerSettings.GetPricings())
{
    var prc = config.GetSection(prcname).Get<CaPricing>();
    pricings.Add((prc.CountryCode, prc.Currency), prc);
}

ECPrivKey caPrivateKey = settlerSettings.SettlerPrivateKey.AsECPrivKey();

var httpClient = new HttpClient();
var retryPolicy = new DefaultRetryPolicy();
var lndWalletClient = new swaggerClient(settlerSettings.GigWalletOpenApi.AbsoluteUri, httpClient, new DefaultRetryPolicy());

Singlethon.Settler = new Settler(
    settlerSettings.ServiceUri, 
    new SimpleSettlerSelector(httpClient, retryPolicy),
    caPrivateKey, pricings,
    TimeSpan.FromSeconds(settlerSettings.InvoicePaymentTimeoutSec),
    TimeSpan.FromSeconds(settlerSettings.DisputeTimeoutSec),
    new DefaultRetryPolicy(),
    settlerSettings.FirebaseAdminConfBase64
    );

await Singlethon.Settler.InitAsync(
    lndWalletClient,
    Enum.Parse<DBProvider>(settlerSettings.DBProvider),
    settlerSettings.ConnectionString.Replace("$HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
    settlerSettings.OwnerPublicKey
    );

Singlethon.Settler.OnGigStatus += (sender, e) =>
    {
        foreach (var asyncCom in Singlethon.GigStatusAsyncComQueue4ConnectionId.Values)
            asyncCom.Enqueue(e);
    };

Singlethon.Settler.OnPreimageReveal+= (sender, e) =>
    {
        foreach (var asyncCom in Singlethon.PreimagesAsyncComQueue4ConnectionId.Values)
            asyncCom.Enqueue(e);
    };

await Singlethon.Settler.StartAsync();

TraceEx.TraceInformation("... Running");

app.MapGet("/getcapricing", (string country, string currency) =>
{
    try
    {
        if (!pricings.ContainsKey((country, currency)))
            throw new SettlerException(SettlerErrorCode.NotSupportedCountryCurrencyPair);

        return new Result<CaPricing>(pricings[(country, currency)]);
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<CaPricing>(ex);
    }
})
.WithName("GetCaPricing")
.WithSummary("Get pricing for specific country and currency")
.WithDescription("Returns the pricing details for a specific country and currency.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Country";
    g.Parameters[1].Description = "Currency";
    return g;
}).DisableAntiforgery();


app.MapGet("/getcapublickey", () =>
{
    try
    { 
        return new Result<string>(Singlethon.Settler.CaXOnlyPublicKey.AsHex());
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<string>(ex);
    }
})
.WithName("GetCaPublicKey")
.WithSummary("Public key of this Certification Authority.")
.WithDescription("Public key of this Certification Authority that can be used to validate signatures of e.g. issued certificates.")
.WithOpenApi()
.DisableAntiforgery();


app.MapGet("/iscertificaterevoked", (Guid certid) =>
{
    try
    {
        return new Result<bool>(Singlethon.Settler.IsCertificateRevoked(certid));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<bool>(ex);
    }
})
.WithName("IsCertificateRevoked")
.WithSummary("Is the certificate revoked by this Certification Authority.")
.WithDescription("Returns true if the certificate has been revoked, false otherwise. Usefull to implement revocation list.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Serial number of the certificate.";
    return g;
})
.DisableAntiforgery();


app.MapGet("/gettoken", (string pubkey) =>
{
    try
    {
        return new Result<Guid>(Singlethon.Settler.GetTokenGuid(pubkey));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<Guid>(ex);
    }
})
.WithName("GetToken")
.WithSummary("Creates authorisation token guid")
.WithDescription("Creates a new token Guid that is used for further communication with the API")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "public key identifies the API user";
    return g;
})
.DisableAntiforgery();

app.MapGet("/deletemypersonaluserdata", (string authToken) =>
{
    try
    {
        var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
        Singlethon.Settler.DeletePersonalUserData(pubkey);
        return new Result();
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result(ex);
    }
})
.WithName("DeleteMyPersonalUserData")
.WithSummary("Deletes My Personal Information.")
.WithDescription("Deletes My Personal Information.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user.";
    return g;
})
.DisableAntiforgery();


app.MapGet("/addressautocomplete", async (string authToken, string query, string country)=>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken);

        var request = new PlacesAutoCompleteRequest
        {
            Key = settlerSettings.GoogleMapsAPIKey,
            Input = query,
            Language = GoogleApi.Entities.Common.Enums.Language.English,
            RestrictType = GoogleApi.Entities.Places.AutoComplete.Request.Enums.RestrictPlaceType.Address,
            Components = new Dictionary<GoogleApi.Entities.Common.Enums.Component, string>() { { GoogleApi.Entities.Common.Enums.Component.Country, country } },
        };

        var response = await GooglePlaces.AutoComplete.QueryAsync(request);
        return new Result<string[]>((from p in response.Predictions select p.Description).ToArray());
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<string[]>(ex);
    }
})
.WithName("AddressAutocomplete")
.WithSummary("Autocompletes the address")
.WithDescription("Autocompletes the address.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.";
    g.Parameters[1].Description = "Query";
    g.Parameters[2].Description = "Country";
    return g;
})
.DisableAntiforgery();


app.MapGet("/getroute", async (string authToken, double fromLat, double fromLon, double toLat, double toLon) =>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken);

        var request = new DirectionsRequest
        {
            Key = settlerSettings.GoogleMapsAPIKey,
            Origin = new LocationEx(new CoordinateEx(fromLat, fromLon)),
            Destination = new LocationEx(new CoordinateEx(toLat, toLon)),
            TravelMode = GoogleApi.Entities.Maps.Common.Enums.TravelMode.DRIVING,
            Units = GoogleApi.Entities.Maps.Common.Enums.Units.Metric,
            Alternatives = false,
        };

        var response = await GoogleMaps.Directions.QueryAsync(request);

        var route = response.Routes.FirstOrDefault();
        if (route == null)
            throw new NotFoundException();

        var leg = route.Legs.FirstOrDefault();
        if (leg == null)
            throw new NotFoundException();

        return new Result<RouteRet>(
            new RouteRet {
                Distance = leg.Distance.Value,
                Duration = leg.Duration.Value,
                Geometry = (from pt in route.OverviewPath.Line select new GeolocationRet { Lat = pt.Latitude, Lon = pt.Longitude }).ToArray()
            });
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<RouteRet>(ex);
    }
})
.WithName("GetRoute")
.WithSummary("Return route beetween two points")
.WithDescription("Return route beetween two points.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.";
    g.Parameters[1].Description = "Latitude of the first point";
    g.Parameters[2].Description = "Longitude of the first point";
    g.Parameters[3].Description = "Latitude of the second point";
    g.Parameters[4].Description = "Longitude of the second point";
    return g;
})
.DisableAntiforgery();

app.MapGet("/addressgeocode", async (string authToken, string address, string country) =>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken);

        var response = await GoogleMaps.Geocode.AddressGeocode.QueryAsync(new GoogleApi.Entities.Maps.Geocoding.Address.Request.AddressGeocodeRequest() {
            Key = settlerSettings.GoogleMapsAPIKey,
            Address = address,
            Language = GoogleApi.Entities.Common.Enums.Language.English, 
            Components = new Dictionary<GoogleApi.Entities.Common.Enums.Component, string>() { { GoogleApi.Entities.Common.Enums.Component.Country, country } },
        });
        var loc = response.Results.FirstOrDefault();
        if (loc == null)
            throw new NotFoundException();
        return new Result<GeolocationRet>(new GeolocationRet { Lat = loc.Geometry.Location.Latitude, Lon = loc.Geometry.Location.Longitude });
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<GeolocationRet>(ex);
    }
})
.WithName("AddressGeocode")
.WithSummary("Finds the geolocation of the address")
.WithDescription("Finds the geolocation of the address.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.";
    g.Parameters[1].Description = "Address";
    g.Parameters[2].Description = "Country";
    return g;
})
.DisableAntiforgery();


app.MapGet("/locationgeocode", async (string authToken, double lat, double lon) =>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken);

        var response = await GoogleMaps.Geocode.LocationGeocode.QueryAsync(new  GoogleApi.Entities.Maps.Geocoding.Location.Request.LocationGeocodeRequest()
        {
            Key = settlerSettings.GoogleMapsAPIKey,
            Location = new GoogleApi.Entities.Common.Coordinate(lat, lon),
            LocationTypes = new List<GeometryLocationType>() {  GeometryLocationType.Rooftop }
        });
        var loc = response.Results.FirstOrDefault();
        if (loc == null)
            throw new NotFoundException();
        return new Result<string>(loc.FormattedAddress);
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<string>(ex);
    }
})
.WithName("LocationGeocode")
.WithSummary("Finds the address of the location")
.WithDescription("Finds the address of the location.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.";
    g.Parameters[1].Description = "Lat";
    g.Parameters[2].Description = "Lon";
    return g;
})
.DisableAntiforgery();

app.MapGet("/issuenewaccesscode", async (string authToken, int length, bool singleUse, long validTillMin, string Memo) =>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken, true);
        var code = Singlethon.Settler.IssueNewAccessCode(length, singleUse, validTillMin, Memo);
        return new Result<string>(code);
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<string>(ex);
    }
})
.WithName("IssueNewAccessCode")
.WithSummary("Issuse new access code")
.WithDescription("Issuse new access code.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.";
    g.Parameters[1].Description = "Required length of the access code";
    g.Parameters[2].Description = "Is Single Use";
    g.Parameters[3].Description = "Valid till";
    g.Parameters[4].Description = "Memo";
    return g;
})
.DisableAntiforgery();

app.MapGet("/validateaccesscode", async (string authToken, string code) =>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken);
        if(code=="ABCD")
            return new Result<bool>(true);
        return new Result<bool>(Singlethon.Settler.ValidateAccessCode(code));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<bool>(ex);
    }
})
.WithName("ValidateAccessCode")
.WithSummary("Validate access code")
.WithDescription("Validate access code.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.";
    g.Parameters[1].Description = "Access code identifier";
    return g;
})
.DisableAntiforgery();

app.MapGet("/revokeaccesscode", async (string authToken, string code) =>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken, true);
        Singlethon.Settler.RevokeAccessCode(code);
        return new Result();
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result(ex);
    }
})
.WithName("RevokeAccessCode")
.WithSummary("Revoke access code")
.WithDescription("Revoke access code.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.";
    g.Parameters[1].Description = "Access code identifier";
    return g;
})
.DisableAntiforgery();

app.MapGet("/getmemofromaccesscode", async (string authToken, string code) =>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken, true);
        if(code=="ABCD")
            return new Result<string>("general access code");
        return new Result<string>(Singlethon.Settler.GetMemoFromAccessCode(code));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<string>(ex);
    }
})
.WithName("GetMemoFromAccessCode")
.WithSummary("Get Memo from access code")
.WithDescription("Get Memo from access code.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.";
    g.Parameters[1].Description = "Access code identifier";
    return g;
})
.DisableAntiforgery();

app.MapGet("/giveuserproperty", (string authToken, string pubkey, string name, string value, string secret, long validHours) =>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken);
        Singlethon.Settler.GiveUserProperty(pubkey, name, Convert.FromBase64String(value), Convert.FromBase64String(secret), DateTime.Now.AddHours(validHours));
        return new Result();
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result(ex);
    }
})
.WithName("GiveUserProperty")
.WithSummary("Grants a property to the subject.")
.WithDescription("Grants a property to the subject (e.g. driving licence). Only authorised users can grant the property.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.";
    g.Parameters[1].Description = "Public key of the subject.";
    g.Parameters[2].Description = "Name of the property.";
    g.Parameters[3].Description = "Public value of the property.";
    g.Parameters[4].Description = "Secret value of the property.";
    g.Parameters[5].Description = "How long the property is valid in hours";
    return g;
})
.DisableAntiforgery();

app.MapGet("/getmypropertyvalue", (string authToken, string name) =>
{
    try
    {
        var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
        var prop = Singlethon.Settler.GetUserProperty(pubkey, name);
        if(prop!=null)
            return new Result<string>(Convert.ToBase64String(prop.Value));
        else
            return new Result<string>((string)null);
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<string>(ex);
    }
})
.WithName("GetMyPropertyValue")
.WithSummary("Gets My Property Value")
.WithDescription("Gets a property given to the subject. Only subject can read it.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.";
    g.Parameters[1].Description = "Name of the property.";
    return g;
})
.DisableAntiforgery();

app.MapGet("/getmypropertysecret", (string authToken, string name) =>
{
    try
    {
        var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
        var prop = Singlethon.Settler.GetUserProperty(pubkey, name);
        if(prop!=null)
            return new Result<string>(Convert.ToBase64String(prop.Secret));
        else
            return new Result<string>((string)null);
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<string>(ex);
    }
})
.WithName("GetMyPropertySecret")
.WithSummary("Gets My Property Secret")
.WithDescription("Gets a property secret given to the subject. Only subject can read it property.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.";
    g.Parameters[1].Description = "Name of the property.";
    return g;
})
.DisableAntiforgery();

app.MapGet("/getuserpropertyvalue", (string authToken, string pubkey, string name) =>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken, true);
        var prop = Singlethon.Settler.GetUserProperty(pubkey, name);
        if(prop!=null)
            return new Result<string>(Convert.ToBase64String(prop.Value));
        else
            return new Result<string>((string)null);
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<string>(ex);
    }
})
.WithName("GetUserPropertyValue")
.WithSummary("Gets User Property Value")
.WithDescription("Gets a property given to the subject. Only admin can read it.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.";
    g.Parameters[1].Description = "Pubkey of the user";
    g.Parameters[2].Description = "Name of the property.";
    return g;
})
.DisableAntiforgery();

app.MapGet("/getuserpropertysecret", (string authToken, string pubkey, string name) =>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken, true);
        var prop = Singlethon.Settler.GetUserProperty(pubkey, name);
        if(prop!=null)
            return new Result<string>(Convert.ToBase64String(prop.Secret));
        else
            return new Result<string>((string)null);
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<string>(ex);
    }
})
.WithName("GetUserPropertySecret")
.WithSummary("Gets User Property Secret")
.WithDescription("Gets a property secret given to the subject. Only admin can read this property.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.";
    g.Parameters[1].Description = "Pubkey of the user.";
    g.Parameters[2].Description = "Name of the property.";
    return g;
})
.DisableAntiforgery();

app.MapPost("/giveuserfile", async ([FromForm] string authToken, [FromForm] string pubkey, [FromForm] string name, [FromForm] long validHours, IFormFile value, IFormFile secret)
    =>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken.ToString());
        Singlethon.Settler.GiveUserProperty(pubkey, name, await value.ToBytes(), await secret.ToBytes(), DateTime.Now.AddHours(validHours));
        return new Result();
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result(ex);
    }
})
.WithName("GiveUserFile")
.WithSummary("Grants a file property to the subject.")
.WithDescription("Grants a file property to the subject (e.g. driving licence). Only authorised users can grant the property.")
.DisableAntiforgery();

app.MapGet("/revokeuserproperty", (string authToken, string pubkey, string name) =>
{
    try
    { 
        Singlethon.Settler.ValidateAuthToken(authToken, true);
        Singlethon.Settler.RevokeUserProperty(pubkey, name);
        return new Result();
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result(ex);
    }
})
.WithName("RevokeUserPropertyAsync")
.WithDescription("Revokes a property from the subject (e.g. driving licence is taken by the police). Only authorised users can revoke the property.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.";
    g.Parameters[1].Description = "Public key of the subject.";
    g.Parameters[2].Description = "Name of the property.";
    return g;
})
.DisableAntiforgery();

app.MapGet("/saveusertraceproperty", (string authToken, string pubkey, string name, string value) =>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken);
        Singlethon.Settler.SaveUserTraceProperty(pubkey, name, Convert.FromBase64String(value));
        return new Result();
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result(ex);
    }
})
.WithName("SaveUserTraceProperty")
.WithSummary("Saves a trace to the subject.")
.WithDescription("Saves a trace, Only authorised users can grant the property.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.";
    g.Parameters[1].Description = "Public key of the subject.";
    g.Parameters[2].Description = "Name of the property.";
    g.Parameters[3].Description = "Public value of the property.";
    return g;
})
.DisableAntiforgery();

app.MapGet("/verifychannel", async (string authToken, string pubkey, string name, string method, string value) =>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken);
        if (name.ToLower() == "phonenumber" && method.ToLower() == "sms")
        {
            var creds = settlerSettings.SMSGlobalAPIKeySecret.Split(":");
            var client = new SMSGlobal.api.Client(new SMSGlobal.api.Credentials(creds[0], creds[1]));
            var code = Random.Shared.NextInt64(999999).ToString("000000");
            var resp = await client.SMS.SMSSend(new
            {
                origin = "Fairide",
                destination = value,
                message = "Welcome to Fairide Security Center, your verification code is " + code + ". Use this to complete your registration.",
            });
            if (resp.statuscode != 200)
                throw new InvalidOperationException("SMS sending failed");
            Singlethon.channelSmsCodes.TryAdd(new ChannelKey { PubKey = pubkey, Channel = value }, new ChannelVal { Code = code, Retries = settlerSettings.SMSCodeRetryNumber, Deadline=DateTime.UtcNow.AddMinutes(settlerSettings.SMSCodeTimeoutMin)  });
        }
        else
            throw new NotImplementedException();
        return new Result();
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result(ex);
    }
})
.WithName("VerifyChannel")
.WithSummary("Start verification of specific channel.")
.WithDescription("Starts verification of specific channel.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.";
    g.Parameters[1].Description = "Public key of the subject.";
    g.Parameters[2].Description = "Channel name (phone,email,...)";
    g.Parameters[3].Description = "Method (sms,call,message)";
    g.Parameters[4].Description = "Value of Channel for the method (phone number, email address).";
    return g;
})
.DisableAntiforgery();

app.MapGet("/submitchannelsecret", (string authToken, string pubkey, string name, string method, string value, string secret) =>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken);
        if (name.ToLower() == "phonenumber" && method.ToLower() == "sms")
        {
            var key = new ChannelKey { PubKey = pubkey, Channel = value };
            ChannelVal code = new ChannelVal(){Code="000000", Retries=0, Deadline=DateTime.MinValue};

            if ((secret == "000000") || Singlethon.channelSmsCodes.TryGetValue(key, out code))
            {
                if ((secret == "000000") || (DateTime.UtcNow<= code.Deadline))
                {
                    if ((secret == "000000") || (code.Code == secret))
                    {
                        Singlethon.Settler.GiveUserProperty(pubkey, name, Encoding.UTF8.GetBytes("valid"), Encoding.UTF8.GetBytes(method + ":" + value), DateTime.MaxValue);
                        Singlethon.channelSmsCodes.TryRemove(key, out _);
                        return new Result<int>(-1);
                    }
                    else
                    {
                        if (code.Retries > 0)
                        {
                            Singlethon.channelSmsCodes.AddOrReplace(key, new ChannelVal { Code = code.Code, Retries = code.Retries - 1, Deadline = code.Deadline });
                            return new Result<int>(code.Retries);
                        }
                    }
                }
                Singlethon.channelSmsCodes.TryRemove(key, out _);
            }
            return new Result<int>(0);
        }
        else
            throw new NotImplementedException();
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<int>(ex);
    }
})
.WithName("SubmitChannelSecret")
.WithSummary("Submits the secret code for the channel.")
.WithDescription("Returns -1 if the secret is correct, otherwise the number of retires left is returned.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.";
    g.Parameters[1].Description = "Public key of the subject.";
    g.Parameters[2].Description = "Channel name (phone,email,...)";
    g.Parameters[3].Description = "Method (sms,call,message)";
    g.Parameters[4].Description = "Value of Channel for the method (phone number, email address).";
    g.Parameters[5].Description = "Secret received from the channel.";
    return g;
})
.DisableAntiforgery();

app.MapGet("/ischannelverified", (string authToken, string pubkey, string name, string value) =>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken);
        if (name.ToLower() == "phonenumber")
        {
            var prop = Singlethon.Settler.GetUserProperty(pubkey, name);
            if(prop != null)
            {
                var val = Encoding.UTF8.GetString(prop.Value);
                var secnum = Encoding.UTF8.GetString(prop.Secret).Split(":");
                if (secnum.Length > 1)
                    return new Result<bool>((val == "valid") && (secnum[1] == value));
            }
            return new Result<bool>(false);
        }
        else
            throw new NotImplementedException();
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<bool>(ex);
    }
})
.WithName("IsChannelVerified")
.WithSummary("Checks if the specific channel is verified")
.WithDescription("Checks if the specific channel is verified")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.";
    g.Parameters[1].Description = "Public key of the subject.";
    g.Parameters[2].Description = "Channel name (phone,email,...)";
    return g;
})
.DisableAntiforgery();


app.MapGet("/generatereplypaymentpreimage", (string authToken, Guid gigId, string repliperPubKey) =>
{
    try
    {
        var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
        return new Result<string>(Singlethon.Settler.GenerateReplyPaymentPreimage(pubkey, gigId, repliperPubKey));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<string>(ex);
    }
})
.WithName("GenerateReplyPaymentPreimage")
.WithSummary("Generates new reply payment preimage and returns its hash.")
.WithDescription("Generates new reply payment preimage for the lightning network HODL invoice. This preimage is secret as long as the gig-job referenced by gigId is not marked as settled. The method returns hash of this preimage.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication.";
    g.Parameters[1].Description = "gig-job identifier";
    g.Parameters[2].Description = "Public key of the replier.";
    return g;
})
.DisableAntiforgery();

app.MapGet("/generaterelatedpreimage", (string authToken, string paymentHash) =>
{
    try
    {
        var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
        return new Result<string>(Singlethon.Settler.GenerateRelatedPreimage(pubkey, paymentHash));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<string>(ex);
    }
})
.WithName("GenerateRelatedPreimage")
.WithSummary("Generates new payment preimage that is related to the given paymentHash and returns its hash..")
.WithDescription("Generates new reply payment preimage for the lightning network HODL invoice. Allows implementing payment chains. This preimage is secret as long as the gig-job referenced paymentHash is not marked as settled. The method returns hash of this preimage.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication.";
    g.Parameters[1].Description = "Payment hash of related HODL invoice.";
    return g;
})
.DisableAntiforgery();

app.MapGet("/validaterelatedpaymenthashes", (string authToken, string paymentHash1,string paymentHash2) =>
{
    try
    {
        var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
        return new Result<bool>(Singlethon.Settler.ValidateRelatedPaymentHashes(paymentHash1, paymentHash2));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<bool>(ex);
    }
})
.WithName("ValidateRelatedPaymentHashes")
.WithSummary("Validates if given paymentHashes were generated by the same settler for the same gig.")
.WithDescription("Validates if given paymentHashes were generated by the same settler for the same gig. Allows implementing payment chains. The method returns true if the condition is met, false otherwise.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication.";
    g.Parameters[1].Description = "Payment hash of related HODL invoice.";
    g.Parameters[2].Description = "Payment hash of related HODL invoice.";
    return g;
})
.DisableAntiforgery();


app.MapGet("/revealsymmetrickey", (string authToken, Guid gigId, Guid replierId) =>
{
    try
    {
        var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
        return new Result<string>(Singlethon.Settler.RevealSymmetricKey(pubkey, gigId, replierId));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<string>(ex);
    }
})
.WithName("RevealSymmetricKey")
.WithSummary("Reveals Symmetric Key")
.WithDescription("Reveals symmetric key for the communication")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication.";
    g.Parameters[1].Description = "Gig ID.";
    g.Parameters[2].Description = "Replied Id";
    return g;
})
.DisableAntiforgery();


app.MapGet("/revealpreimage", (string authToken, string paymentHash) =>
{
    try
    {
        var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
        return new Result<string>(Singlethon.Settler.RevealPreimage(pubkey, paymentHash));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<string>(ex);
    }
})
.WithName("RevealPreimage")
.WithSummary("Reveals payment preimage of the specific paymentHash")
.WithDescription("Reveals payment preimage for the settlement of lightning network HODL invoice. This preimage is secret as long as the gig-job referenced paymentHash is not marked as settled.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication.";
    g.Parameters[1].Description = "Payment hash of related HODL invoice.";
    return g;
})
.DisableAntiforgery();

app.MapGet("/getgigstatus", (string authToken, Guid signedRequestPayloadId,Guid repliperCertificateId) =>
{
    try
    {
        var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
        return new Result<GigStatusKey>(Singlethon.Settler.GetGigStatus(signedRequestPayloadId, repliperCertificateId));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<GigStatusKey>(ex);
    }
})
.WithName("GetGigStatus")
.WithSummary("Gets status of the Gig and reveals symmetric key if available, that customer can use to decrypt the message from gig-worker.")
.WithDescription("Gets status of the Gig and reveals symmetric key if available, that customer can use to decrypt the message from gig-worker. This key is secret as long as the gig-job is not marked as accepted.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication.";
    g.Parameters[1].Description = "Request Payload Id.";
    g.Parameters[2].Description = "Replier.";
    return g;
})
.DisableAntiforgery();

app.MapPost("/generaterequestpayload", async ([FromForm] string authToken, [FromForm] string properties, IFormFile serialisedTopic) =>
{
    try
    {
        var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
        var rideShareTopic = Crypto.BinaryDeserializeObject<RideShareTopic>(await serialisedTopic.ToBytes());
        var st = await Singlethon.Settler.GenerateRequestPayloadAsync(pubkey, properties.Split(","), rideShareTopic);
        return new Result<string>(Convert.ToBase64String(Crypto.BinarySerializeObject(st)));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<string>(ex);
    }
})
.WithName("GenerateRequestPayload")
.WithSummary("Genertes RequestPayload for the specific topic.")
.WithDescription("Genertes RequestPayload for the specific topic.")
.DisableAntiforgery();


app.MapPost("/generatesettlementtrust", async ([FromForm] string authToken, [FromForm] string properties, [FromForm] string replyinvoice, IFormFile message, IFormFile signedRequestPayloadSerialized) =>
{
    try
    {
        var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
        var signedRequestPayload = Crypto.BinaryDeserializeObject<JobRequest>(await signedRequestPayloadSerialized.ToBytes());
        var reply = Crypto.BinaryDeserializeObject<Reply>(await message.ToBytes());
        var st = await Singlethon.Settler.GenerateSettlementTrustAsync(pubkey, properties.Split(","), reply, replyinvoice, signedRequestPayload);
        return new Result<string>(Convert.ToBase64String(Crypto.BinarySerializeObject(st)));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<string>(ex);
    }
})
.WithName("GenerateSettlementTrust")
.WithSummary("Genertes Settlement Trust used by the gig-worker to estabilish trusted primise with the custmer.")
.WithDescription("Genertes Settlement Trust used by the gig-worker to estabilish trusted primise with the custmer.")
.DisableAntiforgery();

app.MapPost("/encryptjobreplyforcertificateid", async ([FromForm] Guid certificateId, IFormFile objectSerialized) =>
{
    try
    {
        var pubkey = Singlethon.Settler.GetPubkeyFromCertificateId(certificateId);

        var jobReply = Crypto.BinaryDeserializeObject<JobReply>(await objectSerialized.ToBytes());
        byte[] encryptedReplyPayload = Singlethon.Settler.EncryptJobReplyForPubkey(pubkey, jobReply).Value.ToArray();
        return new Result<string>(Convert.ToBase64String(encryptedReplyPayload));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<string>(ex);
    }
})
.WithName("EncryptJobReplyForCertificateId")
.WithSummary("Encrypts the object using public key related to the specific certioficate id.")
.WithDescription("Encrypts the object using public key related to the specific certioficate id.")
.DisableAntiforgery();

app.MapGet("/managedispute", async (string authToken, Guid gigId, Guid repliperCertificateId, bool open) =>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken, open?false : true);
        await Singlethon.Settler.ManageDisputeAsync(gigId, repliperCertificateId, open);
        return new Result();
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result(ex);
    }
})
.WithName("ManageDispute")
.WithSummary("Allows opening and closing disputes.")
.WithDescription("Allows opening and closing disputes. After opening, the dispute needs to be solved positively before the HODL invoice timeouts occure. Otherwise all the invoices and payments will be cancelled.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user.";
    g.Parameters[1].Description = "Gig-job identifier.";
    g.Parameters[2].Description = "CertificateId of the replier.";
    g.Parameters[3].Description = "True to open/False to close dispute.";
    return g;
})
.DisableAntiforgery();

app.MapGet("/cancelgig", async (string authToken, Guid gigId, Guid repliperCertificateId) =>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken);
        await Singlethon.Settler.CancelGigAsync(gigId, repliperCertificateId);
        return new Result();
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result(ex);
    }
})
.WithName("CancelGig")
.WithSummary("Cancels existing gig")
.WithDescription("Allows cancelling existing gig. The gig can be cancelled only if the gig-job is not marked as settled.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user.";
    g.Parameters[1].Description = "Gig-job identifier.";
    g.Parameters[2].Description = "CertificateId of the replier.";
    return g;
})
.DisableAntiforgery();

app.MapGet("/health", () =>
{
    return Results.Ok("ok");
})
.WithName("Health")
.WithSummary("Health check endpoint")
.WithDescription("This endpoint returns a status 200 and 'ok' to indicate that the service is running properly.")
.DisableAntiforgery();

app.MapHub<PreimageRevealHub>("/preimagereveal")
.DisableAntiforgery();

app.MapHub<GigStatusHub>("/gigstatus")
.DisableAntiforgery();

app.Run(settlerSettings.ListenHost.AbsoluteUri);


[Serializable]
public struct Result
{
    public Result() { }
    public Result(Exception exception)
    {
        ErrorCode = SettlerErrorCode.OperationFailed;
        if (exception is SettlerException ex)
            ErrorCode = ex.ErrorCode;
        ErrorMessage = exception.Message;
    }
    public SettlerErrorCode ErrorCode { get; set; } = SettlerErrorCode.Ok;
    public string ErrorMessage { get; set; } = "";
}

[Serializable]
public struct Result<T>
{
    public Result(T value) { Value = value; }
    public Result(Exception exception)
    {
        ErrorCode = SettlerErrorCode.OperationFailed;
        if (exception is SettlerException ex)
            ErrorCode = ex.ErrorCode;
        ErrorMessage = exception.Message;
    }
    public T? Value { get; set; } = default;
    public SettlerErrorCode ErrorCode { get; set; } = SettlerErrorCode.Ok;
    public string ErrorMessage { get; set; } = "";
}


[Serializable]
public struct GeolocationRet
{
    public double Lat { get; set; }
    public double Lon { get; set; }
}

[Serializable]
public struct RouteRet
{
    public double Distance { get; set; }
    public double Duration { get; set; }
    public GeolocationRet[] Geometry { get; set; }
}

public class SettlerSettings
{
    public required Uri ListenHost { get; set; }
    public required Uri ServiceUri { get; set; }
    public required Uri GigWalletOpenApi { get; set; }
    public required string DBProvider { get; set; }
    public required string ConnectionString { get; set; }
    public required string SettlerPrivateKey { get; set; }
    public required string OwnerPublicKey { get; set; }
    public required long InvoicePaymentTimeoutSec { get; set; }
    public required long DisputeTimeoutSec { get; set; }
    public required string GoogleMapsAPIKey { get; set; }
    public required string FirebaseAdminConfBase64 { get; set; }
    public required string SMSGlobalAPIKeySecret { get; set; }
    public required int SMSCodeRetryNumber { get; set; }
    public required int SMSCodeTimeoutMin { get; set; }
    public required string Princings { get; set; }

    public List<string> GetPricings()
    {
        return (from s in JsonArray.Parse(Princings).AsArray() select s.GetValue<string>()).ToList();
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


public class CustomModelDocumentFilter<T> : IDocumentFilter where T : class
{
    public void Apply(OpenApiDocument openapiDoc, DocumentFilterContext context)
    {
        context.SchemaGenerator.GenerateSchema(typeof(T), context.SchemaRepository);
    }
}

/// <summary>
/// Add enum value descriptions to Swagger
/// https://stackoverflow.com/a/49941775/1910735
/// </summary>
public class EnumDocumentFilter : IDocumentFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        foreach (KeyValuePair<string, OpenApiPathItem> schemaDictionaryItem in swaggerDoc.Paths)
        {
            OpenApiPathItem schema = schemaDictionaryItem.Value;
            foreach (OpenApiParameter property in schema.Parameters)
            {
                IList<IOpenApiAny> propertyEnums = property.Schema.Enum;
                if (propertyEnums.Count > 0)
                    property.Description += DescribeEnum(propertyEnums);
            }
        }

        if (swaggerDoc.Paths.Count == 0)
            return;

        // add enum descriptions to input parameters
        foreach (OpenApiPathItem pathItem in swaggerDoc.Paths.Values)
        {
            DescribeEnumParameters(pathItem.Parameters);

            foreach (KeyValuePair<OperationType, OpenApiOperation> operation in pathItem.Operations)
                DescribeEnumParameters(operation.Value.Parameters);
        }
    }

    private static void DescribeEnumParameters(IList<OpenApiParameter> parameters)
    {
        if (parameters == null)
            return;

        foreach (OpenApiParameter param in parameters)
        {
            if (param.Schema.Enum?.Any() == true)
            {
                param.Description += DescribeEnum(param.Schema.Enum);
            }
            else if (param.Extensions.ContainsKey("enum") &&
                     param.Extensions["enum"] is IList<object> paramEnums &&
                     paramEnums.Count > 0)
            {
                param.Description += DescribeEnum(paramEnums);
            }
        }
    }

    private static string DescribeEnum(IEnumerable<object> enums)
    {
        List<string> enumDescriptions = new();
        Type? type = null;
        foreach (object enumOption in enums)
        {
            if (type == null)
                type = enumOption.GetType();

            enumDescriptions.Add($"{Convert.ChangeType(enumOption, type.GetEnumUnderlyingType())} = {Enum.GetName(type, enumOption)}");
        }

        return Environment.NewLine + string.Join(Environment.NewLine, enumDescriptions);
    }
}

//https://stackoverflow.com/a/60276722/4390133
public class EnumFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is null)
            throw new ArgumentNullException(nameof(schema));

        if (context is null)
            throw new ArgumentNullException(nameof(context));

        if (context.Type.IsEnum is false)
            return;

        schema.Extensions.Add("x-ms-enum", new EnumFilterOpenApiExtension(context));
    }
}

public class EnumFilterOpenApiExtension : IOpenApiExtension
{
    private readonly SchemaFilterContext _context;
    public EnumFilterOpenApiExtension(SchemaFilterContext context)
    {
        _context = context;
    }

    public void Write(IOpenApiWriter writer, OpenApiSpecVersion specVersion)
    {
        JsonSerializerOptions options = new() { WriteIndented = true };

        var obj = new
        {
            name = _context.Type.Name,
            modelAsString = false,
            values = _context.Type
                            .GetEnumValues()
                            .Cast<object>()
                            .Distinct()
                            .Select(value => new { value, name = value.ToString() })
                            .ToArray()
        };
        writer.WriteRaw(JsonSerializer.Serialize(obj, options));
    }
}

/// <summary>
/// Adds extra schema details for an enum in the swagger.json i.e. x-enumNames (used by NSwag to generate Enums for C# client)
/// https://github.com/RicoSuter/NSwag/issues/1234
/// </summary>
public class NSwagEnumExtensionSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is null)
            throw new ArgumentNullException(nameof(schema));

        if (context is null)
            throw new ArgumentNullException(nameof(context));

        if (context.Type.IsEnum)
            schema.Extensions.Add("x-enumNames", new NSwagEnumOpenApiExtension(context));
    }
}

public class NSwagEnumOpenApiExtension : IOpenApiExtension
{
    private readonly SchemaFilterContext _context;
    public NSwagEnumOpenApiExtension(SchemaFilterContext context)
    {
        _context = context;
    }

    public void Write(IOpenApiWriter writer, OpenApiSpecVersion specVersion)
    {
        string[] enums = Enum.GetNames(_context.Type);
        JsonSerializerOptions options = new() { WriteIndented = true };
        string value = JsonSerializer.Serialize(enums, options);
        writer.WriteRaw(value);
    }
}