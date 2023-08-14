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

app.MapGet("/giveuserproperty", (string pubkey, string authToken, string name, byte[] value, DateTime validTill) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    gigGossipSettler.ValidateToken(pubk, authToken).GiveUserProperty(pubkey, name, value, validTill);
})
.WithName("GiveUserProperty")
.WithOpenApi();

app.MapGet("/revokeuserproperty", (string pubkey, string authToken, string name) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    gigGossipSettler.ValidateToken(pubk, authToken).RevokeUserProperty(pubkey, name);
})
.WithName("RevokeUserProperty")
.WithOpenApi();

app.MapGet("/issuecertificate", (string pubkey, string authToken, string[] properties) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    return Crypto.SerializeObject(gigGossipSettler.ValidateToken(pubk, authToken).IssueCertificate(pubkey, properties));
})
.WithName("IssueCertificate")
.WithOpenApi();



app.MapGet("/generatereplypaymentpreimage", (string pubkey, string authToken, Guid tid) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    return gigGossipSettler.ValidateToken(pubk,authToken).GenerateReplyPaymentPreimage(pubkey,tid);
})
.WithName("GenerateReplyPaymentPreimage")
.WithOpenApi();

app.MapGet("/generaterelatedpreimage", (string pubkey, string authToken, string paymentHash) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    return gigGossipSettler.ValidateToken(pubk, authToken).GenerateRelatedPreimage(pubkey, paymentHash);
})
.WithName("GenerateRelatedPreimage")
.WithOpenApi();

app.MapGet("/revealpreimage", (string pubkey, string authToken, string paymentHash) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    return gigGossipSettler.ValidateToken(pubk, authToken).RevealPreimage(pubkey, paymentHash);
})
.WithName("RevealPreimage")
.WithOpenApi();


app.MapGet("/generatesettlementtrust", async (string pubkey, string authToken, byte[] message, string replyinvoice, byte[] signedRequestPayloadSerialized, byte[] replierCertificateSerialized) =>
{
    var signedRequestPayload = Crypto.DeserializeObject< RequestPayload>(signedRequestPayloadSerialized);
    var replierCertificate = Crypto.DeserializeObject< Certificate>(replierCertificateSerialized);

    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    var st = await gigGossipSettler.ValidateToken(pubk, authToken).GenerateSettlementTrust(pubkey, message, replyinvoice, signedRequestPayload, replierCertificate);
    return Crypto.SerializeObject(st);
})
.WithName("GenerateSettlementTrust")
.WithOpenApi();

app.MapGet("/revealsymmetrickey", (string pubkey, string authToken, Guid tid) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    return gigGossipSettler.ValidateToken(pubk, authToken).RevealSymmetricKey(pubkey, tid);
})
.WithName("RevealSymmetricKey")
.WithOpenApi();

app.MapGet("/managedispute", (string pubkey, string authToken, Guid tid, bool open) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    gigGossipSettler.ValidateToken(pubk, authToken).ManageDispute(tid, open);
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