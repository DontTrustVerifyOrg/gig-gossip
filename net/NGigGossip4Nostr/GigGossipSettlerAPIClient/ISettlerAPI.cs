using System;
using System.Net.Sockets;
using System.Threading;
using GoogleApi.Entities.Translate.Translate.Request.Enums;
using GoogleApi.Interfaces;
using Microsoft.AspNetCore.SignalR.Client;
using NetworkClientToolkit;
using Newtonsoft.Json.Linq;

namespace GigGossipSettlerAPIClient;

public interface ISettlerAPI
{
    string BaseUrl { get; }
    IRetryPolicy RetryPolicy { get; }

    Task<CaPricingResult> GetCaPricingAsync(string country, string currency, System.Threading.CancellationToken cancellationToken);
    Task<StringResult> GetCaPublicKeyAsync(CancellationToken cancellationToken);
    Task<BooleanResult> IsCertificateRevokedAsync(System.Guid certid, CancellationToken cancellationToken);
    Task<GuidResult> GetTokenAsync(string pubkey, CancellationToken cancellationToken);

    Task<StringArrayResult> AddressAutocompleteAsync(string authToken, string query, string country, double longitude, double lattitude, double radius, System.Threading.CancellationToken cancellationToken);
    Task<GeolocationRetResult> AddressGeocodeAsync(string authToken, string address, string country, CancellationToken cancellationToken);
    Task<StringResult> LocationGeocodeAsync(string authToken, double lat, double lon, CancellationToken cancellationToken);
    Task<RouteRetResult> GetRouteAsync(string authToken, double fromLat, double fromLon, double toLat, double toLon, CancellationToken cancellationToken);
    Task<StringResult> IssueNewAccessCodeAsync(string authToken, int length, bool singleUse, long validTill, string memo, CancellationToken cancellationToken);
    Task<BooleanResult> ValidateAccessCodeAsync(string authToken, string accessCodeId, CancellationToken cancellationToken);
    Task<Result> RevokeAccessCodeAsync(string authToken, string accessCodeId, CancellationToken cancellationToken);
    Task<StringResult> GetMemoFromAccessCodeAsync(string authToken, string accessCodeId, System.Threading.CancellationToken cancellationToken);

    Task<Result> GiveUserPropertyAsync(string authToken, string pubkey, string name, string value, string secret, long validHours, CancellationToken cancellationToken);
    Task<Result> GiveUserFileAsync(string authToken, string pubkey, string name, long? validHours, FileParameter value, FileParameter secret, CancellationToken cancellationToken);
    Task<Result> SaveUserTracePropertyAsync(string authToken, string pubkey, string name, string value, CancellationToken cancellationToken);
    Task<StringResult> GetUserPropertyValueAsync(string authToken, string pubkey, string name, System.Threading.CancellationToken cancellationToken);
    Task<StringResult> GetUserPropertySecretAsync(string authToken, string pubkey, string name, System.Threading.CancellationToken cancellationToken);
    Task<StringResult> GetMyPropertyValueAsync(string authToken, string name, CancellationToken cancellationToken);
    Task<StringResult> GetMyPropertySecretAsync(string authToken, string name, System.Threading.CancellationToken cancellationToken);
    Task<Result> VerifyChannelAsync(string authToken, string pubkey, string name, string method, string value, CancellationToken cancellationToken);
    Task<Int32Result> SubmitChannelSecretAsync(string authToken, string pubkey, string name, string method, string value, string secret, CancellationToken cancellationToken);
    Task<BooleanResult> IsChannelVerifiedAsync(string authToken, string pubkey, string name, string value, CancellationToken cancellationToken);
    Task<Result> RevokeUserPropertyAsync(string authToken, string pubkey, string name, CancellationToken cancellationToken);
    Task<StringResult> GenerateReplyPaymentPreimageAsync(string authToken, System.Guid gigId, string repliperPubKey, CancellationToken cancellationToken);
    Task<StringResult> GenerateRelatedPreimageAsync(string authToken, string paymentHash, CancellationToken cancellationToken);
    Task<BooleanResult> ValidateRelatedPaymentHashesAsync(string authToken, string paymentHash1, string paymentHash2, CancellationToken cancellationToken);
    Task<StringResult> RevealPreimageAsync(string authToken, string paymentHash, CancellationToken cancellationToken);
    Task<GigStatusKeyResult> GetGigStatusAsync(string authToken, System.Guid signedRequestPayloadId, System.Guid repliperCertificateId, CancellationToken cancellationToken);
    Task<StringResult> GenerateRequestPayloadAsync(string authToken, string properties, FileParameter serialisedTopic, CancellationToken cancellationToken);
    Task<StringResult> GenerateSettlementTrustAsync(string authToken, string properties, string replyinvoice, FileParameter message, FileParameter signedRequestPayloadSerialized, CancellationToken cancellationToken);
    Task<StringResult> EncryptJobReplyForCertificateIdAsync(System.Guid? certificateId, FileParameter objectSerialized, CancellationToken cancellationToken);
    Task<Result> ManageDisputeAsync(string authToken, System.Guid gigId, System.Guid repliperCertificateId, bool open, CancellationToken cancellationToken);
    Task<Result> OpenDisputeAsync(string authToken, string driverPublicKey, string reason, string gigId, string repliercertificateId, string paymentClientSecret, System.Threading.CancellationToken cancellationToken);
    Task<Result> OpenDriverDisputeAsync(string authToken, string riderPublicKey, string reason, string gigId, string repliercertificateId, System.Threading.CancellationToken cancellationToken);
    Task<Result> OpenTicketAsync(string authToken, string message, System.Threading.CancellationToken cancellationToken);
    Task<Result> CancelGigAsync(string authToken, System.Guid gigId, System.Guid repliperCertificateId, CancellationToken cancellationToken);

