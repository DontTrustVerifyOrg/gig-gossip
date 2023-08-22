// See https://aka.ms/new-console-template for more information
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NBitcoin;
using NBitcoin.RPC;

/// <summary>
/// This method retrieves the configuration information 
/// from an INI file in a specified path.
/// </summary>
/// <param name="defaultFolder">User Profile Folder Name</param> 
/// <param name="iniName">INI File Name</param>
/// <returns>A config object to retrieve settings from.</returns>
IConfigurationRoot GetConfigurationRoot(string defaultFolder,string iniName)
{
    // Fetch the base directory location from environment variables
    var basePath = Environment.GetEnvironmentVariable("GIGGOSSIP_BASEDIR");

    // If not set, use the user's profile folder
    if(basePath==null)
        basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), defaultFolder);

    // Overrides the base path if passed as command line argument
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

// Fetch configuration using defined method
var config = GetConfigurationRoot(".giggossip", "btctest.conf");

// Fetch specific sections of the configuration into relevant objects
var bitcoinSettings = config.GetSection("bitcoin").Get<BitcoinSettings>();
var userSettings = config.GetSection("user").Get<UserSettings>();

// Create a new RPC client from the configured bitcoin settings
var client = bitcoinSettings.NewRPCClient();

// Print the number of blocks in the Bitcoin blockchain
Console.WriteLine("Number of blocks: "+client.GetBlockchainInfo().Blocks.ToString());

RPCClient wallet = null;
string walletName = userSettings.WalletName;
try
{
    // Try to load the given wallet
    wallet = client.LoadWallet(walletName);
}
catch (RPCException exception)
{
    // Handle exceptions if wallet is already loaded or not found
    if (exception.RPCCode == RPCErrorCode.RPC_WALLET_ALREADY_LOADED)
        wallet = client.SetWalletContext(walletName);
    if(exception.RPCCode == RPCErrorCode.RPC_WALLET_NOT_FOUND)
        wallet = client.CreateWallet(walletName, new CreateWalletOptions() { DisablePrivateKeys = true, LoadOnStartup = false, Descriptors = false });
}

// Display the current balance in the wallet
Console.WriteLine("Wallet ballance: " + wallet.GetBalance(6, true).ToString());

public class BitcoinSettings
{
    public string AuthenticationString { get; set; }
    public string HostOrUri { get; set; }
    public string Network { get; set; }

    /// <summary>
    /// Retrieves the appropriate NBitcoin.Network object based on the network name provided.
    /// </summary>
    /// <returns>NBitcoin.Network object representing the chosen network</returns>
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

    /// <summary>
    /// Creates a new RPCClient instance with the properties defined in this object
    /// </summary>
    /// <returns>The new RPCClient object</returns>
    public RPCClient NewRPCClient()
    {
        return new RPCClient(AuthenticationString, HostOrUri, GetNetwork());
    }

}

public class UserSettings
{
    public string WalletName { get; set; }
}