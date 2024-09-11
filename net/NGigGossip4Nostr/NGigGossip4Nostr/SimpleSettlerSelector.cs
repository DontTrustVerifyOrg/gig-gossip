using System;
using GigGossipSettlerAPIClient;
using NBitcoin.Secp256k1;
using GigLNDWalletAPIClient;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using NBitcoin;
using Newtonsoft.Json.Linq;
using System.Xml.Linq;
using NBitcoin.Protocol;
using System.Net.Sockets;
using System.Threading;
using Microsoft.AspNetCore.SignalR.Client;
using NetworkClientToolkit;
using GigGossip;

namespace NGigGossip4Nostr;

public interface ISettlerSelector : ICertificationAuthorityAccessor
{
    ISettlerAPI GetSettlerClient(Uri ServiceUri);
    void RemoveSettlerClient(Uri ServiceUri);
}

public class SimpleSettlerSelector : ISettlerSelector
{
    ConcurrentDictionary<Uri, ISettlerAPI> swaggerClients = new();
    ConcurrentDictionary<Guid, bool> revokedCertificates = new();

    Func<HttpClient> _httpClientFactory;

    IRetryPolicy retryPolicy;

    public SimpleSettlerSelector(Func<HttpClient> httpClientFactory, IRetryPolicy retryPolicy)
    {
        _httpClientFactory = httpClientFactory;
        this.retryPolicy = retryPolicy;
    }

    public async Task<ECXOnlyPubKey> GetPubKeyAsync(Uri serviceUri, CancellationToken cancellationToken)
    {
        return SettlerAPIResult.Get<string>(await GetSettlerClient(serviceUri).GetCaPublicKeyAsync(cancellationToken)).AsECXOnlyPubKey();
    }

    public ISettlerAPI GetSettlerClient(Uri serviceUri)
    {
        return new SettlerAPIWrapper(swaggerClients.GetOrAdd(serviceUri, (serviceUri) => new SettlerAPIRetryWrapper(serviceUri.AbsoluteUri, _httpClientFactory(), retryPolicy)));
    }

    public async Task<bool> IsRevokedAsync(Uri serviceUri, Guid id, CancellationToken cancellationToken)
    {
        return await revokedCertificates.GetOrAddAsync(id, async (id) => SettlerAPIResult.Get<bool>(await GetSettlerClient(serviceUri).IsCertificateRevokedAsync(id, cancellationToken)));
    }

    public void RemoveSettlerClient(Uri ServiceUri)
    {
        swaggerClients.TryRemove(ServiceUri, out _);
    }
}


public class SettlerAPIWrapper : ISettlerAPI
{
    ISettlerAPI API;
    GigDebugLoggerAPIClient.LogWrapper<ISettlerAPI> TRACE = GigDebugLoggerAPIClient.FlowLoggerFactory.Trace<ISettlerAPI>();

    public SettlerAPIWrapper(ISettlerAPI api)
    {
        API = api;
    }

    public string BaseUrl => API.BaseUrl;
    public IRetryPolicy RetryPolicy => API.RetryPolicy;
 
