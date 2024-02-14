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
var lndWalletClient = new swaggerClient(settlerSettings.GigWalletOpenApi.AbsoluteUri, httpClient);


Singlethon.Settler = new Settler(
    settlerSettings.ServiceUri,
    new SimpleSettlerSelector(httpClient),
    caPrivateKey, settlerSettings.PriceAmountForSettlement,
    TimeSpan.FromSeconds(settlerSettings.InvoicePaymentTimeoutSec),
    TimeSpan.FromSeconds(settlerSettings.DisputeTimeoutSec)
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

app.MapGet("/verifychannel", (string authToken, string pubkey, string name, string method, string value) =>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken);
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
        Singlethon.Settler.GiveUserProperty(pubkey, name, Encoding.UTF8.GetBytes("valid"), Encoding.UTF8.GetBytes(method + ":" + value), DateTime.MaxValue);
        return new Result<int>(-1);
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
}