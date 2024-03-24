using System;
using System.Net.Http;
using System.Threading;
using Microsoft.AspNetCore.SignalR.Client;
using NetworkClientToolkit;

namespace GigLNDWalletAPIClient;

public interface IWalletAPI
{
    string BaseUrl { get; }
    IRetryPolicy RetryPolicy { get; }
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
    Task<PayReqRetResult> DecodeInvoiceAsync(string authToken, string paymentRequest, System.Threading.CancellationToken cancellationToken);
    Task<RouteFeeResponseResult> EstimateRouteFeeAsync(string authToken, string paymentrequest, CancellationToken cancellationToken);
    Task<Result> SendPaymentAsync(string authToken, string paymentrequest, int timeout, long feelimit, System.Threading.CancellationToken cancellationToken);
    Task<Result> SettleInvoiceAsync(string authToken, string preimage, System.Threading.CancellationToken cancellationToken);
    Task<Result> CancelInvoiceAsync(string authToken, string paymenthash, System.Threading.CancellationToken cancellationToken);
    Task<StringResult> GetInvoiceStateAsync(string authToken, string paymenthash, System.Threading.CancellationToken cancellationToken);
    Task<StringResult> GetPaymentStatusAsync(string authToken, string paymenthash, System.Threading.CancellationToken cancellationToken);
    Task<InvoiceRetArrayResult> ListInvoicesAsync(string authToken, System.Threading.CancellationToken cancellationToken);
    Task<PaymentRetArrayResult> ListPaymentsAsync(string authToken, System.Threading.CancellationToken cancellationToken);

    IInvoiceStateUpdatesClient CreateInvoiceStateUpdatesClient();
    IPaymentStatusUpdatesClient CreatePaymentStatusUpdatesClient();
}

public partial class swaggerClient : IWalletAPI
{
    public IRetryPolicy RetryPolicy { get; set; } = null;

    public swaggerClient(string baseUrl, System.Net.Http.HttpClient httpClient, IRetryPolicy retryPolicy) : this(baseUrl, httpClient)
    {
        RetryPolicy = retryPolicy;
    }

    public IInvoiceStateUpdatesClient CreateInvoiceStateUpdatesClient()
    {
        return new InvoiceStateUpdatesClient(this);
    }

    public IPaymentStatusUpdatesClient CreatePaymentStatusUpdatesClient()
    {
        return new PaymentStatusUpdatesClient(this);
    }
}

public class WalletAPIRetryWrapper : IWalletAPI
{
    IWalletAPI api;
    public string BaseUrl => api.BaseUrl;
    public IRetryPolicy RetryPolicy => api.RetryPolicy;

    public WalletAPIRetryWrapper(string baseUrl, System.Net.Http.HttpClient httpClient, IRetryPolicy retryPolicy)
    {
        this.api = new swaggerClient(baseUrl, httpClient, retryPolicy);
    }

    public IInvoiceStateUpdatesClient CreateInvoiceStateUpdatesClient()
    {
        return api.CreateInvoiceStateUpdatesClient();
    }

    public IPaymentStatusUpdatesClient CreatePaymentStatusUpdatesClient()
    {
        return api.CreatePaymentStatusUpdatesClient();
    }


