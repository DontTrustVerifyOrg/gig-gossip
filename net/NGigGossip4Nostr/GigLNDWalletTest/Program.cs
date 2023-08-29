// See https://aka.ms/new-console-template for more information
using GigLNDWalletAPIClient;
using CryptoToolkit;
using NBitcoin.Secp256k1;
using Microsoft.Extensions.Configuration;

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

var config = GetConfigurationRoot(".giggossip", "wallettest.conf");

var userSettings = config.GetSection("user").Get<UserSettings>();

using (var httpClient = new HttpClient())
{
    var baseUrl = userSettings.GigWalletOpenApi;
    var client = new swaggerClient(baseUrl, httpClient);

    var ecpriv = userSettings.UserPrivateKey.AsECPrivKey();

    string pubkey = ecpriv.CreateXOnlyPubKey().AsHex();

    var guid = await client.GetTokenAsync(pubkey);

    var address= await client.NewAddressAsync(Crypto.MakeSignedTimedToken(ecpriv, DateTime.Now, guid), CancellationToken.None);

    var ballance = await client.GetBalanceAsync(Crypto.MakeSignedTimedToken(ecpriv, DateTime.Now, guid), CancellationToken.None);

    var inv = await client.AddInvoiceAsync(Crypto.MakeSignedTimedToken(ecpriv, DateTime.Now, guid), 1000, "", 8400, CancellationToken.None);

}

public class UserSettings
{
    public required string GigWalletOpenApi { get; set; }
    public required string UserPrivateKey { get; set; }
}