    Task<Result> DeleteMyPersonalUserDataAsync(string authToken, System.Threading.CancellationToken cancellationToken);

    IGigStatusClient CreateGigStatusClient();
    IPreimageRevealClient CreatePreimageRevealClient();
}

public partial class swaggerClient : ISettlerAPI
{
    public IRetryPolicy RetryPolicy { get; set; } = null;
    public swaggerClient(string baseUrl, System.Net.Http.HttpClient httpClient, IRetryPolicy retryPolicy) : this(baseUrl,httpClient)
    {
        RetryPolicy = retryPolicy;
    }

    public IGigStatusClient CreateGigStatusClient()
    {
        return new GigStatusClient(this);
    }

    public IPreimageRevealClient CreatePreimageRevealClient()
    {
        return new PreimageRevealClient(this);
    }
}

public class SettlerAPIRetryWrapper : ISettlerAPI
{
    ISettlerAPI api;
    public string BaseUrl => api.BaseUrl;
    public IRetryPolicy RetryPolicy => api.RetryPolicy;

    public SettlerAPIRetryWrapper(string baseUrl, System.Net.Http.HttpClient httpClient, IRetryPolicy retryPolicy)
    {
        this.api = new swaggerClient(baseUrl, httpClient, retryPolicy);
    }

    public IGigStatusClient CreateGigStatusClient()
    {
        return api.CreateGigStatusClient();
    }

    public IPreimageRevealClient CreatePreimageRevealClient()
    {
        return api.CreatePreimageRevealClient();
    }

