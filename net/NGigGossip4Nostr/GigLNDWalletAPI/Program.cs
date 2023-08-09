
using System.Threading;
using LNDClient;
using LNDWallet;
using Lnrpc;
using Microsoft.OpenApi.Models;
using NBitcoin.Secp256k1;
using Newtonsoft.Json;

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

var lndConf = new LND.NodesConfiguration();

var lndIdx2 = lndConf.AddNodeConfiguration(
    new LND.MacaroonFile(@"/Users/pawel/work/locallnd/.lnd2/data/chain/bitcoin/regtest/admin.macaroon"),
    @"/Users/pawel/work/locallnd/.lnd2/tls.cert",
    @"localhost:11009",
    @"localhost:9734"
    );

var lndWalletDBConnectionString2 = "Data Source=lndwallets2.db";

bool deleteDb = true; // should we delete all dbs at start (e.g. schema change)
long newAddressTxFee = 100;
long addInvoiceTxFee = 100;
long sendPaymentTxFee = 100;
long feelimit = 1000;

LNDWalletManager walletManager = new LNDWalletManager(lndWalletDBConnectionString2, lndConf, 1, LND.GetNodeInfo(lndConf, lndIdx2), deleteDb);
walletManager.Start();
LNDChannelManager channelManager = new LNDChannelManager(walletManager, new List<string>(), 0, 0);
channelManager.Start();

app.MapGet("/gettoken", (string pubkey) =>
{
    return walletManager.GetToken(pubkey);
})
.WithName("GetToken")
.WithOpenApi();

app.MapGet("/getbalance", (string pubkey, string authToken) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    return walletManager.GetAccount(pubk).ValidateToken(authToken).GetAccountBallance();
})
.WithName("GetBalance")
.WithOpenApi();

app.MapGet("/newaddress", (string pubkey, string authToken) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    return walletManager.GetAccount(pubk).ValidateToken(authToken).NewAddress(newAddressTxFee);
})
.WithName("NewAddress")
.WithOpenApi();


app.MapGet("/addinvoice", (string pubkey, string authToken,  long satoshis,string memo) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    var acc = walletManager.GetAccount(pubk).ValidateToken(authToken);
    var ph= acc.AddInvoice(satoshis,memo, addInvoiceTxFee).PaymentRequest;
    var pa = acc.DecodeInvoice(ph);
    return new InvoiceRet() { PaymentHash = pa.PaymentHash, PaymentRequest = ph };
})
.WithName("AddInvoice")
.WithOpenApi();

app.MapGet("/addhodlinvoice", (string pubkey, string authToken, long satoshis, string hash, string memo) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    var hashb = Convert.FromHexString(hash);
    var acc = walletManager.GetAccount(pubk).ValidateToken(authToken);
    var ph= acc.AddHodlInvoice(satoshis, memo, hashb, addInvoiceTxFee).PaymentRequest;
    var pa = acc.DecodeInvoice(ph);
    return new InvoiceRet() { PaymentHash = pa.PaymentHash, PaymentRequest = ph };
})
.WithName("AddHodlInvoice")
.WithOpenApi();

app.MapGet("/decodeinvoice", (string pubkey, string authToken, string paymentRequest) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    var acc = walletManager.GetAccount(pubk).ValidateToken(authToken);
    return acc.DecodeInvoice(paymentRequest);
})
.WithName("DecodeInvoice")
.WithOpenApi();


app.MapGet("/sendpayment", (string pubkey, string authToken, string paymentrequest, int timeout) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    walletManager.GetAccount(pubk).ValidateToken(authToken).SendPayment(paymentrequest, timeout, sendPaymentTxFee, feelimit);
})
.WithName("SendPayment")
.WithOpenApi();

app.MapGet("/settleinvoice", (string pubkey, string authToken, string preimage) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    walletManager.GetAccount(pubk).ValidateToken(authToken).SettleInvoice(Convert.FromHexString(preimage));
})
.WithName("SettleInvoice")
.WithOpenApi();

app.MapGet("/cancelinvoice", (string pubkey, string authToken, string paymenthash) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    walletManager.GetAccount(pubk).ValidateToken(authToken).CancelInvoice(paymenthash);
})
.WithName("CancelInvoice")
.WithOpenApi();

app.MapGet("/getinvoicestate", (string pubkey, string authToken, string paymenthash) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    return walletManager.GetAccount(pubk).ValidateToken(authToken).GetInvoiceState(paymenthash).ToString();
})
.WithName("GetInvoiceState")
.WithOpenApi();

app.MapGet("/getpaymentstatus", (string pubkey, string authToken, string paymenthash) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    return walletManager.GetAccount(pubk).ValidateToken(authToken).GetPaymentStatus(paymenthash).ToString();
})
.WithName("GetPaymentStatus")
.WithOpenApi();


app.Run();



public record InvoiceRet
{
    public string PaymentRequest { get; set; }
    public string PaymentHash { get; set; }
}
