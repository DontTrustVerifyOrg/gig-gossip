using System;
using NBitcoin.RPC;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace RideShareCLIApp
{
	public class Settings
	{
        public string Id;

        public NodeSettings NodeSettings;
        public SettlerAdminSettings SettlerAdminSettings;
        public ApplicationSettings ApplicationSettings;

        public Settings(string id, IConfigurationRoot config)
        {
            this.Id = id;
            NodeSettings = config.GetSection("node").Get<NodeSettings>();
            SettlerAdminSettings = config.GetSection("settleradmin").Get<SettlerAdminSettings>();
            ApplicationSettings = config.GetSection("application").Get<ApplicationSettings>();
        }
    }
}

public class ApplicationSettings
{
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
    public required Uri LoggerOpenApi { get; set; }
    public required long PriceAmountForRouting { get; set; }
    public required long TimestampToleranceMs { get; set; }
    public required long InvoicePaymentTimeoutSec { get; set; }
    public required int ChunkSize { get; set; }
    public required int Fanout { get; set; }
    public required long FeeLimitSat { get; set; }
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

}
