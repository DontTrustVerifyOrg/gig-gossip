
using System.Text.Json.Nodes;
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

var config = GetConfigurationRoot(".giggossip", "wallet.conf");
var walletSettings = config.GetSection("wallet").Get<WalletSettings>();
var lndConf = config.GetSection("lnd").Get<LndSettings>();


LNDWalletManager walletManager = new LNDWalletManager(
    walletSettings.ConnectionString,
    lndConf,
    LND.GetNodeInfo(lndConf),
    deleteDb: false);
walletManager.Start();

LNDChannelManager channelManager = new LNDChannelManager(
    walletManager,
    lndConf.GetFriendNodes(),
    lndConf.MaxSatoshisPerChannel,
    walletSettings.EstimatedTxFee);
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
    return walletManager.GetAccount(pubk).ValidateToken(authToken).NewAddress(walletSettings.NewAddressTxFee);
})
.WithName("NewAddress")
.WithOpenApi();


app.MapGet("/addinvoice", (string pubkey, string authToken, long satoshis, string memo, long expiry) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    var acc = walletManager.GetAccount(pubk).ValidateToken(authToken);
    var ph = acc.AddInvoice(satoshis, memo, walletSettings.AddInvoiceTxFee, expiry).PaymentRequest;
    var pa = acc.DecodeInvoice(ph);
    return new InvoiceRet() { PaymentHash = pa.PaymentHash, PaymentRequest = ph };
})
.WithName("AddInvoice")
.WithOpenApi();

app.MapGet("/addhodlinvoice", (string pubkey, string authToken, long satoshis, string hash, string memo, long expiry) =>
{
    var pubk = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey));
    var hashb = Convert.FromHexString(hash);
    var acc = walletManager.GetAccount(pubk).ValidateToken(authToken);
    var ph = acc.AddHodlInvoice(satoshis, memo, hashb, walletSettings.AddInvoiceTxFee, expiry).PaymentRequest;
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
    walletManager.GetAccount(pubk).ValidateToken(authToken).SendPayment(paymentrequest, timeout, walletSettings.SendPaymentTxFee, walletSettings.FeeLimit);
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


app.Run(walletSettings.ServiceUri.AbsoluteUri);



public record InvoiceRet
{
    public string PaymentRequest { get; set; }
    public string PaymentHash { get; set; }
}

public class WalletSettings
{
    public Uri ServiceUri { get; set; }
    public string ConnectionString { get; set; }
    public long NewAddressTxFee { get; set; }
    public long AddInvoiceTxFee { get; set; }
    public long SendPaymentTxFee { get; set; }
    public long FeeLimit { get; set; }
    public long EstimatedTxFee { get; set; }
}

public class LndSettings : LND.NodeSettings
{
    public string FriendNodes { get; set; }
    public long MaxSatoshisPerChannel { get; set; }

    public List<string> GetFriendNodes()
    {
        return (from s in JsonArray.Parse(FriendNodes).AsArray() select s.GetValue<string>()).ToList();
    }
}