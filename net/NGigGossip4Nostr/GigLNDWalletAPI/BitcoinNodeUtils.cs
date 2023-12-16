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

	public void TopUpAndMine6Blocks(string bitcoinAddr, long satoshis)
	{
        bitcoinClient.SendToAddress(NBitcoin.BitcoinAddress.Create(bitcoinAddr, network), new NBitcoin.Money(satoshis));

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
        bitcoinWalletClient.Generate(6);
    }
}