    public async Task<InvoiceRetResult> AddHodlInvoiceAsync(string authToken, long satoshis, string hash, string memo, long expiry, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.AddHodlInvoiceAsync(authToken, satoshis, hash, memo, expiry, cancellationToken));
    }

    public async Task<InvoiceRetResult> AddInvoiceAsync(string authToken, long satoshis, string memo, long expiry, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.AddInvoiceAsync(authToken, satoshis, memo, expiry, cancellationToken));
    }

    public async Task<Result> CancelInvoiceAsync(string authToken, string paymenthash, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.CancelInvoiceAsync(authToken, paymenthash, cancellationToken));
    }

    public async Task<Result> CloseReserveAsync(string authToken, string reserveId, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.CloseReserveAsync(authToken, reserveId, cancellationToken));
    }

    public async Task<PayReqRetResult> DecodeInvoiceAsync(string authToken, string paymentRequest, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.DecodeInvoiceAsync(authToken, paymentRequest, cancellationToken));
    }

    public async Task<FeeEstimateRetResult> EstimateFeeAsync(string authToken, string address, long satoshis, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.EstimateFeeAsync(authToken, address, satoshis, cancellationToken));
    }

    public async Task<Result> GenerateBlocksAsync(string authToken, int blocknum, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GenerateBlocksAsync(authToken, blocknum, cancellationToken));
    }

    public async Task<Int64Result> GetBalanceAsync(string authToken, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GetBalanceAsync(authToken, cancellationToken));
    }

    public async Task<AccountBallanceDetailsResult> GetBalanceDetailsAsync(string authToken, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GetBalanceDetailsAsync(authToken, cancellationToken));
    }

    public async Task<Int64Result> GetBitcoinWalletBallanceAsync(string authToken, int minConf, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GetBitcoinWalletBallanceAsync(authToken, minConf, cancellationToken));
    }

    public async Task<StringResult> GetInvoiceStateAsync(string authToken, string paymenthash, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GetInvoiceStateAsync(authToken, paymenthash, cancellationToken));
    }

    public async Task<LndWalletBallanceRetResult> GetLndWalletBallanceAsync(string authToken, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GetLndWalletBallanceAsync(authToken, cancellationToken));
    }

    public async Task<StringResult> GetPaymentStatusAsync(string authToken, string paymenthash, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GetPaymentStatusAsync(authToken, paymenthash, cancellationToken));
    }

    public async Task<GuidResult> GetTokenAsync(string pubkey, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GetTokenAsync(pubkey, cancellationToken));
    }

    public async Task<StringResult> NewAddressAsync(string authToken, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.NewAddressAsync(authToken, cancellationToken));
    }

    public async Task<StringResult> NewBitcoinAddressAsync(string authToken, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.NewBitcoinAddressAsync(authToken, cancellationToken));
    }

    public async Task<GuidResult> OpenReserveAsync(string authToken, long satoshis, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.OpenReserveAsync(authToken, satoshis, cancellationToken));
    }

    public async Task<GuidResult> RegisterPayoutAsync(string authToken, long satoshis, string btcAddress, long txfee, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.RegisterPayoutAsync(authToken, satoshis, btcAddress, txfee, cancellationToken));
    }

    public async Task<Result> SendPaymentAsync(string authToken, string paymentrequest, int timeout, long feelimit, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.SendPaymentAsync(authToken, paymentrequest, timeout, feelimit, cancellationToken));
    }

    public async Task<Result> SendToAddressAsync(string authToken, string bitcoinAddr, long satoshis, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.SendToAddressAsync(authToken, bitcoinAddr, satoshis, cancellationToken));
    }

    public async Task<Result> SettleInvoiceAsync(string authToken, string preimage, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.SettleInvoiceAsync(authToken, preimage, cancellationToken));
    }

    public async Task<Result> TopUpAndMine6BlocksAsync(string authToken, string bitcoinAddr, long satoshis, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.TopUpAndMine6BlocksAsync(authToken, bitcoinAddr, satoshis, cancellationToken));
    }

    public async Task<InvoiceRetArrayResult> ListInvoicesAsync(string authToken, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.ListInvoicesAsync(authToken, cancellationToken));
    }

    public async Task<PaymentRetArrayResult> ListPaymentsAsync(string authToken, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.ListPaymentsAsync(authToken, cancellationToken));
    }

    public async Task<RouteFeeResponseResult> EstimateRouteFeeAsync(string authToken, string paymentrequest, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.EstimateRouteFeeAsync(authToken, paymentrequest, cancellationToken));
    }
}
