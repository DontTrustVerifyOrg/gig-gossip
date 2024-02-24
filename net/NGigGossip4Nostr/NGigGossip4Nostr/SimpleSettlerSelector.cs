using System;
using CryptoToolkit;
using GigGossipSettlerAPIClient;
using NBitcoin.Secp256k1;
using GigLNDWalletAPIClient;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;
using NBitcoin;
using Newtonsoft.Json.Linq;
using System.Xml.Linq;
using NBitcoin.Protocol;
using System.Net.Sockets;
using System.Threading;

namespace NGigGossip4Nostr;

public interface ISettlerSelector : ICertificationAuthorityAccessor
{
    ISettlerAPI GetSettlerClient(Uri ServiceUri);
}

public class SimpleSettlerSelector : ISettlerSelector
{
    ConcurrentDictionary<Uri, ISettlerAPI> swaggerClients = new();
    ConcurrentDictionary<Guid, bool> revokedCertificates = new();

    Func<HttpClient> _httpClientFactory;

    IFlowLogger flowLogger;

    public SimpleSettlerSelector(Func<HttpClient> httpClientFactory, IFlowLogger flowLogger)
    {
        _httpClientFactory = httpClientFactory;
        this.flowLogger = flowLogger;
    }

    public async Task<ECXOnlyPubKey> GetPubKeyAsync(Uri serviceUri, CancellationToken cancellationToken)
    {
        return SettlerAPIResult.Get<string>(await GetSettlerClient(serviceUri).GetCaPublicKeyAsync(cancellationToken)).AsECXOnlyPubKey();
    }

    public ISettlerAPI GetSettlerClient(Uri serviceUri)
    {
        return new SettlerAPIWrapper(flowLogger, swaggerClients.GetOrAdd(serviceUri, (serviceUri) => new GigGossipSettlerAPIClient.swaggerClient(serviceUri.AbsoluteUri, _httpClientFactory())));
    }

    public async Task<bool> IsRevokedAsync(Uri serviceUri, Guid id, CancellationToken cancellationToken)
    {
        return await revokedCertificates.GetOrAddAsync(id, async (id) => SettlerAPIResult.Get<bool>(await GetSettlerClient(serviceUri).IsCertificateRevokedAsync(id.ToString(), cancellationToken)));
    }
}


public class SettlerAPIWrapper : LogWrapper<ISettlerAPI>, ISettlerAPI
{
    public SettlerAPIWrapper(IFlowLogger flowLogger, ISettlerAPI api) : base(flowLogger, api)
    {
    }

    public string BaseUrl => api.BaseUrl;

