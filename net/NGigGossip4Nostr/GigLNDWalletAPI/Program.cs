using System.Text.Json.Nodes;
using CryptoToolkit;
using GigLNDWalletAPI;
using LNDClient;
using LNDWallet;
using NBitcoin;
using NBitcoin.RPC;
using Spectre.Console;
using TraceExColor;

TraceEx.TraceInformation("[[lime]]Starting[[/]] ...");

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.EnableAnnotations();
});
builder.Services.AddSignalR();
builder.Services.AddProblemDetails();
var app = builder.Build();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseExceptionHandler();
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

var config = GetConfigurationRoot(".giggossip", "wallet.conf");
var walletSettings = config.GetSection("wallet").Get<WalletSettings>();
var lndConf = config.GetSection("lnd").Get<LndSettings>();
BitcoinSettings btcConf = config.GetSection("bitcoin").Get<BitcoinSettings>();
BitcoinNode bitcoinNode = new BitcoinNode(btcConf.AuthenticationString, btcConf.HostOrUri, btcConf.Network, btcConf.WalletName);

while (true)
{
    var nd1 = LND.GetNodeInfo(lndConf);
    if (nd1.SyncedToChain)
        break;

    TraceEx.TraceWarning("Node not synced to chain");
    if (bitcoinNode.IsRegTest)
    {
        TraceEx.TraceWarning("Mining 101");
        bitcoinNode.Mine101Blocks();
    }
    Thread.Sleep(1000);
}


Singlethon.LNDWalletManager = new LNDWalletManager(
    Enum.Parse<DBProvider>(walletSettings.DBProvider),
    walletSettings.ConnectionString.Replace("$HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
    bitcoinNode,
    lndConf,
    walletSettings.AdminPublicKey);

Singlethon.LNDWalletManager.OnInvoiceStateChanged += (sender, e) =>
{
    foreach (var asyncCom in Singlethon.InvoiceAsyncComQueue4ConnectionId.Values)
        asyncCom.Enqueue(e);
};

Singlethon.LNDWalletManager.OnPaymentStatusChanged += (sender, e) =>
{
    foreach (var asyncCom in Singlethon.PaymentAsyncComQueue4ConnectionId.Values)
        asyncCom.Enqueue(e);
};

Singlethon.LNDWalletManager.Start();

LNDChannelManager channelManager = new LNDChannelManager(
    Singlethon.LNDWalletManager,
    lndConf.GetFriendNodes(),
    lndConf.MaxSatoshisPerChannel,
    walletSettings.EstimatedChannelCloseFee,
    walletSettings.MaxChannelCloseFeePerVByte);
channelManager.Start();


TraceEx.TraceInformation("... Running");


app.MapGet("/gettoken", (string pubkey) =>
{
    try
    {
        return new Result<Guid>(Singlethon.LNDWalletManager.GetTokenGuid(pubkey));
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

app.MapGet("/topupandmine6blocks", (string authToken, string bitcoinAddr, long satoshis) =>
{
    try
    {
        Singlethon.LNDWalletManager.ValidateAuthToken(authToken);
        if (Singlethon.LNDWalletManager.BitcoinNode.IsRegTest)
            Singlethon.LNDWalletManager.BitcoinNode.TopUpAndMine6Blocks(bitcoinAddr, satoshis);
        else
            throw new InvalidOperationException("Bitcoin node is not in RegTest");
        return new Result();
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result(ex);
    }
})
.WithName("TopUpAndMine6Blocks")
.WithSummary("Sends satoshis from local BTC wallet to the address (Regtest only), then mines 6 blocks.")
.WithDescription("Sends satoshis from local BTC wallet to the address (Regtest only), then mines 6 blocks.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "bitcoin address";
    g.Parameters[1].Description = "number of satoshis";
    return g;
});

app.MapGet("/sendtoaddress", (string authToken, string bitcoinAddr, long satoshis) =>
{
    try
    {
        Singlethon.LNDWalletManager.ValidateAuthToken(authToken);
        if (Singlethon.LNDWalletManager.BitcoinNode.IsRegTest)
            Singlethon.LNDWalletManager.BitcoinNode.SendToAddress(bitcoinAddr, satoshis);
        else
            throw new InvalidOperationException("Bitcoin node is not in RegTest");

        return new Result();
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result(ex);
    }
})
.WithName("SendToAddress")
.WithSummary("Sends satoshis from local BTC wallet to the address.")
.WithDescription("Sends satoshis from local BTC wallet to the address.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "bitcoin address";
    g.Parameters[1].Description = "number of satoshis";
    return g;
});

app.MapGet("/generateblocks", (string authToken, int blocknum) =>
{
    try
    {
        Singlethon.LNDWalletManager.ValidateAuthToken(authToken);
        if (Singlethon.LNDWalletManager.BitcoinNode.IsRegTest)
            Singlethon.LNDWalletManager.BitcoinNode.GenerateBlocks(blocknum);
        else
            throw new InvalidOperationException("Bitcoin node is not in RegTest");

        return new Result();
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result(ex);
    }
})
.WithName("GenerateBlocks")
.WithSummary("Mines Number of Blocks (Regtest only)")
.WithDescription("Mines Number of Blocks (Regtest only)")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "number of blocks to mine";
    return g;
});

