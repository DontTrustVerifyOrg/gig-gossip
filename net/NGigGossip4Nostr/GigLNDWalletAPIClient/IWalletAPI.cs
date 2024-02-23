using System;
namespace GigLNDWalletAPIClient
{
    public interface IWalletAPI
    {
        string BaseUrl { get; }
        Task<GuidResult> GetTokenAsync(string pubkey, System.Threading.CancellationToken cancellationToken);
        Task<Result> TopUpAndMine6BlocksAsync(string authToken, string bitcoinAddr, long satoshis, System.Threading.CancellationToken cancellationToken);
        Task<Result> SendToAddressAsync(string authToken, string bitcoinAddr, long satoshis, System.Threading.CancellationToken cancellationToken);
        Task<Result> GenerateBlocksAsync(string authToken, int blocknum, System.Threading.CancellationToken cancellationToken);
        Task<StringResult> NewBitcoinAddressAsync(string authToken, System.Threading.CancellationToken cancellationToken);
        Task<Int64Result> GetBitcoinWalletBallanceAsync(string authToken, int minConf, System.Threading.CancellationToken cancellationToken);
        Task<LndWalletBallanceRetResult> GetLndWalletBallanceAsync(string authToken, System.Threading.CancellationToken cancellationToken);
        Task<GuidResult> OpenReserveAsync(string authToken, long satoshis, System.Threading.CancellationToken cancellationToken);
        Task<Result> CloseReserveAsync(string authToken, string reserveId, System.Threading.CancellationToken cancellationToken);
        Task<FeeEstimateRetResult> EstimateFeeAsync(string authToken, string address, long satoshis, System.Threading.CancellationToken cancellationToken);
        Task<Int64Result> GetBalanceAsync(string authToken, System.Threading.CancellationToken cancellationToken);
        Task<AccountBallanceDetailsResult> GetBalanceDetailsAsync(string authToken, System.Threading.CancellationToken cancellationToken);
        Task<StringResult> NewAddressAsync(string authToken, System.Threading.CancellationToken cancellationToken);
        Task<GuidResult> RegisterPayoutAsync(string authToken, long satoshis, string btcAddress, long txfee, System.Threading.CancellationToken cancellationToken);
        Task<InvoiceRetResult> AddInvoiceAsync(string authToken, long satoshis, string memo, long expiry, System.Threading.CancellationToken cancellationToken);
        Task<InvoiceRetResult> AddHodlInvoiceAsync(string authToken, long satoshis, string hash, string memo, long expiry, System.Threading.CancellationToken cancellationToken);
        Task<PayReqResult> DecodeInvoiceAsync(string authToken, string paymentRequest, System.Threading.CancellationToken cancellationToken);
        Task<Result> SendPaymentAsync(string authToken, string paymentrequest, int timeout, System.Threading.CancellationToken cancellationToken);
        Task<Result> SettleInvoiceAsync(string authToken, string preimage, System.Threading.CancellationToken cancellationToken);
        Task<Result> CancelInvoiceAsync(string authToken, string paymenthash, System.Threading.CancellationToken cancellationToken);
        Task<StringResult> GetInvoiceStateAsync(string authToken, string paymenthash, System.Threading.CancellationToken cancellationToken);
        Task<StringResult> GetPaymentStatusAsync(string authToken, string paymenthash, System.Threading.CancellationToken cancellationToken);
    }

    public partial class swaggerClient : IWalletAPI
    {

    }
}

