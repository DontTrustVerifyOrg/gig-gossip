using System;
using NBitcoin.RPC;

namespace GigLNDWalletAPI;

public class BitcoinNodeUtils
{
    RPCClient bitcoinClient;
    NBitcoin.Network network;
    string walletName;
    public BitcoinNodeUtils(RPCClient bitcoinClient, NBitcoin.Network network, string walletName)
	{
        this.bitcoinClient = bitcoinClient;
        this.network = network;
        this.walletName = walletName;
    }

    public bool IsRegTest => this.network == NBitcoin.Network.RegTest;

    public RPCClient walletClient()
    {
        // load bitcoin node wallet
        RPCClient? bitcoinWalletClient;
        try
        {
            bitcoinWalletClient = bitcoinClient.LoadWallet(walletName); ;
        }
        catch (RPCException exception) when (exception.RPCCode == RPCErrorCode.RPC_WALLET_ALREADY_LOADED)
        {
            bitcoinWalletClient = bitcoinClient.SetWalletContext(walletName);
        }

        return bitcoinWalletClient;
    }

    public void Mine101Blocks()
    {
        walletClient().Generate(101);
    }

    public void TopUpAndMine6Blocks(string bitcoinAddr, long satoshis)
	{
        walletClient().SendToAddress(NBitcoin.BitcoinAddress.Create(bitcoinAddr, network), new NBitcoin.Money(satoshis));
        walletClient().Generate(6);
    }

    public void SendToAddress(string bitcoinAddr, long satoshis)
    {
        walletClient().SendToAddress(NBitcoin.BitcoinAddress.Create(bitcoinAddr, network), new NBitcoin.Money(satoshis));
    }

    public void GenerateBlocks(int number)
    {
        walletClient().Generate(number);
    }

    public string NewAddress()
    {
        return walletClient().GetNewAddress().ToString();
    }

    public long WalletBallance(int minConf)
    {
        return walletClient().GetBalance(minConf, false).Satoshi;
    }

}

