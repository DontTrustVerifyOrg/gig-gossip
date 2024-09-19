using System;
using NBitcoin.RPC;

namespace LNDWallet;

public class BitcoinNode
{
    public string AuthenticationString { get; set; }
    public string HostOrUri { get; set; }
    public string Network { get; set; }
    public string WalletName { get; set; }

    public NBitcoin.Network GetNetwork() => Network.ToLower() switch
    {
        "main" => NBitcoin.Network.Main,
        "testnet" => NBitcoin.Network.TestNet,
        "regtest" => NBitcoin.Network.RegTest,
        _ => throw new NotImplementedException()
    };

    public RPCClient WalletClient()
    {
        var client = new RPCClient(AuthenticationString, HostOrUri, GetNetwork());
        try
        {
            // load bitcoin node wallet
            return client.LoadWallet(WalletName); ;
        }
        catch (RPCException exception) when (exception.RPCCode == RPCErrorCode.RPC_WALLET_ALREADY_LOADED)
        {
            return client.SetWalletContext(WalletName);
        }
    }

    public BitcoinNode(string authenticationString, string hostoruri, string network, string walletName)
	{
        this.AuthenticationString = authenticationString;
        this.HostOrUri = hostoruri;
        this.Network = network;
        this.WalletName = walletName;
    }

    public bool IsRegTest => this.Network.ToLower() == "regtest";


    public void Mine101Blocks()
    {
        WalletClient().Generate(101);
    }

    public void TopUpAndMine6Blocks(string bitcoinAddr, long satoshis)
	{
        WalletClient().SendToAddress(NBitcoin.BitcoinAddress.Create(bitcoinAddr, GetNetwork()), new NBitcoin.Money(satoshis));
        WalletClient().Generate(6);
    }

    public void SendToAddress(string bitcoinAddr, long satoshis)
    {
        WalletClient().SendToAddress(NBitcoin.BitcoinAddress.Create(bitcoinAddr, GetNetwork()), new NBitcoin.Money(satoshis));
    }

    public void GenerateBlocks(int number)
    {
        WalletClient().Generate(number);
    }

    public string NewAddress()
    {
        return WalletClient().GetNewAddress().ToString();
    }

    public long WalletBalance(int minConfirmations)
    {
        return WalletClient().GetBalance(minConfirmations, false).Satoshi;
    }

    public decimal EstimateFeeSatoshiPerByte(int confirmationTarget)
    {
        return WalletClient().EstimateSmartFee(confirmationTarget).FeeRate.SatoshiPerByte;
    }

}

