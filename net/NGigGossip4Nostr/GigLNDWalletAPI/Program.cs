
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using CryptoToolkit;
using LNDClient;
using LNDWallet;
using Lnrpc;
using Microsoft.OpenApi.Models;
using NBitcoin.Secp256k1;
using Newtonsoft.Json;
using static NBitcoin.Scripting.PubKeyProvider;

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

while (true)
{
    var nd1 = LND.GetNodeInfo(lndConf);
    if (nd1.SyncedToChain)
        break;

    Console.WriteLine("Node not synced to chain");
    Thread.Sleep(1000);
}


LNDWalletManager walletManager = new LNDWalletManager(
    walletSettings.ConnectionString,
    lndConf,
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
    return walletManager.GetTokenGuid(pubkey);
})
.WithName("GetToken")
.WithSummary("Creates a new token Guid that is used for further communication with the API")
.WithOpenApi();

app.MapGet("/getbalance", (string authToken) =>
{
    return walletManager.ValidateAuthTokenAndGetAccount(authToken).GetAccountBallance();
})
.WithName("GetBalance")
.WithSummary("Returns the ballance of the lightning network account")
.WithOpenApi();

app.MapGet("/newaddress", (string authToken) =>
{
    return walletManager.ValidateAuthTokenAndGetAccount(authToken).NewAddress(walletSettings.NewAddressTxFee);
})
.WithName("NewAddress")
.WithSummary("Creates a new Bitcoin address that can be used to top-up this lightning network account")
.WithOpenApi();


app.MapGet("/addinvoice", (string authToken, long satoshis, string memo, long expiry) =>
{
    var acc = walletManager.ValidateAuthTokenAndGetAccount(authToken);
    var ph = acc.AddInvoice(satoshis, memo, walletSettings.AddInvoiceTxFee, expiry).PaymentRequest;
    var pa = acc.DecodeInvoice(ph);
    return new InvoiceRet() { PaymentHash = pa.PaymentHash, PaymentRequest = ph };
})
.WithName("AddInvoice")
.WithSummary("Creates a new lightning network invoice")
.WithOpenApi();

app.MapGet("/addhodlinvoice", (string authToken, long satoshis, string hash, string memo, long expiry) =>
{
    var hashb = hash.AsBytes();
    var acc = walletManager.ValidateAuthTokenAndGetAccount(authToken);
    var ph = acc.AddHodlInvoice(satoshis, memo, hashb, walletSettings.AddInvoiceTxFee, expiry).PaymentRequest;
    var pa = acc.DecodeInvoice(ph);
    return new InvoiceRet() { PaymentHash = pa.PaymentHash, PaymentRequest = ph };
})
.WithName("AddHodlInvoice")
.WithSummary("Creates a new lightning network HODL invoice")
.WithOpenApi();

app.MapGet("/decodeinvoice", (string authToken, string paymentRequest) =>
{
    return walletManager.ValidateAuthTokenAndGetAccount(authToken).DecodeInvoice(paymentRequest);
})
.WithName("DecodeInvoice")
.WithSummary("Decodes the given payment request and returns its details")
.WithOpenApi();


app.MapGet("/sendpayment", (string authToken, string paymentrequest, int timeout) =>
{
    walletManager.ValidateAuthTokenAndGetAccount(authToken).SendPayment(paymentrequest, timeout, walletSettings.SendPaymentTxFee, walletSettings.FeeLimit);
})
.WithName("SendPayment")
.WithSummary("Sends a payment via lightning network for the given payment request")
.WithOpenApi();

app.MapGet("/settleinvoice", (string authToken, string preimage) =>
{
    walletManager.ValidateAuthTokenAndGetAccount(authToken).SettleInvoice(preimage.AsBytes());
})
.WithName("SettleInvoice")
.WithSummary("Settles hodl invoice that is identified by the payment hash deliverd from the given preimage")
.WithOpenApi();

app.MapGet("/cancelinvoice", (string authToken, string paymenthash) =>
{
    walletManager.ValidateAuthTokenAndGetAccount(authToken).CancelInvoice(paymenthash);
})
.WithName("CancelInvoice")
.WithSummary("Cancels the invoice identified by the given payment hash")
.WithOpenApi();

app.MapGet("/getinvoicestate", (string authToken, string paymenthash) =>
{
    return walletManager.ValidateAuthTokenAndGetAccount(authToken).GetInvoiceState(paymenthash).ToString();
})
.WithName("GetInvoiceState")
.WithSummary("Returns a state of the invoice identified by the given payment hash")
.WithOpenApi();

app.MapGet("/getpaymentstatus", (string authToken, string paymenthash) =>
{
    return walletManager.ValidateAuthTokenAndGetAccount(authToken).GetPaymentStatus(paymenthash).ToString();
})
.WithName("GetPaymentStatus")
.WithSummary("Returns a status of the payment identified by the given payment hash")
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