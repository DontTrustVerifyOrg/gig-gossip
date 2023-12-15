using CryptoToolkit;
using GigGossipSettler;
using GigGossipSettler.Exceptions;
using GigGossipSettlerAPI;
using GigGossipSettlerAPI.Config;
using GigGossipSettlerAPI.Middlewares;
using GigLNDWalletAPIClient;
using NBitcoin.Secp256k1;
using NGigGossip4Nostr;
using System.Reflection;
using System.Text;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    c.IncludeXmlComments(xmlPath);
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

    var builder = new ConfigurationBuilder();
    builder.SetBasePath(basePath)
           .AddIniFile(iniName)
           .AddEnvironmentVariables()
           .AddCommandLine(args);

    return builder.Build();
}

var config = GetConfigurationRoot(".giggossip", "settler.conf");
var settlerSettings = config.GetSection("settler").Get<SettlerSettings>();
//var settlerSettings = builder.Configuration.GetSection(SettlerConfig.SectionName).Get<SettlerConfig>();

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
Singlethon.Settler.OnSymmetricKeyReveal += (sender, e) =>
    {
        foreach (var asyncCom in Singlethon.SymmetricKeyAsyncComQueue4ConnectionId.Values)
            asyncCom.Enqueue(e);
    };

Singlethon.Settler.OnPreimageReveal+= (sender, e) =>
    {
        foreach (var asyncCom in Singlethon.PreimagesAsyncComQueue4ConnectionId.Values)
            asyncCom.Enqueue(e);
    };

await Singlethon.Settler.StartAsync();