    public async Task<GigGossipSettlerAPIClient.StringResult> GetCaPublicKeyAsync(CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log();
        try
        {
            return TL.Ret(await API.GetCaPublicKeyAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<BooleanResult> IsCertificateRevokedAsync(System.Guid certid, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(certid);
        try
        {
            return TL.Ret(await API.IsCertificateRevokedAsync(certid, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.GuidResult> GetTokenAsync(string pubkey, CancellationToken cancellationToken)
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

    public async Task<StringArrayResult> AddressAutocompleteAsync(string authToken, string query, string country, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(query, country);
        try
        {
            return TL.Ret(await API.AddressAutocompleteAsync(authToken, query, country, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GeolocationRetResult> AddressGeocodeAsync(string authToken, string address, string country, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(address, country);
        try
        {
            return TL.Ret(await API.AddressGeocodeAsync(authToken, address, country, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.StringResult> LocationGeocodeAsync(string authToken, double lat, double lon, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(lat, lon);
        try
        {
            return TL.Ret(await API.LocationGeocodeAsync(authToken, lat, lon, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<RouteRetResult> GetRouteAsync(string authToken, double fromLat, double fromLon, double toLat, double toLon, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(fromLat, fromLon, toLat, toLon);
        try
        {
            return TL.Ret(await API.GetRouteAsync(authToken, fromLat, fromLon, toLat, toLon, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.Result> GiveUserPropertyAsync(string authToken, string pubkey, string name, string value, string secret, long validHours, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(pubkey, name, value, secret, validHours);
        try
        {
            return TL.Ret(await API.GiveUserPropertyAsync(authToken, pubkey, name, value, secret, validHours, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }


    public async Task<GigGossipSettlerAPIClient.StringResult> GetMyPropertyValueAsync(string authToken, string name, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(name);
        try
        {
            return TL.Ret(await API.GetMyPropertyValueAsync(authToken, name, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.StringResult> GetMyPropertySecretAsync(string authToken, string name, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(name);
        try
        {
            return TL.Ret(await API.GetMyPropertySecretAsync(authToken, name, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.Result> VerifyChannelAsync(string authToken, string pubkey, string name, string method, string value, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(pubkey, name, method, value);
        try
        {
            return TL.Ret(await API.VerifyChannelAsync(authToken, pubkey, name, method, value, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<Int32Result> SubmitChannelSecretAsync(string authToken, string pubkey, string name, string method, string value, string secret, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(pubkey, name, method, value, secret);
        try
        {
            return TL.Ret(await API.SubmitChannelSecretAsync(authToken, pubkey, name, method, value, secret, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<BooleanResult> IsChannelVerifiedAsync(string authToken, string pubkey, string name, string value, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(pubkey, name, value);
        try
        {
            return TL.Ret(await API.IsChannelVerifiedAsync(authToken, pubkey, name, value, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.Result> RevokeUserPropertyAsync(string authToken, string pubkey, string name, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(pubkey, name);
        try
        {
            return TL.Ret(await API.RevokeUserPropertyAsync(authToken, pubkey, name, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.StringResult> GenerateReplyPaymentPreimageAsync(string authToken, System.Guid gigId, string repliperPubKey, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(gigId, repliperPubKey);
        try
        {
            return TL.Ret(await API.GenerateReplyPaymentPreimageAsync(authToken, gigId, repliperPubKey, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.StringResult> GenerateRelatedPreimageAsync(string authToken, string paymentHash, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(paymentHash);
        try
        {
            return TL.Ret(await API.GenerateRelatedPreimageAsync(authToken, paymentHash, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<BooleanResult> ValidateRelatedPaymentHashesAsync(string authToken, string paymentHash1, string paymentHash2, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(paymentHash1, paymentHash2);
        try
        {
            return TL.Ret(await API.ValidateRelatedPaymentHashesAsync(authToken, paymentHash1, paymentHash2, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.StringResult> RevealPreimageAsync(string authToken, string paymentHash, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(paymentHash);
        try
        {
            return TL.Ret(await API.RevealPreimageAsync(authToken, paymentHash, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.Result> GiveUserFileAsync(string authToken, string pubkey, string name, long? validHours, GigGossipSettlerAPIClient.FileParameter value, GigGossipSettlerAPIClient.FileParameter secret, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(pubkey, name, validHours);
        try
        {
            return TL.Ret(await API.GiveUserFileAsync(authToken, pubkey, name, validHours, value, secret, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.Result> SaveUserTracePropertyAsync(string authToken, string pubkey, string name, string value, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(pubkey, name, value);
        try
        {
            return TL.Ret(await API.SaveUserTracePropertyAsync(authToken, pubkey, name, value, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.GigStatusKeyResult> GetGigStatusAsync(string authToken, System.Guid signedRequestPayloadId, System.Guid repliperCertificateId, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(signedRequestPayloadId, repliperCertificateId);
        try
        {
            return TL.Ret(await API.GetGigStatusAsync(authToken, signedRequestPayloadId, repliperCertificateId, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.StringResult> GenerateRequestPayloadAsync(string authToken, string properties, GigGossipSettlerAPIClient.FileParameter serialisedTopic, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(properties, serialisedTopic.ToBytes());
        try
        {
            return TL.Ret(await API.GenerateRequestPayloadAsync(authToken, properties, serialisedTopic, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.StringResult> GenerateSettlementTrustAsync(string authToken, string properties, string replyinvoice, GigGossipSettlerAPIClient.FileParameter message, GigGossipSettlerAPIClient.FileParameter signedRequestPayloadSerialized, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(properties, replyinvoice, message.ToBytes(), signedRequestPayloadSerialized.ToBytes());
        try
        {
            return TL.Ret(await API.GenerateSettlementTrustAsync(authToken, properties, replyinvoice, message, signedRequestPayloadSerialized, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.StringResult> EncryptJobReplyForCertificateIdAsync(System.Guid? certificateId, GigGossipSettlerAPIClient.FileParameter objectSerialized, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(certificateId, objectSerialized.ToBytes());
        try
        {
            return TL.Ret(await API.EncryptJobReplyForCertificateIdAsync(certificateId, objectSerialized, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.Result> ManageDisputeAsync(string authToken, System.Guid gigId, System.Guid repliperCertificateId, bool open, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(gigId, repliperCertificateId, open);
        try
        {
            return TL.Ret(await API.ManageDisputeAsync(authToken, gigId, repliperCertificateId, open, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.Result> CancelGigAsync(string authToken, System.Guid gigId, System.Guid repliperCertificateId, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(gigId, repliperCertificateId);
        try
        {
            return TL.Ret(await API.CancelGigAsync(authToken, gigId, repliperCertificateId, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.Result> DeleteMyPersonalUserDataAsync(string authToken, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log();
        try
        {
            return TL.Ret(await API.DeleteMyPersonalUserDataAsync(authToken, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.StringResult> IssueNewAccessCodeAsync(string authToken, int length, bool singleUse, long validTill, string memo, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(singleUse, validTill, memo);
        try
        {
            return TL.Ret(await API.IssueNewAccessCodeAsync(authToken, length, singleUse, validTill, memo, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<GigGossipSettlerAPIClient.Result> RevokeAccessCodeAsync(string authToken, string accessCodeId, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(accessCodeId);
        try
        {
            return TL.Ret(await API.RevokeAccessCodeAsync(authToken, accessCodeId, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }


    public async Task<BooleanResult> ValidateAccessCodeAsync(string authToken, string accessCodeId, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(accessCodeId);
        try
        {
            return TL.Ret(await API.ValidateAccessCodeAsync(authToken, accessCodeId,cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }


    public async Task<GigGossipSettlerAPIClient.StringResult> GetMemoFromAccessCodeAsync(string authToken, string accessCodeId, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(accessCodeId);
        try
        {
            return TL.Ret(await API.GetMemoFromAccessCodeAsync(authToken, accessCodeId, cancellationToken));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }


    public IGigStatusClient CreateGigStatusClient()
    {
        return new GigStatusClientWrapper(API.CreateGigStatusClient());
    }

    public IPreimageRevealClient CreatePreimageRevealClient()
    {
        return new PreimageRevealClientWrapper(API.CreatePreimageRevealClient());
    }

}

internal class GigStatusClientWrapper :  IGigStatusClient
{
    IGigStatusClient API;
    GigDebugLoggerAPIClient.LogWrapper<IGigStatusClient> TRACE = GigDebugLoggerAPIClient.FlowLoggerFactory.Trace<IGigStatusClient>();

    public GigStatusClientWrapper(IGigStatusClient api) 
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

    public async Task DisposeAsync()
    {
        using var TL = TRACE.Log();
        try
        {
            await API.DisposeAsync();
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task MonitorAsync(string authToken, Guid gigId, Guid replierCertificateId, CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log().Args(gigId, replierCertificateId);
        try
        {
            await API.MonitorAsync(authToken, gigId, replierCertificateId, cancellationToken);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async IAsyncEnumerable<GigStatusKey> StreamAsync(string authToken, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log();
        await foreach (var row in API.StreamAsync(authToken, cancellationToken))
        {
            TL.Iteration(row);
            yield return row;
        }
    }
}

internal class PreimageRevealClientWrapper : IPreimageRevealClient
{
    IPreimageRevealClient API;
    GigDebugLoggerAPIClient.LogWrapper<IPreimageRevealClient> TRACE = GigDebugLoggerAPIClient.FlowLoggerFactory.Trace<IPreimageRevealClient>();
    public PreimageRevealClientWrapper(IPreimageRevealClient api) 
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

    public async Task DisposeAsync()
    {
        using var TL = TRACE.Log();
        try
        {
            await API.DisposeAsync();
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

    public async IAsyncEnumerable<PreimageReveal> StreamAsync(string authToken, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var TL = TRACE.Log();
        await foreach (var row in API.StreamAsync(authToken, cancellationToken))
        {
            TL.Iteration(row);
            yield return row;
        }
    }
}