namespace GigGossipSettlerAPI.Config
{
    public record SettlerConfig
    {
        public const string SectionName = nameof(SettlerConfig);
        public required Uri ServiceUri { get; set; }
        public required Uri GigWalletOpenApi { get; set; }
        public required long PriceAmountForSettlement { get; set; }
        public required string ConnectionString { get; set; }
        public required string SettlerPrivateKey { get; set; }
        public required long InvoicePaymentTimeoutSec { get; set; }
        public required long DisputeTimeoutSec { get; set; }
    }
}
