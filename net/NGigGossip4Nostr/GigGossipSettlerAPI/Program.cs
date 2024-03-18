using CryptoToolkit;
using GigGossipSettler;
using GigGossipSettler.Exceptions;
using GigGossipSettlerAPI;
using GigLNDWalletAPIClient;
using NBitcoin.Secp256k1;
using NGigGossip4Nostr;
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

#pragma warning disable 1591

TraceEx.TraceInformation("[[lime]]Starting[[/]] ...");

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.EnableAnnotations();
});
builder.Services.AddSignalR();
builder.Services.AddProblemDetails();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
//app.UseMiddleware<ErrorHandlerMiddleware>();
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

ECPrivKey caPrivateKey = settlerSettings.SettlerPrivateKey.AsECPrivKey();

var httpClient = new HttpClient();
var retryPolicy = new DefaultRetryPolicy();
var lndWalletClient = new swaggerClient(settlerSettings.GigWalletOpenApi.AbsoluteUri, httpClient, new DefaultRetryPolicy());

Singlethon.Settler = new Settler(
    settlerSettings.ServiceUri,
    new SimpleSettlerSelector(httpClient, retryPolicy),
    caPrivateKey, settlerSettings.PriceAmountForSettlement,
    TimeSpan.FromSeconds(settlerSettings.InvoicePaymentTimeoutSec),
    TimeSpan.FromSeconds(settlerSettings.DisputeTimeoutSec),
            new DefaultRetryPolicy()
    );

await Singlethon.Settler.InitAsync(lndWalletClient, settlerSettings.ConnectionString.Replace("$HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
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
.WithOpenApi();

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
});


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
});

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
});


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
            TravelMode = GoogleApi.Entities.Maps.Common.Enums.TravelMode.Driving,
            Units = GoogleApi.Entities.Maps.Common.Enums.Units.Metric,
            Alternatives = false,
        };

        var response = await GoogleMaps.Directions.QueryAsync(request);

        var route = response.Routes.FirstOrDefault();
        if (route == null)
            throw new Exception("Not found");

        var leg = route.Legs.FirstOrDefault();
        if (leg == null)
            throw new Exception("Not found");

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
});

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
            throw new Exception("Not found");
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
});


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
            throw new Exception("Not found");
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
});


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
});



app.MapPost("/giveuserfile/{authToken}/{pubkey}/{name}/{validHours}", async (HttpRequest request, string authToken, string pubkey, string name, long validHours, [FromForm] IFormFile value, [FromForm] IFormFile secret)
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
.WithDescription("Grants a file property to the subject (e.g. driving licence). Only authorised users can grant the property.");

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
});

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
                throw new Exception("Cannot send");
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
});

app.MapGet("/submitchannelsecret", (string authToken, string pubkey, string name, string method, string value, string secret) =>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken);
        if (name.ToLower() == "phonenumber" && method.ToLower() == "sms")
        {
            var key = new ChannelKey { PubKey = pubkey, Channel = value };
            ChannelVal code;

            if (Singlethon.channelSmsCodes.TryGetValue(key, out code))
            {
                if (DateTime.UtcNow<= code.Deadline)
                {
                    if (code.Code == secret)
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
});

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
});

app.MapGet("/revokeuserproperty", (string authToken, string pubkey, string name) =>
{
    try
    { 
        Singlethon.Settler.ValidateAuthToken(authToken);
        Singlethon.Settler.RevokeUserProperty(pubkey, name);
        return new Result();
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result(ex);
    }
})
.WithDescription("Revokes a property from the subject (e.g. driving licence is taken by the police). Only authorised users can revoke the property.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.";
    g.Parameters[1].Description = "Public key of the subject.";
    g.Parameters[2].Description = "Name of the property.";
    return g;
});

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
});

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
});

