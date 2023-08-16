using System;
using NBitcoin.Secp256k1;
using NGigGossip4Nostr;
using CryptoToolkit;
using GigGossipSettler;
using Microsoft.AspNetCore.Builder;
using GigLNDWalletAPIClient;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

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


var caPrivateKey = Context.Instance.CreateECPrivKey(Convert.FromHexString(settlerSettings.SettlerPrivateKey));

var httpClient = new HttpClient();
var lndWalletClient = new swaggerClient(settlerSettings.GigWalletOpenApi.AbsoluteUri, httpClient);

var gigGossipSettler = new Settler(settlerSettings.ServiceUri, caPrivateKey, settlerSettings.PriceAmountForSettlement, TimeSpan.FromSeconds(settlerSettings.InvoicePaymentTimeoutSec));
await gigGossipSettler.Init(lndWalletClient, settlerSettings.ConnectionString, false);
await gigGossipSettler.Start();

app.MapGet("/getcapublickey", () =>
{
    return gigGossipSettler.CaXOnlyPublicKey.AsHex();
})
.WithName("GetCaPublicKey")
.WithOpenApi();

app.MapGet("/iscertificaterevoked", (Guid certid) =>
{
    return gigGossipSettler.IsCertificateRevoked(certid);
})
.WithName("IsCertificateRevoked")
.WithOpenApi();

app.MapGet("/gettoken", (string pubkey) =>
{
    return gigGossipSettler.GetToken(pubkey);
})
.WithName("GetToken")
.WithOpenApi();

app.MapGet("/giveuserproperty", (string authToken, string pubkey, string name, string value, DateTime validTill) =>
{
    gigGossipSettler.ValidateToken(authToken);
    gigGossipSettler.GiveUserProperty(pubkey, name, Convert.FromBase64String(value), validTill);
})
.WithName("GiveUserProperty")
.WithOpenApi();

app.MapGet("/revokeuserproperty", (string authToken, string pubkey, string name) =>
{
    gigGossipSettler.ValidateToken(authToken);
    gigGossipSettler.RevokeUserProperty(pubkey, name);
})
.WithName("RevokeUserProperty")
.WithOpenApi();

app.MapGet("/issuecertificate", (string authToken, string pubkey, string[] properties) =>
{
    gigGossipSettler.ValidateToken(authToken);
    return Crypto.SerializeObject(gigGossipSettler.IssueCertificate(pubkey, properties));
})
.WithName("IssueCertificate")
.WithOpenApi();

app.MapGet("/getcertificate", (string authToken, string pubkey, Guid certid) =>
{
    gigGossipSettler.ValidateToken(authToken);
    return Crypto.SerializeObject(gigGossipSettler.GetCertificate(pubkey, certid));
})
.WithName("GetCertificate")
.WithOpenApi();

app.MapGet("/listcertificates", (string authToken, string pubkey) =>
{
    gigGossipSettler.ValidateToken(authToken);
    return gigGossipSettler.ListCertificates(pubkey);
})
.WithName("ListCertificates")
.WithOpenApi();

app.MapGet("/generatereplypaymentpreimage", (string authToken, Guid tid) =>
{
    var pubkey = gigGossipSettler.ValidateToken(authToken);
    return gigGossipSettler.GenerateReplyPaymentPreimage(pubkey,tid);
})
.WithName("GenerateReplyPaymentPreimage")
.WithOpenApi();

app.MapGet("/generaterelatedpreimage", (string authToken, string paymentHash) =>
{
    var pubkey = gigGossipSettler.ValidateToken(authToken);
    return gigGossipSettler.GenerateRelatedPreimage(pubkey, paymentHash);
})
.WithName("GenerateRelatedPreimage")
.WithOpenApi();

app.MapGet("/revealpreimage", (string authToken, string paymentHash) =>
{
    var pubkey = gigGossipSettler.ValidateToken(authToken);
    return gigGossipSettler.RevealPreimage(pubkey, paymentHash);
})
.WithName("RevealPreimage")
.WithOpenApi();


app.MapGet("/generatesettlementtrust", async (string authToken, string message, string replyinvoice, string signedRequestPayloadSerialized, string replierCertificateSerialized) =>
{
    var pubkey = gigGossipSettler.ValidateToken(authToken);
    var signedRequestPayload = Crypto.DeserializeObject< RequestPayload>(Convert.FromBase64String(signedRequestPayloadSerialized));
    var replierCertificate = Crypto.DeserializeObject< Certificate>(Convert.FromBase64String(replierCertificateSerialized));
    var st = await gigGossipSettler.GenerateSettlementTrust(pubkey, Convert.FromBase64String(message), replyinvoice, signedRequestPayload, replierCertificate);
    return Convert.ToBase64String(Crypto.SerializeObject(st));
})
.WithName("GenerateSettlementTrust")
.WithOpenApi();

app.MapGet("/revealsymmetrickey", (string authToken, Guid tid) =>
{
    var pubkey = gigGossipSettler.ValidateToken(authToken);
    return gigGossipSettler.RevealSymmetricKey(pubkey, tid);
})
.WithName("RevealSymmetricKey")
.WithOpenApi();

app.MapGet("/managedispute", (string authToken, Guid tid, bool open) =>
{
    gigGossipSettler.ValidateToken(authToken);
    gigGossipSettler.ManageDispute(tid, open);
})
.WithName("ManageDispute")
.WithOpenApi();

app.Run(settlerSettings.ServiceUri.AbsoluteUri);

public class SettlerSettings
{
    public Uri ServiceUri { get; set; }
    public Uri GigWalletOpenApi { get; set; }
    public long PriceAmountForSettlement { get; set; }
    public string ConnectionString { get; set; }
    public string SettlerPrivateKey { get; set; }
    public long InvoicePaymentTimeoutSec { get; set; }
}