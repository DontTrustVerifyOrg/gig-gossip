using System;
using NBitcoin.RPC;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace RideShareCLIApp
{
	public class Settings
	{
        public string Script;
        public string Id;

        public NodeSettings NodeSettings;
        public SettlerAdminSettings SettlerAdminSettings;
        public BitcoinSettings BitcoinSettings;
        public ApplicationSettings ApplicationSettings;

        public Settings(string id, string script, IConfigurationRoot config)
        {
            this.Script = script;
            this.Id = id;
            NodeSettings = config.GetSection("node").Get<NodeSettings>();
            SettlerAdminSettings = config.GetSection("settleradmin").Get<SettlerAdminSettings>();
            BitcoinSettings = config.GetSection("bitcoin").Get<BitcoinSettings>();
            ApplicationSettings = config.GetSection("application").Get<ApplicationSettings>();
        }
    }
}

public class ApplicationSettings
{
    public required string FlowLoggerPath { get; set; }
}

public class SettlerAdminSettings
{
    public required Uri SettlerOpenApi { get; set; }
    public required string PrivateKey { get; set; }
}

public class NodeSettings
{
    public required string ConnectionString { get; set; }
    public required string SecureStorageConnectionString { get; set; }
    public required Uri GigWalletOpenApi { get; set; }
    public required string NostrRelays { get; set; }
    public required Uri SettlerOpenApi { get; set; }
    public required long PriceAmountForRouting { get; set; }
    public required long TimestampToleranceMs { get; set; }
    public required long InvoicePaymentTimeoutSec { get; set; }
    public required int ChunkSize { get; set; }
    public required int Fanout { get; set; }
    public required string RiderProperties { get; set; }
    public required string DriverProperties { get; set; }
    public required int GeohashPrecision { get; set; }

    public string[] GetNostrRelays()
    {
        return (from s in JsonArray.Parse(NostrRelays)!.AsArray() select s.GetValue<string>()).ToArray();
    }

    public string[] GetRiderProperties()
    {
        return (from s in JsonArray.Parse(RiderProperties)!.AsArray() select s.GetValue<string>()).ToArray();
    }

    public string[] GetDriverProperties()
    {
        return (from s in JsonArray.Parse(DriverProperties)!.AsArray() select s.GetValue<string>()).ToArray();
    }
    public GigLNDWalletAPIClient.swaggerClient GetLndWalletClient(HttpClient httpClient)
    {
        return new GigLNDWalletAPIClient.swaggerClient(GigWalletOpenApi.AbsoluteUri, httpClient);
    }
}


public class BitcoinSettings
{
    public required string AuthenticationString { get; set; }
    public required string HostOrUri { get; set; }
    public required string Network { get; set; }
    public required string WalletName { get; set; }

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