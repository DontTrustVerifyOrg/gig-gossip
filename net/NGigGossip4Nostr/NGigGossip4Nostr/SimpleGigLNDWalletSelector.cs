using System;
using CryptoToolkit;
using GigLNDWalletAPIClient;
using NBitcoin.Secp256k1;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Dynamic;
using System.Reflection;
using System.Runtime.CompilerServices;
using static NBitcoin.Scripting.OutputDescriptor.TapTree;
using System.Threading;
using Microsoft.AspNetCore.SignalR.Client;

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

    public async Task<InvoiceRetResult> AddHodlInvoiceAsync(string authToken, long satoshis, string hash, string memo, long expiry, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(authToken, satoshis, hash, memo, expiry);
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

    public async Task<InvoiceRetResult> AddInvoiceAsync(string authToken, long satoshis, string memo, long expiry, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(authToken, satoshis, memo, expiry);
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

    public async Task<Result> CancelInvoiceAsync(string authToken, string paymenthash, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(authToken, paymenthash);
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
        using var TL = TRACE.Log().Args(authToken, reserveId);
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

    public async Task<PayReqRetResult> DecodeInvoiceAsync(string authToken, string paymentRequest, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(authToken, paymentRequest);
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
        using var TL = TRACE.Log().Args(authToken, address, satoshis);
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
        using var TL = TRACE.Log().Args(authToken, blocknum);
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

    public async Task<Int64Result> GetBalanceAsync(string authToken, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(authToken);
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

    public async Task<AccountBallanceDetailsResult> GetBalanceDetailsAsync(string authToken, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(authToken);
        try
        {
            return TL.Ret(await API.GetBalanceDetailsAsync(authToken, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<Int64Result> GetBitcoinWalletBallanceAsync(string authToken, int minConf, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(authToken, minConf);
        try
        {
            return TL.Ret(await API.GetBitcoinWalletBallanceAsync(authToken, minConf, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<StringResult> GetInvoiceStateAsync(string authToken, string paymenthash, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(authToken, paymenthash);
        try
        {
            return TL.Ret(await API.GetInvoiceStateAsync(authToken, paymenthash, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<LndWalletBallanceRetResult> GetLndWalletBallanceAsync(string authToken, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(authToken);
        try
        {
            return TL.Ret(await API.GetLndWalletBallanceAsync(authToken, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<StringResult> GetPaymentStatusAsync(string authToken, string paymenthash, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(authToken, paymenthash);
        try
        {
            return TL.Ret(await API.GetPaymentStatusAsync(authToken, paymenthash, cancellationToken));
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
        using var TL = TRACE.Log().Args(authToken);
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
        using var TL = TRACE.Log().Args(authToken);
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
        using var TL = TRACE.Log().Args(authToken, satoshis);
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

    public async Task<Result> SendPaymentAsync(string authToken, string paymentrequest, int timeout, long feelimit, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(authToken, paymentrequest, timeout, feelimit);
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

    public async Task<Result> SendToAddressAsync(string authToken, string bitcoinAddr, long satoshis, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(authToken, bitcoinAddr, satoshis);
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

    public async Task<RouteFeeResponseResult> EstimateRouteFeeAsync(string authToken, string paymentrequest, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(authToken, paymentrequest);
        try
        {
            return TL.Ret(await API.EstimateRouteFeeAsync(authToken, paymentrequest, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<InvoiceRetArrayResult> ListInvoicesAsync(string authToken, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(authToken);
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

    public async Task<PaymentRetArrayResult> ListPaymentsAsync(string authToken, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(authToken);
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
        using var TL = TRACE.Log().Args(authToken, preimage);
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

    public async Task<GuidResult> RegisterPayoutAsync(string authToken, long satoshis, string btcAddress, long txfee, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(authToken, satoshis, btcAddress, txfee);
        try
        {
            return TL.Ret(await API.RegisterPayoutAsync(authToken, satoshis, btcAddress, txfee, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<Result> TopUpAndMine6BlocksAsync(string authToken, string bitcoinAddr, long satoshis, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(authToken, bitcoinAddr, satoshis);
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

    public IInvoiceStateUpdatesClient CreateInvoiceStateUpdatesClient()
    {
        return new InvoiceStateUpdatesClientWrapper(API.CreateInvoiceStateUpdatesClient());
    }

    public IPaymentStatusUpdatesClient CreatePaymentStatusUpdatesClient()
    {
        return new PaymentStatusUpdatesClientWrapper(API.CreatePaymentStatusUpdatesClient());
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
        using var TL = TRACE.Log().Args(authToken);
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
        using var TL = TRACE.Log().Args(authToken,paymentHash);
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
        using var TL = TRACE.Log().Args(authToken, paymentHash);
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

    public async IAsyncEnumerable<string> StreamAsync(string authToken, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(authToken);
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
        using var TL = TRACE.Log().Args(authToken);
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
        using var TL = TRACE.Log().Args(authToken,paymentHash);
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
        using var TL = TRACE.Log().Args(authToken, paymentHash);
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

    public async IAsyncEnumerable<string> StreamAsync(string authToken, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(authToken);
        await foreach (var row in API.StreamAsync(authToken, cancellationToken))
        {
            TL.Iteration(row);
            yield return row;
        }
   }
}