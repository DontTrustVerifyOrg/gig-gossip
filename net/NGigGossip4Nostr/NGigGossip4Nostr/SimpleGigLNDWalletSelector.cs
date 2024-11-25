using GigLNDWalletAPIClient;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR.Client;
using NetworkClientToolkit;
using GigGossip;

namespace NGigGossip4Nostr;

public interface IGigLNDWalletSelector
{
    IWalletAPI GetWalletClient(Uri ServiceUri);
}

public class SimpleGigLNDWalletSelector : IGigLNDWalletSelector
{
    ConcurrentDictionary<Uri, IWalletAPI> swaggerClients = new();

    Func<HttpClient> _httpClientFactory;
    IRetryPolicy retryPolicy;

    public SimpleGigLNDWalletSelector(Func<HttpClient> httpClientFactory,IRetryPolicy retryPolicy)
    {
        _httpClientFactory = httpClientFactory;
        this.retryPolicy = retryPolicy;
    }

    public IWalletAPI GetWalletClient(Uri serviceUri)
    {
        return new WalletAPILoggingWrapper(swaggerClients.GetOrAdd(serviceUri,
            (serviceUri) => new WalletAPIRetryWrapper(serviceUri.AbsoluteUri, _httpClientFactory(), retryPolicy)));
    }

}

public class WalletAPILoggingWrapper : IWalletAPI
{
    IWalletAPI API;
    GigDebugLoggerAPIClient.LogWrapper<IWalletAPI> TRACE = GigDebugLoggerAPIClient.FlowLoggerFactory.Trace<IWalletAPI>();

    public WalletAPILoggingWrapper(IWalletAPI api)
    {
        API = api;
    }

    public string BaseUrl => API.BaseUrl;
    public IRetryPolicy RetryPolicy => API.RetryPolicy;