app.MapGet("/validaterelatedpaymenthashes", (string authToken, string paymentHash1,string paymentHash2) =>
{
    try
    {
        var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
        return new Result<bool>(Singlethon.Settler.ValidateRelatedPaymentHashes(pubkey, paymentHash1, paymentHash2));
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
});

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
});

app.MapGet("/getgigstatus", (string authToken, Guid signedRequestPayloadId,Guid repliperCertificateId) =>
{
    try
    {
        var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
        return new Result<string>(Singlethon.Settler.GetGigStatus(signedRequestPayloadId, repliperCertificateId));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<string>(ex);
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
});

app.MapPost("/generaterequestpayload/{authToken}/{properties}", async (string authToken, string properties, [FromForm] IFormFile serialisedTopic) =>
{
    try
    {
        var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
        var st = Singlethon.Settler.GenerateRequestPayload(pubkey, properties.Split(","), await serialisedTopic.ToBytes());
        return new Result<string>(Convert.ToBase64String(Crypto.SerializeObject(st)));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<string>(ex);
    }
})
.WithName("GenerateRequestPayload")
.WithSummary("Genertes RequestPayload for the specific topic.")
.WithDescription("Genertes RequestPayload for the specific topic.");

app.MapPost("/generatesettlementtrust/{authToken}/{properties}/{replyinvoice}", async (string authToken, string properties, string replyinvoice, [FromForm] IFormFile message, [FromForm] IFormFile signedRequestPayloadSerialized) =>
{
    try
    {
        var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
        var signedRequestPayload = Crypto.DeserializeObject<Certificate<RequestPayloadValue>>(await signedRequestPayloadSerialized.ToBytes());
        var st = await Singlethon.Settler.GenerateSettlementTrustAsync(pubkey, properties.Split(","), await message.ToBytes(), replyinvoice, signedRequestPayload);
        return new Result<string>(Convert.ToBase64String(Crypto.SerializeObject(st)));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<string>(ex);
    }
})
.WithName("GenerateSettlementTrust")
.WithSummary("Genertes Settlement Trust used by the gig-worker to estabilish trusted primise with the custmer.")
.WithDescription("Genertes Settlement Trust used by the gig-worker to estabilish trusted primise with the custmer.");

app.MapPost("/encryptobjectforcertificateid/{certificateId}", async (Guid certificateId, [FromForm] IFormFile objectSerialized) =>
{
    try
    {
        byte[] encryptedReplyPayload = Singlethon.Settler.EncryptObjectForCertificateId(await objectSerialized.ToBytes(), certificateId);
        return new Result<string>(Convert.ToBase64String(encryptedReplyPayload));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<string>(ex);
    }
})
.WithName("EncryptObjectForCertificateId")
.WithSummary("Encrypts the object using public key related to the specific certioficate id.")
.WithDescription("Encrypts the object using public key related to the specific certioficate id.");

app.MapGet("/managedispute", async (string authToken, Guid gigId, Guid repliperCertificateId, bool open) =>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken);
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
});

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
.WithDescription("Allows opening and closing disputes. After opening, the dispute needs to be solved positively before the HODL invoice timeouts occure. Otherwise all the invoices and payments will be cancelled.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user.";
    g.Parameters[1].Description = "Gig-job identifier.";
    g.Parameters[2].Description = "CertificateId of the replier.";
    return g;
});

app.MapHub<PreimageRevealHub>("/preimagereveal");
app.MapHub<GigStatusHub>("/gigstatus");

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
    public required long PriceAmountForSettlement { get; set; }
    public required string ConnectionString { get; set; }
    public required string SettlerPrivateKey { get; set; }
    public required long InvoicePaymentTimeoutSec { get; set; }
    public required long DisputeTimeoutSec { get; set; }
    public required string GoogleMapsAPIKey { get; set; }
    public required string SMSGlobalAPIKeySecret { get; set; }
    public required int SMSCodeRetryNumber { get; set; }
    public required int SMSCodeTimeoutMin { get; set; }
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