app.MapGet("/newbitcoinaddress", (string authToken) =>
{
    try
    {
        Singlethon.LNDWalletManager.ValidateAuthToken(authToken,true);
        return new Result<string>(Singlethon.LNDWalletManager.BitcoinNode.NewAddress());
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<string>(ex);
    }
})
.WithName("NewBitcoinAddress")
.WithSummary("Creates New Bitcoin Address To the Bitcoin Wallet")
.WithDescription("Creates New Bitcoin Address To the Bitcoin Wallet")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "authorisation token for the communication";
    return g;
});

app.MapGet("/getbitcoinwalletballance", (string authToken, int minConf) =>
{
    try
    {
        Singlethon.LNDWalletManager.ValidateAuthToken(authToken,true);
        return new Result<long>(Singlethon.LNDWalletManager.BitcoinNode.WalletBallance(minConf));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<long>(ex);
    }
})
.WithName("GetBitcoinWalletBallance")
.WithSummary("Returns the Bitcoin Wallet Ballance in Sathoshis")
.WithDescription("Returns the Bitcoin Wallet Ballance in Sathoshis")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "authorisation token for the communication";
    g.Parameters[1].Description = "minimal number of confirmation";
    return g;
});


app.MapGet("/getlndwalletballance", (string authToken) =>
{
    try
    {
        Singlethon.LNDWalletManager.ValidateAuthToken(authToken,true);
        var ret = Singlethon.LNDWalletManager.GetWalletBalance();
        return new Result<LndWalletBallanceRet>(
            new LndWalletBallanceRet
            {
                ConfirmedBalance = ret.ConfirmedBalance,
                UnconfirmedBalance = ret.UnconfirmedBalance,
                TotalBalance = ret.TotalBalance,
                ReservedBalanceAnchorChan = ret.ReservedBalanceAnchorChan,
                LockedBalance = ret.LockedBalance,
            });
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<LndWalletBallanceRet>(ex);
    }
})
.WithName("GetLndWalletBallance")
.WithSummary("Returns the Lnd Node Wallet Ballance in Sathoshis")
.WithDescription("Returns the Lnd Node Wallet Ballance in Sathoshis")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "authorisation token for the communication";
    return g;
});


app.MapGet("/openreserve", (string authToken, long satoshis) =>
{
    try
    {
        Singlethon.LNDWalletManager.ValidateAuthToken(authToken,true);
        return new Result<Guid>(Singlethon.LNDWalletManager.OpenReserve(satoshis));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<Guid>(ex);
    }
})
.WithName("OpenReserve")
.WithSummary("Opens reserve")
.WithDescription("Opens reserve")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "number of satoshis";
    return g;
});

app.MapGet("/closereserve", (string authToken, Guid reserveId) =>
{
    try
    {
        Singlethon.LNDWalletManager.ValidateAuthToken(authToken,true);
        Singlethon.LNDWalletManager.CloseReserve(reserveId);
        return new Result();
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result(ex);
    }
})
.WithName("CloseReserve")
.WithSummary("Closes Reserve")
.WithDescription("Closes Reserve")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "reserve id";
    return g;
});

