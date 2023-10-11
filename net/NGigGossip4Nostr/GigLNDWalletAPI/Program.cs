
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using CryptoToolkit;
using GigLNDWalletAPI;
using LNDClient;
using LNDWallet;
using Swashbuckle.AspNetCore.Annotations;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.EnableAnnotations();
});
builder.Services.AddSignalR();

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


Singlethon.LNDWalletManager = new LNDWalletManager(
    walletSettings.ConnectionString.Replace("$HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
    lndConf,
    deleteDb: false);
Singlethon.LNDWalletManager.Start();

LNDChannelManager channelManager = new LNDChannelManager(
    Singlethon.LNDWalletManager,
    lndConf.GetFriendNodes(),
    lndConf.MaxSatoshisPerChannel,
    walletSettings.EstimatedTxFee);
channelManager.Start();

app.MapGet("/gettoken",(string pubkey) =>
{
    return Singlethon.LNDWalletManager.GetTokenGuid(pubkey);
})
.WithName("GetToken")
.WithSummary("Creates authorisation token guid")
.WithDescription("Creates a new token Guid that is used for further communication with the API")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "public key identifies the API user";
    return g;
});

app.MapGet("/getbalance",(string authToken) =>
{
    return Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).GetAccountBallance();
})
.WithName("GetBalance")
.WithSummary("Balance of the account")
.WithDescription("Returns the account balance in Satoshis")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "authorisation token for the communication";
    return g;
});

app.MapGet("/newaddress", (string authToken) =>
{
    return Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).NewAddress(walletSettings.NewAddressTxFee);
})
.WithName("NewAddress")
.WithSummary("New topup Bitcoin address")
.WithDescription("Creates a new Bitcoin address that can be used to top-up this lightning network account")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "authorisation token for the communication";
    return g;
});

app.MapGet("/registerpayout", (string authToken,long satoshis,string btcAddress,long txfee) =>
{
    return Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).RegisterPayout(satoshis, btcAddress, txfee);
})
.WithName("RegisterPayout")
.WithSummary("Register for payout to Chain")
.WithDescription("Creates new request for payout from wallet to the chain")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "authorisation token for the communication";
    g.Parameters[1].Description = "amount in satoshis";
    g.Parameters[2].Description = "bitcoin address";
    g.Parameters[3].Description = "transaction fee";
    return g;
});

app.MapGet("/addinvoice", (string authToken, long satoshis, string memo, long expiry) =>
{
    var acc = Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken);
    var ph = acc.AddInvoice(satoshis, memo, walletSettings.AddInvoiceTxFee, expiry).PaymentRequest;
    var pa = acc.DecodeInvoice(ph);
    return new InvoiceRet() { PaymentHash = pa.PaymentHash, PaymentRequest = ph };
})
.WithName("AddInvoice")
.WithSummary("Creates a new lightning network invoice")
.WithDescription("Creates a new lightning network invoice")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication";
    g.Parameters[1].Description = "The value of this invoice in satoshis.";
    g.Parameters[2].Description = "An optional memo to attach along with the invoice. Used for record keeping purposes for the invoice's creator, and will also be set in the description field of the encoded payment request if the description_hash field is not being used.";
    g.Parameters[3].Description = "Payment request expiry time in seconds.";
    return g;
});

app.MapGet("/addhodlinvoice", (string authToken, long satoshis, string hash, string memo, long expiry) =>
{
    var hashb = hash.AsBytes();
    var acc = Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken);
    var ph = acc.AddHodlInvoice(satoshis, memo, hashb, walletSettings.AddInvoiceTxFee, expiry).PaymentRequest;
    var pa = acc.DecodeInvoice(ph);
    return new InvoiceRet() { PaymentHash = pa.PaymentHash, PaymentRequest = ph };
})
.WithName("AddHodlInvoice")
.WithSummary("Creates a new lightning network HODL invoice")
.WithDescription("Creates a new lightning network HODL invoice")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication";
    g.Parameters[1].Description = "The value of this invoice in satoshis.";
    g.Parameters[2].Description = "The hash of the preimage.";
    g.Parameters[3].Description = "An optional memo to attach along with the invoice. Used for record keeping purposes for the invoice's creator, and will also be set in the description field of the encoded payment request if the description_hash field is not being used.";
    g.Parameters[4].Description = "Payment request expiry time in seconds.";
    return g;
});

app.MapGet("/decodeinvoice", (string authToken, string paymentRequest) =>
{
    return Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).DecodeInvoice(paymentRequest);
})
.WithName("DecodeInvoice")
.WithSummary("Decodes the given payment request and returns its details")
.WithDescription("Takes an encoded payment request string and attempts to decode it, returning a full description of the conditions encoded within the payment request.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication";
    g.Parameters[1].Description = "The payment request string to be decoded.";
    return g;
});


app.MapGet("/sendpayment", (string authToken, string paymentrequest, int timeout) =>
{
    Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).SendPayment(paymentrequest, timeout, walletSettings.SendPaymentTxFee, walletSettings.FeeLimit);
})
.WithName("SendPayment")
.WithSummary("Sends a payment via lightning network for the given payment request")
.WithDescription("SendPayment attempts to route a payment described by the passed paymentrequest to the final destination.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication";
    g.Parameters[1].Description = "A bare-bones invoice for a payment within the Lightning Network. With the details of the invoice, the sender has all the data necessary to send a payment to the recipient.";
    g.Parameters[2].Description = "An upper limit on the amount of time we should spend when attempting to fulfill the payment. This is expressed in seconds.";
    return g;
});

app.MapGet("/settleinvoice", (string authToken, string preimage) =>
{
    Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).SettleInvoice(preimage.AsBytes());
})
.WithName("SettleInvoice")
.WithSummary("SettleInvoice settles an accepted invoice.")
.WithDescription("Settles hodl invoice that is identified by the payment hash deliverd from the given preimage.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication";
    g.Parameters[1].Description = "Externally discovered pre-image that should be used to settle the hold invoice.";
    return g;
});

app.MapGet("/cancelinvoice", (string authToken, string paymenthash) =>
{
    Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).CancelInvoice(paymenthash);
})
.WithName("CancelInvoice")
.WithSummary("Cancels the invoice identified by the given payment hash")
.WithDescription("CancelInvoice cancels a currently open invoice. If the invoice is already canceled, this call will succeed. If the invoice is already settled, it will fail.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication";
    g.Parameters[1].Description = "Hash corresponding to the invoice to cancel.";
    return g;
});

app.MapGet("/getinvoicestate", (string authToken, string paymenthash) =>
{
    return Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).GetInvoiceState(paymenthash).ToString();
})
.WithName("GetInvoiceState")
.WithSummary("Returns a state of the invoice identified by the given payment hash")
.WithDescription("Returns a state of the invoice identified by the given payment hash")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication";
    g.Parameters[1].Description = "Hash corresponding to the invoice.";
    return g;
});

app.MapGet("/getpaymentstatus", (string authToken, string paymenthash) =>
{
    return Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).GetPaymentStatus(paymenthash).ToString();
})
.WithName("GetPaymentStatus")
.WithSummary("Returns a status of the payment identified by the given payment hash")
.WithDescription("Returns a status of the payment identified by the given payment hash")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication";
    g.Parameters[1].Description = "Hash corresponding to the payment.";
    return g;
});

app.MapHub<InvoiceStateUpdatesHub>("/invoicestateupdates");
app.MapHub<PaymentStatusUpdatesHub>("/paymentstatusupdates");

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