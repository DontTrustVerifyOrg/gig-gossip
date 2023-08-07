using System.Threading;
using LNDClient;
using LNDWallet;
using Lnrpc;
using NBitcoin.Secp256k1;

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

bool deleteDb = false; // should we delete all dbs at start (e.g. schema change)
ulong newAddressTxFee = 100;
ulong addInvoiceTxFee = 100;
ulong sendPaymentTxFee = 100;
ulong feelimit = 1000;

LNDWalletManager walletManager = new LNDWalletManager(lndWalletDBConnectionString2, lndConf, 1, LND.GetNodeInfo(lndConf, lndIdx2), deleteDb);


app.MapGet("/getbalance", (string pubkey, string signedTime) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    return walletManager.GetAccount(pubk).GetAccountBallance();
})
.WithName("GetBalance")
.WithOpenApi();

app.MapGet("/newaddress", (string pubkey, string signedTime) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    return walletManager.GetAccount(pubk).NewAddress(newAddressTxFee);
})
.WithName("NewAddress")
.WithOpenApi();


app.MapGet("/addinvoice", (string pubkey, string signedTime, long satoshis,string memo) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    var acc = walletManager.GetAccount(pubk);
    var ph= acc.AddInvoice(satoshis,memo, addInvoiceTxFee).PaymentRequest;
    var pa = acc.DecodeInvoice(ph);
    return new InvoiceRet() { PaymentHash = pa.PaymentHash, PaymentRequest = ph };
})
.WithName("AddInvoice")
.WithOpenApi();

app.MapGet("/addhodlinvoice", (string pubkey, string signedTime, long satoshis, string hash, string memo) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    var hashb = Convert.FromHexString(hash);
    var acc = walletManager.GetAccount(pubk);
    var ph= acc.AddHodlInvoice(satoshis, memo, hashb, addInvoiceTxFee).PaymentRequest;
    var pa = acc.DecodeInvoice(ph);
    return new InvoiceRet() { PaymentHash = pa.PaymentHash, PaymentRequest = ph };
})
.WithName("AddHodlInvoice")
.WithOpenApi();


app.MapGet("/sendpayment", (string pubkey, string signedTime, string paymentrequest, int timeout) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    walletManager.GetAccount(pubk).SendPayment(paymentrequest, timeout, sendPaymentTxFee, feelimit);
})
.WithName("SendPayment")
.WithOpenApi();

app.MapGet("/settleinvoice", (string pubkey, string preimage) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    walletManager.GetAccount(pubk).SettleInvoice(Convert.FromHexString(preimage));
})
.WithName("SettleInvoice")
.WithOpenApi();

app.MapGet("/cancelinvoice", (string pubkey, string paymenthash) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    walletManager.GetAccount(pubk).CancelInvoice(paymenthash);
})
.WithName("CancelInvoice")
.WithOpenApi();

app.MapGet("/getinvoicestate", (string pubkey, string paymenthash) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    return walletManager.GetAccount(pubk).GetInvoiceState(paymenthash).ToString();
})
.WithName("GetInvoiceState")
.WithOpenApi();

app.MapGet("/getpaymentstatus", (string pubkey, string paymenthash) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    return walletManager.GetAccount(pubk).GetPaymentStatus(paymenthash).ToString();
})
.WithName("GetPaymentStatus")
.WithOpenApi();


app.Run();



public record InvoiceRet
{
    public string PaymentRequest;
    public string PaymentHash;
}