    public async Task<GigGossipSettlerAPIClient.StringResult> GetCaPublicKeyAsync(CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__);
            return await TraceOutAsync(g__, m__,
                await api.GetCaPublicKeyAsync(cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<BooleanResult> IsCertificateRevokedAsync(string certid, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, certid);
            return await TraceOutAsync(g__, m__,
                await api.IsCertificateRevokedAsync(certid, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.GuidResult> GetTokenAsync(string pubkey, CancellationToken cancellationToken)
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

    public async Task<StringArrayResult> AddressAutocompleteAsync(string authToken, string query, string country, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, query, country);
            return await TraceOutAsync(g__, m__,
                await api.AddressAutocompleteAsync(authToken, query, country, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<GeolocationRetResult> AddressGeocodeAsync(string authToken, string address, string country, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, address, country);
            return await TraceOutAsync(g__, m__,
                await api.AddressGeocodeAsync(authToken, address, country, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.StringResult> LocationGeocodeAsync(string authToken, double lat, double lon, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, lat, lon);
            return await TraceOutAsync(g__, m__,
                await api.LocationGeocodeAsync(authToken, lat, lon, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.Result> GiveUserPropertyAsync(string authToken, string pubkey, string name, string value, string secret, long validHours, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, pubkey, name, value, secret, validHours);
            return await TraceOutAsync(g__, m__,
                await api.GiveUserPropertyAsync(authToken, pubkey, name, value, secret, validHours, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.Result> LogEventAsync(string authToken, string eventType, FileParameter message, FileParameter exception, CancellationToken cancellationToken)
    {
        return await api.LogEventAsync(authToken, eventType, message, exception, cancellationToken);
    }

    public async Task<SystemLogEntryListResult> GetLogEventsAsync(string authToken, string pubkey, long frmtmst, long totmst, CancellationToken cancellationToken)
    {
        return await api.GetLogEventsAsync(authToken, pubkey, frmtmst, totmst, cancellationToken);
    }

    public async Task<GigGossipSettlerAPIClient.Result> GiveUserFileAsync(string authToken, string pubkey, string name, long validHours, FileParameter value, FileParameter secret, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, pubkey, name, validHours, value.ToBytes(), secret.ToBytes());
            return await TraceOutAsync(g__, m__,
                await api.GiveUserFileAsync(authToken, pubkey, name, validHours, value, secret, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.Result> SaveUserTracePropertyAsync(string authToken, string pubkey, string name, string value, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, pubkey, name, value);
            return await TraceOutAsync(g__, m__,
                await api.SaveUserTracePropertyAsync(authToken, pubkey, name, value, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.Result> VerifyChannelAsync(string authToken, string pubkey, string name, string method, string value, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, pubkey, name, method, value);
            return await TraceOutAsync(g__, m__,
                await api.VerifyChannelAsync(authToken, pubkey, name, method, value, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<Int32Result> SubmitChannelSecretAsync(string authToken, string pubkey, string name, string method, string value, string secret, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, pubkey, name, method, value, secret);
            return await TraceOutAsync(g__, m__,
                await api.SubmitChannelSecretAsync(authToken, pubkey, name, method, value, secret, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<BooleanResult> IsChannelVerifiedAsync(string authToken, string pubkey, string name, string value, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, pubkey, name, value);
            return await TraceOutAsync(g__, m__,
                await api.IsChannelVerifiedAsync(authToken, pubkey, name, value, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.Result> RevokeuserpropertyAsync(string authToken, string pubkey, string name, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, pubkey, name);
            return await TraceOutAsync(g__, m__,
                await api.RevokeuserpropertyAsync(authToken, pubkey, name, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.StringResult> GenerateReplyPaymentPreimageAsync(string authToken, string gigId, string repliperPubKey, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, gigId, repliperPubKey);
            return await TraceOutAsync(g__, m__,
                await api.GenerateReplyPaymentPreimageAsync(authToken, gigId, repliperPubKey, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.StringResult> GenerateRelatedPreimageAsync(string authToken, string paymentHash, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, paymentHash);
            return await TraceOutAsync(g__, m__,
                await api.GenerateRelatedPreimageAsync(authToken, paymentHash, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<BooleanResult> ValidateRelatedPaymentHashesAsync(string authToken, string paymentHash1, string paymentHash2, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, paymentHash1, paymentHash2);
            return await TraceOutAsync(g__, m__,
                await api.ValidateRelatedPaymentHashesAsync(authToken, paymentHash1, paymentHash2, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.StringResult> RevealPreimageAsync(string authToken, string paymentHash, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, paymentHash);
            return await TraceOutAsync(g__, m__,
                await api.RevealPreimageAsync(authToken, paymentHash, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.StringResult> GetGigStatusAsync(string authToken, string signedRequestPayloadId, string repliperCertificateId, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, signedRequestPayloadId, repliperCertificateId);
            return await TraceOutAsync(g__, m__,
                await api.GetGigStatusAsync(authToken, signedRequestPayloadId, repliperCertificateId, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.StringResult> GenerateRequestPayloadAsync(string authToken, string properties, FileParameter serialisedTopic, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, properties, serialisedTopic.ToBytes());
            return await TraceOutAsync(g__, m__,
                await api.GenerateRequestPayloadAsync(authToken, properties, serialisedTopic, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.StringResult> GenerateSettlementTrustAsync(string authToken, string properties, string replyinvoice, FileParameter message, FileParameter signedRequestPayloadSerialized, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, properties, replyinvoice, message.ToBytes(), signedRequestPayloadSerialized.ToBytes());
            return await TraceOutAsync(g__, m__,
                await api.GenerateSettlementTrustAsync(authToken, properties, replyinvoice, message, signedRequestPayloadSerialized, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.StringResult> EncryptObjectForCertificateIdAsync(string certificateId, FileParameter objectSerialized, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, certificateId, objectSerialized.ToBytes());
            return await TraceOutAsync(g__, m__,
                await api.EncryptObjectForCertificateIdAsync(certificateId, objectSerialized, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.Result> ManageDisputeAsync(string authToken, string gigId, string repliperCertificateId, bool open, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, gigId, repliperCertificateId, open);
            return await TraceOutAsync(g__, m__,
                await api.ManageDisputeAsync(authToken, gigId, repliperCertificateId, open, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.Result> CancelGigAsync(string authToken, string gigId, string repliperCertificateId, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, gigId, repliperCertificateId);
            return await TraceOutAsync(g__, m__,
                await api.CancelGigAsync(authToken, gigId, repliperCertificateId, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public IGigStatusClient CreateGigStatusClient()
    {
        return new GigStatusClientWrapper(this.flowLogger, api.CreateGigStatusClient());
    }

    public IPreimageRevealClient CreatePreimageRevealClient()
    {
        return new PreimageRevealClientWrapper(this.flowLogger, api.CreatePreimageRevealClient());
    }
}

internal class GigStatusClientWrapper : LogWrapper<IGigStatusClient>, IGigStatusClient
{
    public GigStatusClientWrapper(IFlowLogger flowLogger, IGigStatusClient api) : base(flowLogger,api)
    {
    }

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

    public async Task DisposeAsync()
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__);
            await api.DisposeAsync();
            await TraceVoidAsync(g__, m__);
        }
        catch (Exception ex)
        {
            await TraceExcAsync(g__, m__, ex);
            throw;
        }
    }

    public async Task MonitorAsync(string authToken, Guid gigId, Guid replierCertificateId, CancellationToken cancellationToken)
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__, authToken, gigId, replierCertificateId);
            await api.MonitorAsync(authToken, gigId, replierCertificateId, cancellationToken);
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

internal class PreimageRevealClientWrapper : LogWrapper<IPreimageRevealClient>, IPreimageRevealClient
{
    public PreimageRevealClientWrapper(IFlowLogger flowLogger, IPreimageRevealClient api) : base(flowLogger, api)
    {
    }

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

    public async Task DisposeAsync()
    {
        Guid? g__ = null; string? m__ = null; if (flowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = MetNam(); }
        try
        {
            await TraceInAsync(g__, m__);
            await api.DisposeAsync();
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