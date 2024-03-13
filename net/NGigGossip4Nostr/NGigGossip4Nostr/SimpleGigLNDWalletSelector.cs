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
    IFlowLogger flowLogger;
    IRetryPolicy retryPolicy;

    public SimpleGigLNDWalletSelector(Func<HttpClient> httpClientFactory, IFlowLogger flowLogger, IRetryPolicy retryPolicy)
    {
        _httpClientFactory = httpClientFactory;
        this.flowLogger = flowLogger;
        this.retryPolicy = retryPolicy;
    }

    public IWalletAPI GetWalletClient(Uri serviceUri)
    {
        return new WalletAPILoggingWrapper(flowLogger, swaggerClients.GetOrAdd(serviceUri,
            (serviceUri) => new WalletAPIRetryWrapper(serviceUri.AbsoluteUri, _httpClientFactory(), retryPolicy)));
    }

}


public class WalletAPILoggingWrapper : LogWrapper<IWalletAPI>, IWalletAPI
{
    public WalletAPILoggingWrapper(IFlowLogger flowLogger, IWalletAPI api) : base(flowLogger, api)
    {
    }

    public string BaseUrl => api.BaseUrl;
    public IRetryPolicy RetryPolicy => api.RetryPolicy;

    public async Task<InvoiceRetResult> AddHodlInvoiceAsync(string authToken, long satoshis, string hash, string memo, long expiry, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, satoshis, hash, memo, expiry);
            return await TraceOutAsync(g__, m__,
                await api.AddHodlInvoiceAsync(authToken, satoshis, hash, memo, expiry, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<InvoiceRetResult> AddInvoiceAsync(string authToken, long satoshis, string memo, long expiry, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, satoshis, memo, expiry);
            return await TraceOutAsync(g__, m__,
                await api.AddInvoiceAsync(authToken, satoshis, memo, expiry, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<Result> CancelInvoiceAsync(string authToken, string paymenthash, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, paymenthash);
            return await TraceOutAsync(g__, m__,
                await api.CancelInvoiceAsync(authToken, paymenthash, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<Result> CloseReserveAsync(string authToken, string reserveId, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, reserveId);
            return await TraceOutAsync(g__, m__,
                await api.CloseReserveAsync(authToken, reserveId, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<PayReqResult> DecodeInvoiceAsync(string authToken, string paymentRequest, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, paymentRequest);
            return await TraceOutAsync(g__, m__,
                await api.DecodeInvoiceAsync(authToken, paymentRequest, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<FeeEstimateRetResult> EstimateFeeAsync(string authToken, string address, long satoshis, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, address, satoshis);
            return await TraceOutAsync(g__, m__,
                await api.EstimateFeeAsync(authToken, address, satoshis, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<Result> GenerateBlocksAsync(string authToken, int blocknum, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, blocknum);
            return await TraceOutAsync(g__, m__,
                await api.GenerateBlocksAsync(authToken, blocknum, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<Int64Result> GetBalanceAsync(string authToken, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken);
            return await TraceOutAsync(g__, m__,
                await api.GetBalanceAsync(authToken, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<AccountBallanceDetailsResult> GetBalanceDetailsAsync(string authToken, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken);
            return await TraceOutAsync(g__, m__,
                await api.GetBalanceDetailsAsync(authToken, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<Int64Result> GetBitcoinWalletBallanceAsync(string authToken, int minConf, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, minConf);
            return await TraceOutAsync(g__, m__,
                await api.GetBitcoinWalletBallanceAsync(authToken, minConf, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<StringResult> GetInvoiceStateAsync(string authToken, string paymenthash, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, paymenthash);
            return await TraceOutAsync(g__, m__,
                await api.GetInvoiceStateAsync(authToken, paymenthash, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<LndWalletBallanceRetResult> GetLndWalletBallanceAsync(string authToken, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken);
            return await TraceOutAsync(g__, m__,
                await api.GetLndWalletBallanceAsync(authToken, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<StringResult> GetPaymentStatusAsync(string authToken, string paymenthash, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, paymenthash);
            return await TraceOutAsync(g__, m__,
                await api.GetPaymentStatusAsync(authToken, paymenthash, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<GuidResult> GetTokenAsync(string pubkey, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, pubkey);
            return await TraceOutAsync(g__, m__,
                await api.GetTokenAsync(pubkey, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<StringResult> NewAddressAsync(string authToken, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken);
            return await TraceOutAsync(g__, m__,
                await api.NewAddressAsync(authToken, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<StringResult> NewBitcoinAddressAsync(string authToken, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken);
            return await TraceOutAsync(g__, m__,
                await api.NewBitcoinAddressAsync(authToken, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<GuidResult> OpenReserveAsync(string authToken, long satoshis, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, satoshis);
            return await TraceOutAsync(g__, m__,
                await api.OpenReserveAsync(authToken, satoshis, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<GuidResult> RegisterPayoutAsync(string authToken, long satoshis, string btcAddress, long txfee, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, satoshis, btcAddress, txfee);
            return await TraceOutAsync(g__, m__,
                await api.RegisterPayoutAsync(authToken, satoshis, btcAddress, txfee, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<Result> SendPaymentAsync(string authToken, string paymentrequest, int timeout, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, paymentrequest, timeout);
            return await TraceOutAsync(g__, m__,
                await api.SendPaymentAsync(authToken, paymentrequest, timeout, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<Result> SendToAddressAsync(string authToken, string bitcoinAddr, long satoshis, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, bitcoinAddr, satoshis);
            return await TraceOutAsync(g__, m__,
                await api.SendToAddressAsync(authToken, bitcoinAddr, satoshis, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<Result> SettleInvoiceAsync(string authToken, string preimage, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, preimage);
            return await TraceOutAsync(g__, m__,
                await api.SettleInvoiceAsync(authToken, preimage, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<Result> TopUpAndMine6BlocksAsync(string authToken, string bitcoinAddr, long satoshis, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, bitcoinAddr, satoshis);
            return await TraceOutAsync(g__, m__,
                await api.TopUpAndMine6BlocksAsync(authToken, bitcoinAddr, satoshis, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public IInvoiceStateUpdatesClient CreateInvoiceStateUpdatesClient()
    {
        return new InvoiceStateUpdatesClientWrapper(flowLogger, api.CreateInvoiceStateUpdatesClient());
    }

    public IPaymentStatusUpdatesClient CreatePaymentStatusUpdatesClient()
    {
        return new PaymentStatusUpdatesClientWrapper(flowLogger,api.CreatePaymentStatusUpdatesClient());
    }
}

internal class InvoiceStateUpdatesClientWrapper : LogWrapper<IInvoiceStateUpdatesClient>, IInvoiceStateUpdatesClient
{
    public InvoiceStateUpdatesClientWrapper(IFlowLogger flowLogger, IInvoiceStateUpdatesClient api) : base(flowLogger, api)
    {
    }

    public Uri Uri => api.Uri;

    public async Task ConnectAsync(string authToken, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken);
            await api.ConnectAsync(authToken, cancellationToken);
            await TraceVoidAsync(g__, m__);
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task MonitorAsync(string authToken, string paymentHash, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, paymentHash);
            await api.MonitorAsync(authToken, paymentHash, cancellationToken);
            await TraceVoidAsync(g__, m__);
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task StopMonitoringAsync(string authToken, string paymentHash, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, paymentHash);
            await api.StopMonitoringAsync(authToken, paymentHash, cancellationToken);
            await TraceVoidAsync(g__, m__);
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(string authToken, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (flowLogger.Enabled)
        {
            Guid? g__ = Guid.NewGuid(); string? m__ = MetNam();
            await TraceInAsync(g__, m__, authToken);
            await foreach (var row in api.StreamAsync(authToken, cancellationToken))
            {
                await TraceIterAsync(g__, m__, row);
                yield return row;
            }
            await TraceVoidAsync(g__, m__);
        }
        else
        {
            await foreach (var row in api.StreamAsync(authToken, cancellationToken))
                yield return row;
        }
    }
}

internal class PaymentStatusUpdatesClientWrapper : LogWrapper<IPaymentStatusUpdatesClient>, IPaymentStatusUpdatesClient
{
    public PaymentStatusUpdatesClientWrapper(IFlowLogger flowLogger, IPaymentStatusUpdatesClient api) : base(flowLogger, api)
    {
    }

    public Uri Uri => api.Uri;

    public async Task ConnectAsync(string authToken, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken);
            await api.ConnectAsync(authToken, cancellationToken);
            await TraceVoidAsync(g__, m__);
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task MonitorAsync(string authToken, string paymentHash, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, paymentHash);
            await api.MonitorAsync(authToken, paymentHash, cancellationToken);
            await TraceVoidAsync(g__, m__);
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task StopMonitoringAsync(string authToken, string paymentHash, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, paymentHash);
            await api.StopMonitoringAsync(authToken, paymentHash, cancellationToken);
            await TraceVoidAsync(g__, m__);
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(string authToken, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (flowLogger.Enabled)
        {
            Guid? g__ = Guid.NewGuid(); string? m__ = MetNam();
            await TraceInAsync(g__, m__, authToken);
            await foreach (var row in api.StreamAsync(authToken, cancellationToken))
            {
                await TraceIterAsync(g__, m__, row);
                yield return row;
            }
            await TraceVoidAsync(g__, m__);
        }
        else
        {
            await foreach (var row in api.StreamAsync(authToken, cancellationToken))
                yield return row;
        }
    }
}