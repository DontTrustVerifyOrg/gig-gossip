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
        [Display(Name = "Top up")]
        TopUp,
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
        throw new NotImplementedException();
//        var ballanceDetails = WalletAPIResult.Get<AccountBallanceDetails>(await walletClient.GetBalanceDetailsAsync(await MakeToken(), CancellationToken.None));                
//        AnsiConsole.WriteLine(JsonConvert.SerializeObject(ballanceDetails, Formatting.Indented, new JsonConverter[] { new StringEnumConverter() }));
    }

    enum ClipType
    {
        Invoice=0,
        PaymentHash=1,
        Preimage=2
    }

    private void ToClipboard(ClipType clipType, string value)
    {
        var clip = TextCopy.ClipboardService.GetText();
        if (string.IsNullOrWhiteSpace(clip) || !clip.StartsWith("GigLNDWalletTest\n"))
            clip= string.Join("\n", new string[] { "GigLNDWalletTest", "", "", "" });
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
                AnsiConsole.MarkupLine("[yellow]Invoice State Change:"+invstateupd+"[/]");
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
                var cmd = Prompt.Select<CommandEnum>("Select command");
                if (cmd == CommandEnum.Exit)
                {
                    if (cmd == CommandEnum.Exit)
                        break;
                }
                else if (cmd == CommandEnum.BalanceDetails)
                {
                    await WriteBalanceDetails();
                }
                else if (cmd == CommandEnum.TopUp)
                {
                    var topUpAmount = Prompt.Input<int>("How much top up");
                    if (topUpAmount > 0)
                    {
                        var newBitcoinAddressOfCustomer = WalletAPIResult.Get<string>(await walletClient.NewAddressAsync(await MakeToken()));
                        WalletAPIResult.Check(await walletClient.TopUpAndMine6BlocksAsync(newBitcoinAddressOfCustomer, topUpAmount));
                    }
                }
                else if (cmd == CommandEnum.AddInvoice)
                {
                    var satoshis = Prompt.Input<long>("Satoshis");
                    var memo = Prompt.Input<string>("Memo");
                    var expiry = Prompt.Input<long>("Expiry");
                    var inv = WalletAPIResult.Get<InvoiceRet>(await walletClient.AddInvoiceAsync(await MakeToken(), satoshis, memo, expiry));
                    ToClipboard(ClipType.Invoice, inv.PaymentRequest);
                    ToClipboard(ClipType.PaymentHash, inv.PaymentHash);
                    AnsiConsole.WriteLine(inv.PaymentRequest);
                    AnsiConsole.WriteLine(inv.PaymentHash);
                    await invoiceStateUpdatesClient.MonitorAsync(await MakeToken(), inv.PaymentHash);
                }
                else if (cmd == CommandEnum.AddHodlInvoice)
                {
                    var satoshis = Prompt.Input<long>("Satoshis");
                    var memo = Prompt.Input<string>("Memo");
                    var expiry = Prompt.Input<long>("Expiry");
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
                    var paymentreq = Prompt.Input<string>("Payment Request");
                    if (string.IsNullOrWhiteSpace(paymentreq))
                    {
                        paymentreq = FromClipboard(ClipType.Invoice);
                        AnsiConsole.WriteLine(paymentreq);
                    }
                    var pay = WalletAPIResult.Get<PayReq>(await walletClient.DecodeInvoiceAsync(await MakeToken(), paymentreq));
                    AnsiConsole.WriteLine($"Satoshis:{pay.NumSatoshis}");
                    AnsiConsole.WriteLine($"Memo:{pay.Description}");
                    AnsiConsole.WriteLine($"Expiry:{pay.Expiry}");
                    AnsiConsole.WriteLine($"Payment Hash:{pay.PaymentHash}");
                    ToClipboard(ClipType.PaymentHash, pay.PaymentHash);
                    if (Prompt.Confirm("Are you sure?"))
                    {
                        var timeout = Prompt.Input<int>("Timeout");
                        WalletAPIResult.Check(await walletClient.SendPaymentAsync(await MakeToken(), paymentreq, timeout));
                        await paymentStatusUpdatesClient.MonitorAsync(await MakeToken(), pay.PaymentHash);
                    }
                }
                else if (cmd == CommandEnum.CancelInvoice)
                {
                    var paymenthash = Prompt.Input<string>("Payment Hash");
                    if (string.IsNullOrWhiteSpace(paymenthash))
                    {
                        paymenthash = FromClipboard(ClipType.PaymentHash);
                        AnsiConsole.WriteLine(paymenthash);
                    }
                    WalletAPIResult.Check(await walletClient.CancelInvoiceAsync(await MakeToken(), paymenthash));
                }
                else if (cmd == CommandEnum.SettleInvoice)
                {
                    var preimage = Prompt.Input<string>("Preimage");
                    if (string.IsNullOrWhiteSpace(preimage))
                    {
                        preimage = FromClipboard(ClipType.Preimage);
                        AnsiConsole.WriteLine(preimage);
                    }
                    var paymenthash = Crypto.ComputePaymentHash(preimage.AsBytes()).AsHex();
                    AnsiConsole.WriteLine(paymenthash);
                    WalletAPIResult.Check(await walletClient.SettleInvoiceAsync(await MakeToken(), preimage));
                    ToClipboard(ClipType.PaymentHash, paymenthash);
                }
                else if (cmd == CommandEnum.GetPaymentStatus)
                {
                    var paymenthash = Prompt.Input<string>("Payment Hash");
                    if (string.IsNullOrWhiteSpace(paymenthash))
                    {
                        paymenthash = FromClipboard(ClipType.PaymentHash);
                        AnsiConsole.WriteLine(paymenthash);
                    }
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