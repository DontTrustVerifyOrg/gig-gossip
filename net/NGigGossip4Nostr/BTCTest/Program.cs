// See https://aka.ms/new-console-template for more information
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NBitcoin;
using NBitcoin.RPC;

IConfigurationRoot GetConfigurationRoot(string defaultFolder,string iniName)
{
    var basePath = Environment.GetEnvironmentVariable("GIGGOSSIP_BASEDIR");
    if(basePath==null)
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

var config = GetConfigurationRoot(".giggossip", "btctest.conf");

var bitcoinSettings = config.GetSection("bitcoin").Get<BitcoinSettings>();
var userSettings = config.GetSection("user").Get<UserSettings>();


var client = bitcoinSettings.NewRPCClient();

Console.WriteLine(client.GetBlockchainInfo().Blocks);

RPCClient wallet = null;
string walletName = userSettings.WalletName;
try
{
    wallet = client.LoadWallet(walletName);
}
catch (RPCException exception)
{
    if (exception.RPCCode == RPCErrorCode.RPC_WALLET_ALREADY_LOADED)
        wallet = client.SetWalletContext(walletName);
    if(exception.RPCCode == RPCErrorCode.RPC_WALLET_NOT_FOUND)
        wallet = client.CreateWallet(walletName, new CreateWalletOptions() { DisablePrivateKeys = true, LoadOnStartup = false, Descriptors = false });
}
wallet.ImportAddress(BitcoinAddress.Create(userSettings.BitcoinAddress, bitcoinSettings.GetNetwork()),null,true);
Console.WriteLine(wallet.GetBalance(6, true));

public class BitcoinSettings
{
    public string AuthenticationString { get; set; }
    public string HostOrUri { get; set; }
    public string Network { get; set; }

    public NBitcoin.Network GetNetwork()
    {
        if (Network.ToLower() == "main")
            return NBitcoin.Network.Main;
        if (Network.ToLower() == "testnet")
            return NBitcoin.Network.TestNet;
        if (Network.ToLower() == "regtest")
            return NBitcoin.Network.RegTest;
        throw new NotImplementedException();
    }

    public RPCClient NewRPCClient()
    {
        return new RPCClient(AuthenticationString, HostOrUri, GetNetwork());
    }

}

public class UserSettings
{
    public string WalletName { get; set; }
    public string BitcoinAddress { get; set; }
}