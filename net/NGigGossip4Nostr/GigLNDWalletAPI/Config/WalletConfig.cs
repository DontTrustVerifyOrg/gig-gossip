namespace GigLNDWalletAPI.Config
{
    public record WalletConfig
    {
        public const string SectionName = nameof(WalletConfig);
        public Uri ServiceUri { get; set; }
        public string ConnectionString { get; set; }
        public long NewAddressTxFee { get; set; }
        public long AddInvoiceTxFee { get; set; }
        public long SendPaymentTxFee { get; set; }
        public long FeeLimit { get; set; }
        public long EstimatedTxFee { get; set; }

    }
}
