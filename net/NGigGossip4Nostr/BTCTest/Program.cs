// See https://aka.ms/new-console-template for more information
using System.Text.Json;
using NBitcoin;
using NBitcoin.RPC;

var client = new RPCClient("lnd:lightning", "127.0.0.1:18332",NBitcoin.Network.RegTest);

Console.WriteLine(client.GetBlockchainInfo().Blocks);

RPCClient wallet = null;
string walletName = "wallet5";
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
wallet.ImportAddress(BitcoinAddress.Create("2N8qDyC8MEPZz57WYnhsQPXat7ASNkb6vkJ", NBitcoin.Network.RegTest),null,true);
Console.WriteLine(wallet.GetBalance(6, true));

