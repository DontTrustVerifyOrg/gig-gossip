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
        public StripeSettings StripeSettings;

        public Settings(string id, IConfigurationRoot config)
        {
            this.Id = id;
            NodeSettings = config.GetSection("node").Get<NodeSettings>();
            StripeSettings = config.GetSection("stripe").Get<StripeSettings>();
        }
    }
}

public class StripeSettings
{
    public string StripeMerchantDisplayName { get; set; }
    public string StripePublishableKey { get; set; }
}

public class NodeSettings
{
    public required string DBProvider { get; set; }
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
    public required string AllDriverProperties { get; set; }
    public required string RequiredDriverProperties { get; set; }
    public required int GeohashPrecision { get; set; }
    public required long ClosingFeeSat { get; set; }
    public required long PricePerKilometerSat { get; set; }
    public required long ClosingFeeFiat { get; set; }
    public required long PricePerKilometerFiat { get; set; }


    public string[] GetNostrRelays()
    {
        return (from s in JsonArray.Parse(NostrRelays)!.AsArray() select s.GetValue<string>()).ToArray();
    }

    public string[] GetRiderProperties()
    {
        return (from s in JsonArray.Parse(RiderProperties)!.AsArray() select s.GetValue<string>()).ToArray();
    }

    public string[] GetAllDriverProperties()
    {
        return (from s in JsonArray.Parse(AllDriverProperties)!.AsArray() select s.GetValue<string>()).ToArray();
    }

    public string[] GetRequiredDriverProperties()
    {
        return (from s in JsonArray.Parse(RequiredDriverProperties)!.AsArray() select s.GetValue<string>()).ToArray();
    }

}