    public async Task<CaPricingResult> GetCaPricingAsync(string country, string currency, System.Threading.CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GetCaPricingAsync(country, currency, cancellationToken));
    }

    public async Task<StringResult> GetCaPublicKeyAsync(CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GetCaPublicKeyAsync(cancellationToken));
    }

    public async Task<BooleanResult> IsCertificateRevokedAsync(System.Guid certid, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.IsCertificateRevokedAsync(certid, cancellationToken));
    }

    public async Task<GuidResult> GetTokenAsync(string pubkey, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GetTokenAsync(pubkey, cancellationToken));
    }

    public async Task<StringArrayResult> AddressAutocompleteAsync(string authToken, string query, string country, double longitude, double lattitude, double radius, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.AddressAutocompleteAsync(authToken, query, country, longitude, lattitude, radius, cancellationToken));
    }

    public async Task<GeolocationRetResult> AddressGeocodeAsync(string authToken, string address, string country, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.AddressGeocodeAsync(authToken, address, country, cancellationToken));
    }

    public async Task<StringResult> LocationGeocodeAsync(string authToken, double lat, double lon, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.LocationGeocodeAsync(authToken, lat, lon, cancellationToken));
    }

    public async Task<Result> GiveUserPropertyAsync(string authToken, string pubkey, string name, string value, string secret, long validHours, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GiveUserPropertyAsync(authToken, pubkey, name, value, secret, validHours, cancellationToken));
    }

    public async Task<Result> GiveUserFileAsync(string authToken, string pubkey, string name, long? validHours, FileParameter value, FileParameter secret, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GiveUserFileAsync(authToken, pubkey, name, validHours, value, secret, cancellationToken));
    }

    public async Task<StringResult> GetUserPropertyValueAsync(string authToken, string pubkey, string name, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GetUserPropertyValueAsync(authToken, pubkey, name, cancellationToken));
    }

    public async Task<StringResult> GetUserPropertySecretAsync(string authToken, string pubkey, string name, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GetUserPropertySecretAsync(authToken, pubkey, name, cancellationToken));
    }

    public async Task<Result> SaveUserTracePropertyAsync(string authToken, string pubkey, string name, string value, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.SaveUserTracePropertyAsync(authToken, pubkey, name, value, cancellationToken));
    }

    public async Task<StringResult> GetMyPropertyValueAsync(string authToken, string name, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GetMyPropertyValueAsync(authToken, name, cancellationToken));
    }

    public async Task<StringResult> GetMyPropertySecretAsync(string authToken, string name, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GetMyPropertySecretAsync(authToken, name, cancellationToken));
    }

    public async Task<Result> VerifyChannelAsync(string authToken, string pubkey, string name, string method, string value, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.VerifyChannelAsync(authToken, pubkey, name, method, value, cancellationToken));
    }

    public async Task<Int32Result> SubmitChannelSecretAsync(string authToken, string pubkey, string name, string method, string value, string secret, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.SubmitChannelSecretAsync(authToken, pubkey, name, method, value, secret, cancellationToken));
    }

    public async Task<BooleanResult> IsChannelVerifiedAsync(string authToken, string pubkey, string name, string value, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.IsChannelVerifiedAsync(authToken, pubkey, name, value, cancellationToken));
    }

    public async Task<Result> RevokeUserPropertyAsync(string authToken, string pubkey, string name, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.RevokeUserPropertyAsync(authToken, pubkey, name, cancellationToken));
    }

    public async Task<StringResult> GenerateReplyPaymentPreimageAsync(string authToken, System.Guid gigId, string repliperPubKey, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GenerateReplyPaymentPreimageAsync(authToken, gigId, repliperPubKey, cancellationToken));
    }

    public async Task<StringResult> GenerateRelatedPreimageAsync(string authToken, string paymentHash, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GenerateRelatedPreimageAsync(authToken, paymentHash, cancellationToken));
    }

    public async Task<BooleanResult> ValidateRelatedPaymentHashesAsync(string authToken, string paymentHash1, string paymentHash2, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.ValidateRelatedPaymentHashesAsync(authToken, paymentHash1, paymentHash2, cancellationToken));
    }

    public async Task<StringResult> RevealPreimageAsync(string authToken, string paymentHash, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.RevealPreimageAsync(authToken, paymentHash, cancellationToken));
    }

    public async Task<GigStatusKeyResult> GetGigStatusAsync(string authToken, System.Guid signedRequestPayloadId, System.Guid repliperCertificateId, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GetGigStatusAsync(authToken, signedRequestPayloadId, repliperCertificateId, cancellationToken));
    }

    public async Task<StringResult> GenerateRequestPayloadAsync(string authToken, string properties, FileParameter serialisedTopic, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GenerateRequestPayloadAsync(authToken, properties, serialisedTopic, cancellationToken));
    }

    public async Task<StringResult> GenerateSettlementTrustAsync(string authToken, string properties, string replyinvoice, FileParameter message, FileParameter signedRequestPayloadSerialized, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GenerateSettlementTrustAsync(authToken, properties, replyinvoice, message, signedRequestPayloadSerialized, cancellationToken));
    }

    public async Task<StringResult> EncryptJobReplyForCertificateIdAsync(System.Guid? certificateId, FileParameter objectSerialized, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.EncryptJobReplyForCertificateIdAsync(certificateId, objectSerialized, cancellationToken));
    }

    public async Task<Result> ManageDisputeAsync(string authToken, System.Guid gigId, System.Guid repliperCertificateId, bool open, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.ManageDisputeAsync(authToken, gigId, repliperCertificateId, open, cancellationToken));
    }

    public async Task<Result> CancelGigAsync(string authToken, System.Guid gigId, System.Guid repliperCertificateId, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.CancelGigAsync(authToken, gigId, repliperCertificateId, cancellationToken));
    }


    public async Task<RouteRetResult> GetRouteAsync(string authToken, double fromLat, double fromLon, double toLat, double toLon, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GetRouteAsync(authToken, fromLat, fromLon, toLat, toLon, cancellationToken));
    }

    public async Task<StringResult> IssueNewAccessCodeAsync(string authToken, int length, bool singleUse, long validTill, string memo, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.IssueNewAccessCodeAsync(authToken, length, singleUse, validTill, memo, cancellationToken));
    }

    public async Task<BooleanResult> ValidateAccessCodeAsync(string authToken, string accessCodeId, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.ValidateAccessCodeAsync(authToken, accessCodeId, cancellationToken));
    }

    public async Task<Result> RevokeAccessCodeAsync(string authToken, string accessCodeId, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.RevokeAccessCodeAsync(authToken, accessCodeId, cancellationToken));
    }

    public async Task<StringResult> GetMemoFromAccessCodeAsync(string authToken, string accessCodeId, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.GetMemoFromAccessCodeAsync(authToken, accessCodeId, cancellationToken));
    }

    public async Task<Result> DeleteMyPersonalUserDataAsync(string authToken, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.DeleteMyPersonalUserDataAsync(authToken, cancellationToken));
    }

    public async Task<Result> OpenDisputeAsync(string authToken, string driverPublicKey, string reason, string gigId, string repliercertificateId, string paymentClientSecret, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.OpenDisputeAsync(authToken, driverPublicKey, reason, gigId, repliercertificateId, paymentClientSecret, cancellationToken));
    }

    public async Task<Result> OpenDriverDisputeAsync(string authToken, string riderPublicKey, string reason, string gigId, string repliercertificateId, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.OpenDriverDisputeAsync(authToken, riderPublicKey, reason, gigId, repliercertificateId, cancellationToken));
    }

    public async Task<Result> OpenTicketAsync(string authToken, string message, CancellationToken cancellationToken)
    {
        return await RetryPolicy.WithRetryPolicy(() => api.OpenTicketAsync(authToken, message, cancellationToken));
    }
}
