using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Threading;
using CryptoToolkit;
using GigLNDWalletAPIClient;
using Microsoft.Extensions.Configuration;
using NBitcoin;
using NBitcoin.RPC;
using NBitcoin.Secp256k1;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Sharprompt;
using Spectre.Console;

namespace GigLNDWalletCLI;

public class GigLNDWalletCLI
{
    UserSettings userSettings;
    swaggerClient walletClient;
    InvoiceStateUpdatesClient invoiceStateUpdatesClient;
    PaymentStatusUpdatesClient paymentStatusUpdatesClient;
    CancellationTokenSource CancellationTokenSource = new();

    static IConfigurationRoot GetConfigurationRoot(string? basePath, string[] args, string defaultFolder, string iniName)
    {
        if (basePath == null)
        {
            basePath = Environment.GetEnvironmentVariable("GIGGOSSIP_BASEDIR");
            if (basePath == null)
                basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), defaultFolder);
        }
        var builder = new ConfigurationBuilder();
        builder.SetBasePath(basePath)
               .AddIniFile(iniName)
               .AddEnvironmentVariables()
               .AddCommandLine(args);

        return builder.Build();
    }

    public GigLNDWalletCLI(string[] args, string baseDir, string sfx)
    {
        if (sfx == null)
            sfx = AnsiConsole.Prompt(new TextPrompt<string>("Enter the [orange1]config suffix[/]?"));

        sfx = (string.IsNullOrWhiteSpace(sfx)) ? "" : "_" + sfx;

        IConfigurationRoot config = GetConfigurationRoot(baseDir, args, ".giggossip", "wallettest" + sfx + ".conf");

        this.userSettings = config.GetSection("user").Get<UserSettings>();

        var baseUrl = userSettings.GigWalletOpenApi;
        walletClient = new swaggerClient(baseUrl, new HttpClient());

        invoiceStateUpdatesClient = new InvoiceStateUpdatesClient(walletClient);
        paymentStatusUpdatesClient = new PaymentStatusUpdatesClient(walletClient);
    }

    public enum CommandEnum
    {
        [Display(Name = "Exit App")]
        Exit,
        [Display(Name = "Refresh")]
        Refresh,
        [Display(Name = "Balance Details")]
        BalanceDetails,
        [Display(Name = "Estimate Fee")]
        EstimateFee,
        [Display(Name = "New Address")]
        NewAddress,
        [Display(Name = "New Bitcoin Address")]
        NewBitcoinAddress,
        [Display(Name = "Send To Address")]
        SendToAddress,
        [Display(Name = "Generate Blocks")]
        GenerateBlocks,
        [Display(Name = "Bitcoin Wallet Ballance")]
        BitcoinWalletBallance,
        [Display(Name = "Lnd Node Wallet Ballance")]
        LndWalletBallance,
        [Display(Name = "Add Invoice")]
        AddInvoice,
        [Display(Name = "Add Hodl Invoice")]
        AddHodlInvoice,
        [Display(Name = "Accept Invoice")]
        AcceptInvoice,
        [Display(Name = "Cancel Invoice")]
        CancelInvoice,
        [Display(Name = "Settle Invoice")]
        SettleInvoice,
        [Display(Name = "Get Invoice State")]
        GetInvoiceState,
        [Display(Name = "Get Payment Status")]
        GetPaymentStatus,
        [Display(Name = "Payout")]
        Payout,
        [Display(Name = "Open Reserve")]
        OpenReserve,
        [Display(Name = "Close Reserve")]
        CloseReserve,
    }

    public async Task<string> MakeToken()
    {
        var ecpriv = userSettings.UserPrivateKey.AsECPrivKey();
        string pubkey = ecpriv.CreateXOnlyPubKey().AsHex();
        var guid = WalletAPIResult.Get<Guid>(await walletClient.GetTokenAsync(pubkey));
        return Crypto.MakeSignedTimedToken(ecpriv, DateTime.UtcNow, guid);
    }

    private async Task WriteBalance()
    {
        var ballanceOfCustomer = WalletAPIResult.Get<long>(await walletClient.GetBalanceAsync(await MakeToken(), CancellationToken.None));
        AnsiConsole.WriteLine("Current amout in satoshis:" + ballanceOfCustomer.ToString());
    }

    private async Task WriteBalanceDetails()
    {
        var ballanceDetails = WalletAPIResult.Get<AccountBallanceDetails>(await walletClient.GetBalanceDetailsAsync(await MakeToken(), CancellationToken.None));                
        AnsiConsole.WriteLine(JsonConvert.SerializeObject(ballanceDetails, Formatting.Indented, new JsonConverter[] { new StringEnumConverter() }));
    }

    enum ClipType
    {
        Invoice=0,
        PaymentHash=1,
        Preimage=2,
        BitcoinAddr=3,
        ReserveId=4,
    }

    private void ToClipboard(ClipType clipType, string value)
    {
        var clip = TextCopy.ClipboardService.GetText();
        if (string.IsNullOrWhiteSpace(clip) || !clip.StartsWith("GigLNDWalletTest\n"))
        {
            var ini = new List<string>() { "GigLNDWalletTest" };
            for (var i = 0; i <= Enum.GetValues(typeof(ClipType)).Cast<int>().Max(); i++)
                ini.Add("");
            clip = string.Join("\n", ini);
        }
        var clarr = clip.Split("\n");
        clarr[((int)clipType) + 1] = value;
        TextCopy.ClipboardService.SetText(string.Join("\n", clarr));
    }

    private string FromClipboard(ClipType clipType)
    {
        var clip = TextCopy.ClipboardService.GetText();
        if (string.IsNullOrWhiteSpace(clip) || !clip.StartsWith("GigLNDWalletTest\n"))
            return "";
        var clarr = clip.Split("\n");
        return clarr[((int)clipType) + 1];
    }

    Thread invoiceMonitorThread;
    Thread paymentMonitorThread;
    public async Task RunAsync()
    {
        invoiceMonitorThread = new Thread(async () =>
        {
            await invoiceStateUpdatesClient.ConnectAsync(await MakeToken());
            try
            {
                await foreach (var invstateupd in invoiceStateUpdatesClient.StreamAsync(await MakeToken(), CancellationTokenSource.Token))
                {
                    AnsiConsole.MarkupLine("[yellow]Invoice State Change:" + invstateupd + "[/]");
                    await WriteBalance();
                }
            }
            catch (OperationCanceledException)
            {
                //stream closed
                return;
            };
        });
        invoiceMonitorThread.Start();

        paymentMonitorThread = new Thread(async () =>
        {
            await paymentStatusUpdatesClient.ConnectAsync(await MakeToken());
            try
            { 
                await foreach (var paystateupd in paymentStatusUpdatesClient.StreamAsync(await MakeToken(), CancellationTokenSource.Token))
                {
                    AnsiConsole.MarkupLine("[yellow]Payment Status Change:" + paystateupd+"[/]");
                    await WriteBalance();
                }
            }
            catch (OperationCanceledException)
            {
                //stream closed
                return;
            };
        });
        paymentMonitorThread.Start();

        while (true)
        {
            try
            {
                await WriteBalance();
                var cmd = Prompt.Select<CommandEnum>("Select command",pageSize:6);
                if (cmd == CommandEnum.Exit)
                {
                    if (cmd == CommandEnum.Exit)
                        break;
                }
                else if (cmd == CommandEnum.BalanceDetails)
                {
                    await WriteBalanceDetails();
                }
                else if (cmd == CommandEnum.NewAddress)
                {
                    var newBitcoinAddressOfCustomer = WalletAPIResult.Get<string>(await walletClient.NewAddressAsync(await MakeToken()));
                    ToClipboard(ClipType.BitcoinAddr, newBitcoinAddressOfCustomer);
                    AnsiConsole.WriteLine(newBitcoinAddressOfCustomer);
                }
                else if (cmd == CommandEnum.NewBitcoinAddress)
                {
                    var newBitcoinAddressOfBitcoinWallet = WalletAPIResult.Get<string>(await walletClient.NewBitcoinAddressAsync(await MakeToken()));
                    ToClipboard(ClipType.BitcoinAddr, newBitcoinAddressOfBitcoinWallet);
                    AnsiConsole.WriteLine(newBitcoinAddressOfBitcoinWallet);
                }
                else if (cmd == CommandEnum.SendToAddress)
                {
                    var satoshis = Prompt.Input<int>("How much to send", 100000);
                    if (satoshis > 0)
                    {
                        var bitcoinAddressOfCustomer = Prompt.Input<string>("Bitcoin Address", FromClipboard(ClipType.BitcoinAddr));
                        AnsiConsole.WriteLine(bitcoinAddressOfCustomer);
                        WalletAPIResult.Check(await walletClient.SendToAddressAsync(await MakeToken(), bitcoinAddressOfCustomer, satoshis));
                    }
                }
                else if (cmd == CommandEnum.GenerateBlocks)
                {
                    var numblocks = Prompt.Input<int>("How many blocks", 6);
                    if (numblocks > 0)
                    {
                        WalletAPIResult.Check(await walletClient.GenerateBlocksAsync(await MakeToken(), numblocks));
                    }
                }
                else if (cmd == CommandEnum.BitcoinWalletBallance)
                {
                    var minconf = Prompt.Input<int>("How many confirtmations", 6);
                    var ballance = WalletAPIResult.Get<long>(await walletClient.GetBitcoinWalletBallanceAsync(await MakeToken(), minconf));
                    AnsiConsole.WriteLine($"Ballance={ballance}");
                }
                else if (cmd == CommandEnum.LndWalletBallance)
                {
                    var ballance = WalletAPIResult.Get<LndWalletBallanceRet>(await walletClient.GetLndWalletBallanceAsync(await MakeToken()));
                    AnsiConsole.WriteLine($"TotalBalance={ballance.TotalBalance}");
                    AnsiConsole.WriteLine($"ConfirmedBalance={ballance.ConfirmedBalance}");
                    AnsiConsole.WriteLine($"UnconfirmedBalance={ballance.UnconfirmedBalance}");
                    AnsiConsole.WriteLine($"LockedBalance={ballance.LockedBalance}");
                    AnsiConsole.WriteLine($"ReservedBalanceAnchorChan={ballance.ReservedBalanceAnchorChan}");
                }
                else if (cmd == CommandEnum.EstimateFee)
                {
                    var satoshis = Prompt.Input<int>("How much satoshis to send", 100000);
                    if (satoshis > 0)
                    {
                        var bitcoinAddressOfCustomer = Prompt.Input<string>("Bitcoin Address", FromClipboard(ClipType.BitcoinAddr));
                        var feeEr = WalletAPIResult.Get<FeeEstimateRet>(await walletClient.EstimateFeeAsync(await MakeToken(), bitcoinAddressOfCustomer, satoshis));
                        AnsiConsole.WriteLine($"FeeSat={feeEr.FeeSat},SatPerVbyte={feeEr.SatPerVbyte}");
                    }
                }
                else if (cmd == CommandEnum.Payout)
                {
                    var payoutAmount = Prompt.Input<int>("How much to payout");
                    var txFee = Prompt.Input<int>("TxFee");
                    if (payoutAmount > 0)
                    {
                        var payoutId = WalletAPIResult.Get<Guid>(await walletClient.RegisterPayoutAsync(await MakeToken(), payoutAmount, FromClipboard(ClipType.BitcoinAddr), txFee));
                        AnsiConsole.WriteLine(payoutId.ToString());
                    }
                }
                else if (cmd == CommandEnum.OpenReserve)
                {
                    var satoshis = Prompt.Input<int>("How much satoshis to reserve", 100000);
                    if (satoshis > 0)
                    {
                        var reserveId = WalletAPIResult.Get<Guid>(await walletClient.OpenReserveAsync(await MakeToken(), satoshis));
                        ToClipboard(ClipType.ReserveId, reserveId.ToString());
                        AnsiConsole.WriteLine(reserveId.ToString());
                    }
                }
                else if (cmd == CommandEnum.CloseReserve)
                {
                    var reserveId = Prompt.Input<string>("ReserveId", FromClipboard(ClipType.ReserveId));
                    WalletAPIResult.Check(await walletClient.CloseReserveAsync(await MakeToken(), reserveId));
                }
                else if (cmd == CommandEnum.AddInvoice)
                {
                    var satoshis = Prompt.Input<long>("Satoshis", 1000L);
                    var memo = Prompt.Input<string>("Memo", "test");
                    var expiry = Prompt.Input<long>("Expiry", 1000L);
                    var inv = WalletAPIResult.Get<InvoiceRet>(await walletClient.AddInvoiceAsync(await MakeToken(), satoshis, memo, expiry));
                    ToClipboard(ClipType.Invoice, inv.PaymentRequest);
                    ToClipboard(ClipType.PaymentHash, inv.PaymentHash);
                    AnsiConsole.WriteLine(inv.PaymentRequest);
                    AnsiConsole.WriteLine(inv.PaymentHash);
                    await invoiceStateUpdatesClient.MonitorAsync(await MakeToken(), inv.PaymentHash);
                }
                else if (cmd == CommandEnum.AddHodlInvoice)
                {
                    var satoshis = Prompt.Input<long>("Satoshis", 1000L);
                    var memo = Prompt.Input<string>("Memo", "hodl");
                    var expiry = Prompt.Input<long>("Expiry", 1000L);
                    var preimage = Crypto.GenerateRandomPreimage();
                    AnsiConsole.WriteLine(preimage.AsHex());
                    var hash = Crypto.ComputePaymentHash(preimage).AsHex();
                    var inv = WalletAPIResult.Get<InvoiceRet>(await walletClient.AddHodlInvoiceAsync(await MakeToken(), satoshis, hash, memo, expiry));
                    ToClipboard(ClipType.Preimage, preimage.AsHex());
                    ToClipboard(ClipType.Invoice, inv.PaymentRequest);
                    ToClipboard(ClipType.PaymentHash, inv.PaymentHash);
                    AnsiConsole.WriteLine(inv.PaymentRequest);
                    AnsiConsole.WriteLine(inv.PaymentHash);
                    await invoiceStateUpdatesClient.MonitorAsync(await MakeToken(), inv.PaymentHash);
                }
                else if (cmd == CommandEnum.AcceptInvoice)
                {
                    var paymentreq = Prompt.Input<string>("Payment Request", FromClipboard(ClipType.Invoice));
                    AnsiConsole.WriteLine(paymentreq);
                    var pay = WalletAPIResult.Get<PayReq>(await walletClient.DecodeInvoiceAsync(await MakeToken(), paymentreq));
                    AnsiConsole.WriteLine($"Satoshis:{pay.NumSatoshis}");
                    AnsiConsole.WriteLine($"Memo:{pay.Description}");
                    AnsiConsole.WriteLine($"Expiry:{pay.Expiry}");
                    AnsiConsole.WriteLine($"Payment Hash:{pay.PaymentHash}");
                    ToClipboard(ClipType.PaymentHash, pay.PaymentHash);
                    var timeout = Prompt.Input<int>("Timeout", 1000);
                    await paymentStatusUpdatesClient.MonitorAsync(await MakeToken(), pay.PaymentHash);
                    WalletAPIResult.Check(await walletClient.SendPaymentAsync(await MakeToken(), paymentreq, timeout));
                }
                else if (cmd == CommandEnum.CancelInvoice)
                {
                    var paymenthash = Prompt.Input<string>("Payment Hash", FromClipboard(ClipType.PaymentHash));
                    AnsiConsole.WriteLine(paymenthash);
                    WalletAPIResult.Check(await walletClient.CancelInvoiceAsync(await MakeToken(), paymenthash));
                }
                else if (cmd == CommandEnum.SettleInvoice)
                {
                    var preimage = Prompt.Input<string>("Preimage", FromClipboard(ClipType.Preimage));
                    AnsiConsole.WriteLine(preimage);
                    var paymenthash = Crypto.ComputePaymentHash(preimage.AsBytes()).AsHex();
                    AnsiConsole.WriteLine(paymenthash);
                    WalletAPIResult.Check(await walletClient.SettleInvoiceAsync(await MakeToken(), preimage));
                    ToClipboard(ClipType.PaymentHash, paymenthash);
                }
                else if (cmd == CommandEnum.GetPaymentStatus)
                {
                    var paymenthash = Prompt.Input<string>("Payment Hash", FromClipboard(ClipType.PaymentHash));
                    AnsiConsole.WriteLine(paymenthash);
                    var payStatus = WalletAPIResult.Get<string>(await walletClient.GetPaymentStatusAsync(await MakeToken(), paymenthash));
                    AnsiConsole.WriteLine($"Payment Status:{payStatus}");
                }
                else if (cmd == CommandEnum.GetInvoiceState)
                {
                    var paymenthash = Prompt.Input<string>("Payment Hash");
                    if (string.IsNullOrWhiteSpace(paymenthash))
                    {
                        paymenthash = FromClipboard(ClipType.PaymentHash);
                        AnsiConsole.WriteLine(paymenthash);
                    }
                    var invState = WalletAPIResult.Get<string>(await walletClient.GetInvoiceStateAsync(await MakeToken(), paymenthash));
                    AnsiConsole.WriteLine($"Invoice State:{invState}");
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex,
                    ExceptionFormats.ShortenPaths | ExceptionFormats.ShortenTypes |
                    ExceptionFormats.ShortenMethods | ExceptionFormats.ShowLinks);
            }
        }
        CancellationTokenSource.Cancel();
        invoiceMonitorThread.Join();
        paymentMonitorThread.Join();
    }
}

public class UserSettings
{
    public required string GigWalletOpenApi { get; set; }
    public required string UserPrivateKey { get; set; }
}