app.MapGet("/estimatefee", (string authToken, string address, long satoshis) =>
{
    bool isAdmin = false;
    try
    {
        var pubKey = Singlethon.LNDWalletManager.ValidateAuthToken(authToken);
        isAdmin = Singlethon.LNDWalletManager.HasAdminRights(pubKey); 
        var (feesat, satspervbyte) = Singlethon.LNDWalletManager.EstimateFee(address, satoshis);
        return new Result<FeeEstimateRet>(new FeeEstimateRet { FeeSat = feesat, SatPerVbyte = satspervbyte });
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        if (isAdmin)
            return new Result<FeeEstimateRet>(ex);
        else
            return new Result<FeeEstimateRet>(new FeeEstimateRet { FeeSat = -1, SatPerVbyte = 0 });
    }
})
.WithName("EstimateFee")
.WithSummary("Gives Fee Estimate")
.WithDescription("Gives Fee Estimate")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "bitcoin address";
    g.Parameters[1].Description = "number of satoshis";
    return g;
});

app.MapGet("/getbalance",(string authToken) =>
{
    try
    {
        return new Result<AccountBalanceDetails>(Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).GetAccountBallance());
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<AccountBalanceDetails>(ex);
    }
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
    try
    {
        return new Result<string>(Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).CreateNewTopupAddress());
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<string>(ex);
    }
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
    try
    {
        return new Result<Guid>(Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).RegisterNewPayoutForExecution(satoshis, btcAddress, txfee));
    }
    catch(Exception ex) 
    {
        TraceEx.TraceException(ex);
        return new Result<Guid>(ex);
    }
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
    try
    {
        return new Result<InvoiceRecord>(Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).CreateNewClassicInvoice(satoshis, memo, expiry));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<InvoiceRecord>(ex);
    }
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
    try
    {
        return new Result<InvoiceRecord>(Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).CreateNewHodlInvoice(satoshis, memo, hash.AsBytes(), expiry));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<InvoiceRecord>(ex);
    }
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
    try
    {
        return new Result<PaymentRequestRecord>(Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).DecodeInvoice(paymentRequest));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<PaymentRequestRecord>(ex);
    }
})
.WithName("DecodeInvoice")
.WithSummary("Decodes the given payment request and returns its details")
.WithDescription("Takes an encoded payment request string and attempts to decode it, returning a full description of the conditions encoded within the payment request.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication";
    g.Parameters[1].Description = "The payment request string to be decoded.";
    return g;
})
.DisableAntiforgery();


app.MapGet("/sendpayment", async (string authToken, string paymentrequest, int timeout, long feelimit) =>
{
    try
    {
        await Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).SendPaymentAsync(paymentrequest, timeout, walletSettings.SendPaymentFee, feelimit);
        return new Result();
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result(ex);
    }
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
})
.DisableAntiforgery();

app.MapGet("/estimateroutefee", (string authToken, string paymentrequest) =>
{
    try
    {
        return new Result<RouteFeeRecord>(Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).EstimateRouteFee(paymentrequest, walletSettings.SendPaymentFee));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<RouteFeeRecord>(ex);
    }
})
.WithName("EstimateRouteFee")
.WithSummary("Estimates Route Fee for a payment via lightning network for the given payment request")
.WithDescription("Estimates Route Fee for a payment described by the passed paymentrequest to the final destination.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication";
    g.Parameters[1].Description = "A bare-bones invoice for a payment within the Lightning Network. With the details of the invoice, the sender has all the data necessary to send a payment to the recipient.";
    return g;
})
.DisableAntiforgery();

app.MapGet("/settleinvoice", (string authToken, string preimage) =>
{
    try
    {
        Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).SettleInvoice(preimage.AsBytes());
        return new Result();
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result(ex);
    }
})
.WithName("SettleInvoice")
.WithSummary("SettleInvoice settles an accepted invoice.")
.WithDescription("Settles hodl invoice that is identified by the payment hash deliverd from the given preimage.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication";
    g.Parameters[1].Description = "Externally discovered pre-image that should be used to settle the hold invoice.";
    return g;
})
.DisableAntiforgery();

app.MapGet("/cancelinvoice", (string authToken, string paymenthash) =>
{
    try
    {
        Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).CancelInvoice(paymenthash);
        return new Result();
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result(ex);
    }
})
.WithName("CancelInvoice")
.WithSummary("Cancels the invoice identified by the given payment hash")
.WithDescription("CancelInvoice cancels a currently open invoice. If the invoice is already canceled, this call will succeed. If the invoice is already settled, it will fail.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication";
    g.Parameters[1].Description = "Hash corresponding to the invoice to cancel.";
    return g;
})
.DisableAntiforgery();

