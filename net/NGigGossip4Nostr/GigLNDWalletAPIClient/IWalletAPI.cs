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
    Task<Int64Result> GetBitcoinWalletBalanceAsync(string authToken, int minConf, System.Threading.CancellationToken cancellationToken);
    Task<LndWalletBalanceRetResult> GetLndWalletBalanceAsync(string authToken, System.Threading.CancellationToken cancellationToken);
    Task<GuidResult> OpenReserveAsync(string authToken, long satoshis, System.Threading.CancellationToken cancellationToken);
    Task<Result> CloseReserveAsync(string authToken, System.Guid reserveId, System.Threading.CancellationToken cancellationToken);
    Task<FeeEstimateRetResult> EstimateFeeAsync(string authToken, string address, long satoshis, System.Threading.CancellationToken cancellationToken);
    Task<AccountBalanceDetailsResult> GetBalanceAsync(string authToken, System.Threading.CancellationToken cancellationToken);
    Task<StringResult> NewAddressAsync(string authToken, System.Threading.CancellationToken cancellationToken);
    Task<GuidResult> RegisterPayoutAsync(string authToken, long satoshis, string btcAddress, System.Threading.CancellationToken cancellationToken);
    Task<InvoiceRecordResult> AddInvoiceAsync(string authToken, long satoshis, string memo, long expiry, System.Threading.CancellationToken cancellationToken);
    Task<InvoiceRecordResult> AddHodlInvoiceAsync(string authToken, long satoshis, string hash, string memo, long expiry, System.Threading.CancellationToken cancellationToken);
    Task<PaymentRequestRecordResult> DecodeInvoiceAsync(string authToken, string paymentRequest, System.Threading.CancellationToken cancellationToken);
    Task<RouteFeeRecordResult> EstimateRouteFeeAsync(string authToken, string paymentrequest, int timeout, CancellationToken cancellationToken);
    Task<PaymentRecordResult> SendPaymentAsync(string authToken, string paymentrequest, int timeout, long feelimit, System.Threading.CancellationToken cancellationToken);
    Task<Result> SettleInvoiceAsync(string authToken, string preimage, System.Threading.CancellationToken cancellationToken);
    Task<Result> CancelInvoiceAsync(string authToken, string paymenthash, System.Threading.CancellationToken cancellationToken);
    Task<PaymentRecordResult> CancelInvoiceSendPaymentAsync(string authToken, string paymenthash, string paymentrequest, int timeout, long feelimit, System.Threading.CancellationToken cancellationToken);
    Task<InvoiceRecordResult> GetInvoiceAsync(string authToken, string paymenthash, System.Threading.CancellationToken cancellationToken);
    Task<PaymentRecordResult> GetPaymentAsync(string authToken, string paymenthash, System.Threading.CancellationToken cancellationToken);
    Task<InvoiceRecordArrayResult> ListInvoicesAsync(string authToken, System.Threading.CancellationToken cancellationToken);
    Task<PaymentRecordArrayResult> ListPaymentsAsync(string authToken, System.Threading.CancellationToken cancellationToken);
    Task<PayoutRecordArrayResult> ListPayoutsAsync(string authToken, System.Threading.CancellationToken cancellationToken);
    Task<PayoutRecordResult> GetPayoutAsync(string authToken, System.Guid payoutId, System.Threading.CancellationToken cancellationToken);
    Task<TransactionRecordArrayResult> ListTransactionsAsync(string authToken, System.Threading.CancellationToken cancellationToken);

    IInvoiceStateUpdatesClient CreateInvoiceStateUpdatesClient();
    IPaymentStatusUpdatesClient CreatePaymentStatusUpdatesClient();
    ITransactionUpdatesClient CreateTransactionUpdatesClient();
    IPayoutStateUpdatesClient CreatePayoutStateUpdatesClient();
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

    public ITransactionUpdatesClient CreateTransactionUpdatesClient()
    {
        return new TransactionUpdatesClient(this);
    }

    public IPayoutStateUpdatesClient CreatePayoutStateUpdatesClient()
    {
        return new PayoutStateUpdatesClient(this);
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

    public ITransactionUpdatesClient CreateTransactionUpdatesClient()
    {
        return api.CreateTransactionUpdatesClient();
    }

    public IPayoutStateUpdatesClient CreatePayoutStateUpdatesClient()
    {
        return api.CreatePayoutStateUpdatesClient();
    }

    public async Task<InvoiceRecordResult> AddHodlInvoiceAsync(string authToken, long satoshis, string hash, string memo, long expiry, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.AddHodlInvoiceAsync(authToken, satoshis, hash, memo, expiry, cancellationToken));
    }

    public async Task<InvoiceRecordResult> AddInvoiceAsync(string authToken, long satoshis, string memo, long expiry, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.AddInvoiceAsync(authToken, satoshis, memo, expiry, cancellationToken));
    }

    public async Task<Result> CancelInvoiceAsync(string authToken, string paymenthash, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.CancelInvoiceAsync(authToken, paymenthash, cancellationToken));
    }

    public async Task<Result> CloseReserveAsync(string authToken, System.Guid reserveId, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.CloseReserveAsync(authToken, reserveId, cancellationToken));
    }

    public async Task<PaymentRequestRecordResult> DecodeInvoiceAsync(string authToken, string paymentRequest, CancellationToken cancellationToken)
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

    public async Task<AccountBalanceDetailsResult> GetBalanceAsync(string authToken, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GetBalanceAsync(authToken, cancellationToken));
    }


    public async Task<Int64Result> GetBitcoinWalletBalanceAsync(string authToken, int minConf, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GetBitcoinWalletBalanceAsync(authToken, minConf, cancellationToken));
    }

    public async Task<InvoiceRecordResult> GetInvoiceAsync(string authToken, string paymenthash, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GetInvoiceAsync(authToken, paymenthash, cancellationToken));
    }

    public async Task<LndWalletBalanceRetResult> GetLndWalletBalanceAsync(string authToken, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GetLndWalletBalanceAsync(authToken, cancellationToken));
    }

    public async Task<PaymentRecordResult> GetPaymentAsync(string authToken, string paymenthash, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GetPaymentAsync(authToken, paymenthash, cancellationToken));
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

    public async Task<GuidResult> RegisterPayoutAsync(string authToken, long satoshis, string btcAddress, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.RegisterPayoutAsync(authToken, satoshis, btcAddress, cancellationToken));
    }

    public async Task<PaymentRecordResult> SendPaymentAsync(string authToken, string paymentrequest, int timeout, long feelimit, CancellationToken cancellationToken)
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

    public async Task<InvoiceRecordArrayResult> ListInvoicesAsync(string authToken, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.ListInvoicesAsync(authToken, cancellationToken));
    }

    public async Task<PaymentRecordArrayResult> ListPaymentsAsync(string authToken, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.ListPaymentsAsync(authToken, cancellationToken));
    }

    public async Task<RouteFeeRecordResult> EstimateRouteFeeAsync(string authToken, string paymentrequest, int timeout, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.EstimateRouteFeeAsync(authToken, paymentrequest,timeout, cancellationToken));
    }

    public async Task<PayoutRecordArrayResult> ListPayoutsAsync(string authToken, System.Threading.CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.ListPayoutsAsync(authToken, cancellationToken));
    }

    public async Task<PayoutRecordResult> GetPayoutAsync(string authToken, System.Guid payoutId, System.Threading.CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GetPayoutAsync(authToken, payoutId, cancellationToken));
    }

    public async Task<TransactionRecordArrayResult> ListTransactionsAsync(string authToken, System.Threading.CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.ListTransactionsAsync(authToken, cancellationToken));
    }

    public async Task<PaymentRecordResult> CancelInvoiceSendPaymentAsync(string authToken, string paymenthash, string paymentrequest, int timeout, long feelimit, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.CancelInvoiceSendPaymentAsync(authToken, paymenthash, paymentrequest, timeout, feelimit, cancellationToken));
    }
}