app.MapGet("/getcapublickey", () =>
{
    try
    { 
        return new Result<string>(Singlethon.Settler.CaXOnlyPublicKey.AsHex());
    }
    catch (SettlerException ex)
    {
        return new Result<string>(ex.ErrorCode);
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
    catch (SettlerException ex)
    {
        return new Result<bool>(ex.ErrorCode);
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
    catch (SettlerException ex)
    {
        return new Result<Guid>(ex.ErrorCode);
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

app.MapGet("/giveuserproperty", (string authToken, string pubkey, string name, string value, DateTime validTill) =>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken);
        Singlethon.Settler.GiveUserProperty(pubkey, name, Convert.FromBase64String(value), validTill);
        return new Result();
    }
    catch (SettlerException ex)
    {
        return new Result(ex.ErrorCode);
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
    g.Parameters[3].Description = "Value of the property.";
    g.Parameters[4].Description = "Date and time after which the property will not be valid anymore";
    return g;
});

app.MapGet("/verifychannel", (string authToken, string pubkey, string name, string method, string value) =>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken);
        return new Result();
    }
    catch (SettlerException ex)
    {
        return new Result(ex.ErrorCode);
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
        Singlethon.Settler.GiveUserProperty(pubkey, name, Encoding.UTF8.GetBytes(method + ":" + value), DateTime.MaxValue);
        return new Result<int>(-1);
    }
    catch(SettlerException ex)
    {
        return new Result<int>(ex.ErrorCode);
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
    catch (SettlerException ex)
    {
        return new Result(ex.ErrorCode);
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
    catch (SettlerException ex)
    {
        return new Result<string>(ex.ErrorCode);
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
    catch (SettlerException ex)
    {
        return new Result<string>(ex.ErrorCode);
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
    catch (SettlerException ex)
    {
        return new Result<bool>(ex.ErrorCode);
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
    catch (SettlerException ex)
    {
        return new Result<string>(ex.ErrorCode);
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

app.MapGet("/revealsymmetrickey", (string authToken, Guid signedRequestPayloadId,Guid repliperCertificateId) =>
{
    try
    {
        var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
        return new Result<string>(Singlethon.Settler.RevealSymmetricKey(signedRequestPayloadId, repliperCertificateId));
    }
    catch (SettlerException ex)
    {
        return new Result<string>(ex.ErrorCode);
    }
})
.WithName("RevealSymmetricKey")
.WithSummary("Reveals symmetric key that customer can use to decrypt the message from gig-worker.")
.WithDescription("Reveals symmetric key that customer can use to decrypt the message from gig-worker. This key is secret as long as the gig-job is not marked as accepted.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication.";
    g.Parameters[1].Description = "Request Payload Id.";
    g.Parameters[2].Description = "Replier.";
    return g;
});

app.MapGet("/generaterequestpayload", (string authToken, string[] properties, string serialisedTopic) =>
{
    try
    {
        var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
        var st = Singlethon.Settler.GenerateRequestPayload(pubkey, properties, Convert.FromBase64String(serialisedTopic));
        return new Result<string>(Convert.ToBase64String(Crypto.SerializeObject(st)));
    }
    catch (SettlerException ex)
    {
        return new Result<string>(ex.ErrorCode);
    }
})
.WithName("GenerateRequestPayload")
.WithSummary("Genertes RequestPayload for the specific topic.")
.WithDescription("Genertes RequestPayload for the specific topic.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication.";
    g.Parameters[1].Description = "Requested properties of the sender.";
    g.Parameters[2].Description = "Topic";
    return g;
});

app.MapGet("/generatesettlementtrust", async (string authToken, string[] properties, string message, string replyinvoice, string signedRequestPayloadSerialized) =>
{
    try
    {
        var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
        var signedRequestPayload = Crypto.DeserializeObject<Certificate<RequestPayloadValue>>(Convert.FromBase64String(signedRequestPayloadSerialized));
        var st = await Singlethon.Settler.GenerateSettlementTrustAsync(pubkey, properties, Convert.FromBase64String(message), replyinvoice, signedRequestPayload);
        return new Result<string>(Convert.ToBase64String(Crypto.SerializeObject(st)));
    }
    catch (SettlerException ex)
    {
        return new Result<string>(ex.ErrorCode);
    }
})
.WithName("GenerateSettlementTrust")
.WithSummary("Genertes Settlement Trust used by the gig-worker to estabilish trusted primise with the custmer.")
.WithDescription("Genertes Settlement Trust used by the gig-worker to estabilish trusted primise with the custmer.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication.";
    g.Parameters[1].Description = "Requested properties of the replier.";
    g.Parameters[2].Description = "Message to be encrypted";
    g.Parameters[3].Description = "Invoice for the job.";
    g.Parameters[4].Description = "Request payload";
    return g;
});

app.MapGet("/encryptobjectforcertificateid", (Guid certificateId, string objectSerialized) =>
{
    try
    {
        byte[] encryptedReplyPayload = Singlethon.Settler.EncryptObjectForCertificateId(Convert.FromBase64String(objectSerialized), certificateId);
        return new Result<string>(Convert.ToBase64String(encryptedReplyPayload));
    }
    catch (SettlerException ex)
    {
        return new Result<string>(ex.ErrorCode);
    }
})
.WithName("EncryptObjectForCertificateId")
.WithSummary("Encrypts the object using public key related to the specific certioficate id.")
.WithDescription("Encrypts the object using public key related to the specific certioficate id.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Certificate ID";
    g.Parameters[1].Description = "Serialized Object";
    return g;
});

app.MapGet("/managedispute", async (string authToken, Guid gigId, Guid repliperCertificateId, bool open) =>
{
    try
    {
        Singlethon.Settler.ValidateAuthToken(authToken);
        await Singlethon.Settler.ManageDisputeAsync(gigId, repliperCertificateId, open);
        return new Result();
    }
    catch (SettlerException ex)
    {
        return new Result(ex.ErrorCode);
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

app.MapHub<PreimageRevealHub>("/preimagereveal");
app.MapHub<SymmetricKeyRevealHub>("/symmetrickeyreveal");

app.Run(settlerSettings.ListenHost.AbsoluteUri);

[Serializable]
public record Result
{
    public Result() { }
    public Result(SettlerErrorCode errorCode) { ErrorCode = errorCode; ErrorMessage = errorCode.Message(); }
    public SettlerErrorCode ErrorCode { get; set; }
    public string ErrorMessage { get; set; } = "";
}

[Serializable]
public record Result<T>
{
    public Result(T value) { Value = value; }
    public Result(SettlerErrorCode errorCode) { ErrorCode = errorCode; ErrorMessage = errorCode.Message(); }
    public T? Value { get; set; } = default;
    public SettlerErrorCode ErrorCode { get; set; }
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