app.MapGet("/getinvoice", async (string authToken, string paymenthash) =>
{
    try
    {
        return new Result<InvoiceRecord>(await Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).GetInvoiceAsync(paymenthash));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<InvoiceRecord>(ex);
    }
})
.WithName("GetInvoice")
.WithSummary("Returns an invoice identified by the given payment hash")
.WithDescription("Returns an invoice identified by the given payment hash")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication";
    g.Parameters[1].Description = "Hash corresponding to the invoice.";
    return g;
})
.DisableAntiforgery();

app.MapGet("/listinvoices", (string authToken) =>
{
    try
    {
        return new Result<InvoiceRecord[]>(Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).ListInvoices(true,true));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<InvoiceRecord[]>(ex);
    }
})
.WithName("ListInvoices")
.WithSummary("Returns list of all invoices related to the account")
.WithDescription("Returns list of all invoices related to the account")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication";
    return g;
})
.DisableAntiforgery();

app.MapGet("/listpayments", (string authToken) =>
{
    try
    {
        return new Result<PaymentRecord[]>(Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).ListNotFailedPayments());
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<PaymentRecord[]>(ex);
    }
})
.WithName("ListPayments")
.WithSummary("Returns list of all payments related to the account")
.WithDescription("Returns list of all payments related to the account")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication";
    return g;
})
.DisableAntiforgery();


app.MapGet("/getpayment", async (string authToken, string paymenthash) =>
{
    try
    {
        return new Result<PaymentRecord>(await Singlethon.LNDWalletManager.ValidateAuthTokenAndGetAccount(authToken).GetPaymentAsync(paymenthash));
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<PaymentRecord>(ex);
    }
})
.WithName("GetPayment")
.WithSummary("Returns a payment identified by the given payment hash")
.WithDescription("Returns a payment identified by the given payment hash")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication";
    g.Parameters[1].Description = "Hash corresponding to the payment.";
    return g;
})
.DisableAntiforgery();

app.MapHub<InvoiceStateUpdatesHub>("/invoicestateupdates")
.DisableAntiforgery();

app.MapHub<PaymentStatusUpdatesHub>("/paymentstatusupdates")
.DisableAntiforgery();

app.Run(walletSettings.ListenHost.AbsoluteUri);



[Serializable]
public struct Result
{
    public Result() { }
    public Result(Exception exception) {
        ErrorCode = LNDWalletErrorCode.OperationFailed;
        if (exception is LNDWalletException ex)
            ErrorCode = ex.ErrorCode;
        ErrorMessage = exception.Message;
    }
    public LNDWalletErrorCode ErrorCode { get; set; } = LNDWalletErrorCode.Ok;
    public string ErrorMessage { get; set; } = "";
}

[Serializable]
public struct Result<T>
{
    public Result(T value) { Value = value; }
    public Result(Exception exception) {
        ErrorCode = LNDWalletErrorCode.OperationFailed;
        if (exception is LNDWalletException ex)
            ErrorCode = ex.ErrorCode;
        ErrorMessage = exception.Message;
    }
    public T? Value { get; set; } = default;
    public LNDWalletErrorCode ErrorCode { get; set; } = LNDWalletErrorCode.Ok;
    public string ErrorMessage { get; set; } = "";
}


[Serializable]
public struct FeeEstimateRet
{
    public long FeeSat { get; set; }
    public ulong SatPerVbyte { get; set; }
}

[Serializable]
public struct PaymentRequestAndHashRet
{
    public string PaymentRequest { get; set; }
    public string PaymentHash { get; set; }
}

[Serializable]
public struct LndWalletBallanceRet
{
    public long ConfirmedBalance { get; set; }
    public long LockedBalance { get; set; }
    public long ReservedBalanceAnchorChan { get; set; }
    public long TotalBalance { get; set; }
    public long UnconfirmedBalance { get; set; }
}

public class WalletSettings
{
    public required string AdminPublicKey { get; set; }
    public required Uri ListenHost { get; set; }
    public required Uri ServiceUri { get; set; }
    public required string DBProvider { get; set; }
    public required string ConnectionString { get; set; }
    public required long SendPaymentFee { get; set; }
    public required long EstimatedChannelCloseFee { get; set; }
    public required long MaxChannelCloseFeePerVByte { get; set; }
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

public class BitcoinSettings
{
    public required string AuthenticationString { get; set; }
    public required string HostOrUri { get; set; }
    public required string Network { get; set; }
    public required string WalletName { get; set; }

}