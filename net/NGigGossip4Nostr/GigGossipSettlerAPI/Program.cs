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

string hostname = "https://localhost";
string walletApi = "https://localhost:7101/";
int priceAmountForSettlement = 0;
var deleteDb = false;
var connectionString = "Data Source=settler.db";
var caPrivateKey = Context.Instance.CreateECPrivKey(Convert.FromHexString("7f4c11a9742721d66e40e321ca70b682c27f7422190c84a187525e69e6038369"));

var httpClient = new HttpClient();
var lndWalletClient = new swaggerClient(walletApi, httpClient);

var gigGossipSettler = new Settler(hostname, caPrivateKey, priceAmountForSettlement);
await gigGossipSettler.Init(lndWalletClient, connectionString, deleteDb);
await gigGossipSettler.Start();

app.MapGet("/getcapublickey", () =>
{
    return gigGossipSettler.CaXOnlyPublicKey.AsHex();
})
.WithName("GetCaPublicKey")
.WithOpenApi();


app.MapGet("/gettoken", (string pubkey) =>
{
    return gigGossipSettler.GetToken(pubkey);
})
.WithName("GetToken")
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
    var signedRequestPayload = (RequestPayload)Crypto.DeserializeObject(signedRequestPayloadSerialized);
    var replierCertificate = (Certificate)Crypto.DeserializeObject(replierCertificateSerialized);

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

app.Run();

