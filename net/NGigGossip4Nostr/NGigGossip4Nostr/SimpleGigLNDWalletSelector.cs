using System;
using CryptoToolkit;
using GigLNDWalletAPIClient;
using NBitcoin.Secp256k1;
using GigLNDWalletAPIClient;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Dynamic;
using System.Reflection;
using System.Runtime.CompilerServices;
using static NBitcoin.Scripting.OutputDescriptor.TapTree;
using System.Threading;

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

    public SimpleGigLNDWalletSelector(Func<HttpClient> httpClientFactory, IFlowLogger flowLogger)
    {
        _httpClientFactory = httpClientFactory;
        this.flowLogger = flowLogger;
    }

    public IWalletAPI GetWalletClient(Uri serviceUri)
    {
        return new WalletAPIWrapper(flowLogger, swaggerClients.GetOrAdd(serviceUri, (serviceUri) => new GigLNDWalletAPIClient.swaggerClient(serviceUri.AbsoluteUri, _httpClientFactory())));
    }

}

public class WalletAPIWrapper : IWalletAPI
{
    IWalletAPI api;
    IFlowLogger flowLogger;

    public WalletAPIWrapper(IFlowLogger flowLogger, IWalletAPI api)
    {
        this.api = api;
        this.flowLogger = flowLogger;
    }

    public string MetNam([CallerMemberName] string memberName = "")
    {
        return memberName;
    }

    public async Task TraceInAsync(Guid? guid, string? memberName, params dynamic[] objects)
    {
        if (flowLogger.Enabled)
            await flowLogger.TraceInformationAsync(Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                kind = "call",
                id = guid,
                method = memberName,
                type = api.GetType().FullName,
                args = objects
            }));
    }

    public async Task<T> TraceOutAsync<T>(Guid? guid, string? memberName, T r)
    {
        if (flowLogger.Enabled)
            await flowLogger.TraceInformationAsync(Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                kind = "return",
                id = guid,
                method = memberName,
                type = api.GetType().FullName,
                retval = r
            }));
        return r;
    }

    public async Task TraceExcAsync(Guid? guid, string? memberName, Exception ex)
    {
        if (flowLogger.Enabled)
            await flowLogger.TraceExceptionAsync(ex, Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                kind = "exception",
                id = guid,
                method = memberName,
                type = api.GetType().FullName,
                exception = ex.Message,
            }));
    }

    public string BaseUrl => api.BaseUrl;

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


}