    public async Task<InvoiceRecordResult> AddHodlInvoiceAsync(string authToken, long satoshis, string hash, string memo, long expiry, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(satoshis, hash, memo, expiry);
        try
        {
            return TL.Ret(await API.AddHodlInvoiceAsync(authToken, satoshis, hash, memo, expiry, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<InvoiceRecordResult> AddInvoiceAsync(string authToken, long satoshis, string memo, long expiry, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(satoshis, memo, expiry);
        try
        {
            return TL.Ret(await API.AddInvoiceAsync(authToken, satoshis, memo, expiry, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<InvoiceRecordResult> AddFiatInvoiceAsync(string authToken, long cents, string currency, string memo, long expiry, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(cents,currency, memo, expiry);
        try
        {
            return TL.Ret(await API.AddFiatInvoiceAsync(authToken, cents, currency, memo, expiry, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<InvoiceRecordResult> AddFiatHodlInvoiceAsync(string authToken, long cents, string currency, string hash, string memo, long expiry, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(cents, currency, hash, memo, expiry);
        try
        {
            return TL.Ret(await API.AddFiatHodlInvoiceAsync(authToken, cents, currency, hash, memo, expiry, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }


    public async Task<Result> CancelInvoiceAsync(string authToken, string paymenthash, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(paymenthash);
        try
        {
            return TL.Ret(await API.CancelInvoiceAsync(authToken, paymenthash, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<Result> CloseReserveAsync(string authToken, System.Guid reserveId, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(reserveId);
        try
        {
            return TL.Ret(await API.CloseReserveAsync(authToken, reserveId, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<PaymentRequestRecordResult> DecodeInvoiceAsync(string authToken, string paymentRequest, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(paymentRequest);
        try
        {
            return TL.Ret(await API.DecodeInvoiceAsync(authToken, paymentRequest, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<FeeEstimateRetResult> EstimateFeeAsync(string authToken, string address, long satoshis, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(address, satoshis);
        try
        {
            return TL.Ret(await API.EstimateFeeAsync(authToken, address, satoshis, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<Result> GenerateBlocksAsync(string authToken, int blocknum, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(blocknum);
        try
        {
            return TL.Ret(await API.GenerateBlocksAsync(authToken, blocknum, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<AccountBalanceDetailsResult> GetBalanceAsync(string authToken, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log();
        try
        {
            return TL.Ret(await API.GetBalanceAsync(authToken, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<Int64Result> GetBitcoinWalletBalanceAsync(string authToken, int minConf, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(minConf);
        try
        {
            return TL.Ret(await API.GetBitcoinWalletBalanceAsync(authToken, minConf, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<InvoiceRecordResult> GetInvoiceAsync(string authToken, string paymenthash, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(paymenthash);
        try
        {
            return TL.Ret(await API.GetInvoiceAsync(authToken, paymenthash, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<LndWalletBalanceRetResult> GetLndWalletBalanceAsync(string authToken, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log();
        try
        {
            return TL.Ret(await API.GetLndWalletBalanceAsync(authToken, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<PaymentRecordResult> GetPaymentAsync(string authToken, string paymenthash, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(paymenthash);
        try
        {
            return TL.Ret(await API.GetPaymentAsync(authToken, paymenthash, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GuidResult> GetTokenAsync(string pubkey, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(pubkey);
        try
        {
            return TL.Ret(await API.GetTokenAsync(pubkey, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<StringResult> NewAddressAsync(string authToken, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log();
        try
        {
            return TL.Ret(await API.NewAddressAsync(authToken, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<StringResult> NewBitcoinAddressAsync(string authToken, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log();
        try
        {
            return TL.Ret(await API.NewBitcoinAddressAsync(authToken, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GuidResult> OpenReserveAsync(string authToken, long satoshis, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(satoshis);
        try
        {
            return TL.Ret(await API.OpenReserveAsync(authToken, satoshis, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<PaymentRecordResult> SendPaymentAsync(string authToken, string paymentrequest, int timeout, long feelimit, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(paymentrequest, timeout, feelimit);
        try
        {
            return TL.Ret(await API.SendPaymentAsync(authToken, paymentrequest, timeout, feelimit, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<PaymentRecordResult> CancelInvoiceSendPaymentAsync(string authToken, string paymenthash, string paymentrequest, int timeout, long feelimit, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(paymentrequest, timeout, feelimit);
        try
        {
            return TL.Ret(await API.CancelInvoiceSendPaymentAsync(authToken, paymenthash, paymentrequest, timeout, feelimit, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<Result> SendToAddressAsync(string authToken, string bitcoinAddr, long satoshis, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(bitcoinAddr, satoshis);
        try
        {
            return TL.Ret(await API.SendToAddressAsync(authToken, bitcoinAddr, satoshis, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<RouteFeeRecordResult> EstimateRouteFeeAsync(string authToken, string paymentrequest, int timeout, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(paymentrequest);
        try
        {
            return TL.Ret(await API.EstimateRouteFeeAsync(authToken, paymentrequest, timeout, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<InvoiceRecordArrayResult> ListInvoicesAsync(string authToken, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log();
        try
        {
            return TL.Ret(await API.ListInvoicesAsync(authToken, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<PaymentRecordArrayResult> ListPaymentsAsync(string authToken, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log();
        try
        {
            return TL.Ret(await API.ListPaymentsAsync(authToken, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<Result> SettleInvoiceAsync(string authToken, string preimage, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(preimage);
        try
        {
            return TL.Ret(await API.SettleInvoiceAsync(authToken, preimage, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GuidResult> RegisterPayoutAsync(string authToken, long satoshis, string btcAddress, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(satoshis, btcAddress);
        try
        {
            return TL.Ret(await API.RegisterPayoutAsync(authToken, satoshis, btcAddress, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<Result> TopUpAndMine6BlocksAsync(string authToken, string bitcoinAddr, long satoshis, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(bitcoinAddr, satoshis);
        try
        {
            return TL.Ret(await API.TopUpAndMine6BlocksAsync(authToken, bitcoinAddr, satoshis, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<PayoutRecordArrayResult> ListPayoutsAsync(string authToken, System.Threading.CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log();
        try
        {
            return TL.Ret(await API.ListPayoutsAsync(authToken, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<PayoutRecordResult> GetPayoutAsync(string authToken, System.Guid payoutId, System.Threading.CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(payoutId);
        try
        {
            return TL.Ret(await API.GetPayoutAsync(authToken, payoutId, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<TransactionRecordArrayResult> ListTransactionsAsync(string authToken, System.Threading.CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log();
        try
        {
            return TL.Ret(await API.ListTransactionsAsync(authToken, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public IInvoiceStateUpdatesClient CreateInvoiceStateUpdatesClient()
    {
        return new InvoiceStateUpdatesClientWrapper(API.CreateInvoiceStateUpdatesClient());
    }

    public IPaymentStatusUpdatesClient CreatePaymentStatusUpdatesClient()
    {
        return new PaymentStatusUpdatesClientWrapper(API.CreatePaymentStatusUpdatesClient());
    }

    public ITransactionUpdatesClient CreateTransactionUpdatesClient()
    {
        return new TransactionUpdatesClientWrapper(API.CreateTransactionUpdatesClient());
    }

    public IPayoutStateUpdatesClient CreatePayoutStateUpdatesClient()
    {
        return new PayoutStateUpdatesClientWrapper(API.CreatePayoutStateUpdatesClient());
    }


}

internal class InvoiceStateUpdatesClientWrapper : IInvoiceStateUpdatesClient
{

    IInvoiceStateUpdatesClient API;
    GigDebugLoggerAPIClient.LogWrapper<IInvoiceStateUpdatesClient> TRACE = GigDebugLoggerAPIClient.FlowLoggerFactory.Trace<IInvoiceStateUpdatesClient>();

    public InvoiceStateUpdatesClientWrapper(IInvoiceStateUpdatesClient api) 
    {
        API = api;
    }

    public Uri Uri => API.Uri;

    public async Task ConnectAsync(string authToken, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log();
        try
        {
            await API.ConnectAsync(authToken, cancellationToken);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task MonitorAsync(string authToken, string paymentHash, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(paymentHash);
        try
        {
            await API.MonitorAsync(authToken, paymentHash, cancellationToken);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task StopMonitoringAsync(string authToken, string paymentHash, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(paymentHash);
        try
        {
            await API.StopMonitoringAsync(authToken, paymentHash, cancellationToken);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async IAsyncEnumerable<InvoiceStateChange> StreamAsync(string authToken, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log();
        await foreach (var row in API.StreamAsync(authToken, cancellationToken))
        {
            TL.Iteration(row);
            yield return row;
        }
    }
}

internal class PaymentStatusUpdatesClientWrapper : IPaymentStatusUpdatesClient
{
    IPaymentStatusUpdatesClient API;
    GigDebugLoggerAPIClient.LogWrapper<IPaymentStatusUpdatesClient> TRACE = GigDebugLoggerAPIClient.FlowLoggerFactory.Trace<IPaymentStatusUpdatesClient>();

    public PaymentStatusUpdatesClientWrapper( IPaymentStatusUpdatesClient api) 
    {
        API = api;
   }

    public Uri Uri => API.Uri;

    public async Task ConnectAsync(string authToken, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log();
        try
        {
            await API.ConnectAsync(authToken, cancellationToken);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async IAsyncEnumerable<PaymentStatusChanged> StreamAsync(string authToken, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log();
        await foreach (var row in API.StreamAsync(authToken, cancellationToken))
        {
            TL.Iteration(row);
            yield return row;
        }
   }
}


internal class TransactionUpdatesClientWrapper : ITransactionUpdatesClient
{
    ITransactionUpdatesClient API;
    GigDebugLoggerAPIClient.LogWrapper<ITransactionUpdatesClient> TRACE = GigDebugLoggerAPIClient.FlowLoggerFactory.Trace<ITransactionUpdatesClient>();

    public TransactionUpdatesClientWrapper(ITransactionUpdatesClient api)
    {
        API = api;
    }

    public Uri Uri => API.Uri;

    public async Task ConnectAsync(string authToken, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log();
        try
        {
            await API.ConnectAsync(authToken, cancellationToken);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async IAsyncEnumerable<NewTransactionFound> StreamAsync(string authToken, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log();
        await foreach (var row in API.StreamAsync(authToken, cancellationToken))
        {
            TL.Iteration(row);
            yield return row;
        }
    }
}

internal class PayoutStateUpdatesClientWrapper : IPayoutStateUpdatesClient
{
    IPayoutStateUpdatesClient API;
    GigDebugLoggerAPIClient.LogWrapper<IPayoutStateUpdatesClient> TRACE = GigDebugLoggerAPIClient.FlowLoggerFactory.Trace<IPayoutStateUpdatesClient>();

    public PayoutStateUpdatesClientWrapper(IPayoutStateUpdatesClient api)
    {
        API = api;
    }

    public Uri Uri => API.Uri;

    public async Task ConnectAsync(string authToken, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log();
        try
        {
            await API.ConnectAsync(authToken, cancellationToken);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async IAsyncEnumerable<PayoutStateChanged> StreamAsync(string authToken, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log();
        await foreach (var row in API.StreamAsync(authToken, cancellationToken))
        {
            TL.Iteration(row);
            yield return row;
        }
    }
}