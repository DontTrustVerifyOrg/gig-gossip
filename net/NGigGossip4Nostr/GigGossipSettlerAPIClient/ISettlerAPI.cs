using System;
namespace GigGossipSettlerAPIClient
{
    public interface ISettlerAPI
    {
        string BaseUrl { get; }

        Task<StringResult> GetCaPublicKeyAsync(CancellationToken cancellationToken);
        Task<BooleanResult> IsCertificateRevokedAsync(string certid, CancellationToken cancellationToken);
        Task<GuidResult> GetTokenAsync(string pubkey, CancellationToken cancellationToken);
        Task<StringArrayResult> AddressAutocompleteAsync(string authToken, string query, string country, CancellationToken cancellationToken);
        Task<GeolocationRetResult> AddressGeocodeAsync(string authToken, string address, string country, CancellationToken cancellationToken);
        Task<StringResult> LocationGeocodeAsync(string authToken, double lat, double lon, CancellationToken cancellationToken);
        Task<Result> GiveUserPropertyAsync(string authToken, string pubkey, string name, string value, string secret, long validHours, CancellationToken cancellationToken);
        Task<Result> LogEventAsync(string authToken, string eventType, FileParameter message, FileParameter exception, CancellationToken cancellationToken);
        Task<SystemLogEntryListResult> GetLogEventsAsync(string authToken, string pubkey, long frmtmst, long totmst, CancellationToken cancellationToken);
        Task<Result> GiveUserFileAsync(string authToken, string pubkey, string name, long validHours, FileParameter value, FileParameter secret, CancellationToken cancellationToken);
        Task<Result> SaveUserTracePropertyAsync(string authToken, string pubkey, string name, string value, CancellationToken cancellationToken);
        Task<Result> VerifyChannelAsync(string authToken, string pubkey, string name, string method, string value, CancellationToken cancellationToken);
        Task<Int32Result> SubmitChannelSecretAsync(string authToken, string pubkey, string name, string method, string value, string secret, CancellationToken cancellationToken);
        Task<BooleanResult> IsChannelVerifiedAsync(string authToken, string pubkey, string name, string value, CancellationToken cancellationToken);
        Task<Result> RevokeuserpropertyAsync(string authToken, string pubkey, string name, CancellationToken cancellationToken);
        Task<StringResult> GenerateReplyPaymentPreimageAsync(string authToken, string gigId, string repliperPubKey, CancellationToken cancellationToken);
        Task<StringResult> GenerateRelatedPreimageAsync(string authToken, string paymentHash, CancellationToken cancellationToken);
        Task<BooleanResult> ValidateRelatedPaymentHashesAsync(string authToken, string paymentHash1, string paymentHash2, CancellationToken cancellationToken);
        Task<StringResult> RevealPreimageAsync(string authToken, string paymentHash, CancellationToken cancellationToken);
        Task<StringResult> GetGigStatusAsync(string authToken, string signedRequestPayloadId, string repliperCertificateId, CancellationToken cancellationToken);
        Task<StringResult> GenerateRequestPayloadAsync(string authToken, string properties, FileParameter serialisedTopic, CancellationToken cancellationToken);
        Task<StringResult> GenerateSettlementTrustAsync(string authToken, string properties, string replyinvoice, FileParameter message, FileParameter signedRequestPayloadSerialized, CancellationToken cancellationToken);
        Task<StringResult> EncryptObjectForCertificateIdAsync(string certificateId, FileParameter objectSerialized, CancellationToken cancellationToken);
        Task<Result> ManageDisputeAsync(string authToken, string gigId, string repliperCertificateId, bool open, CancellationToken cancellationToken);
        Task<Result> CancelGigAsync(string authToken, string gigId, string repliperCertificateId, CancellationToken cancellationToken);

        IGigStatusClient CreateGigStatusClient();
        IPreimageRevealClient CreatePreimageRevealClient();
    }

    public partial class swaggerClient : ISettlerAPI
    {
        public IGigStatusClient CreateGigStatusClient()
        {
            return new GigStatusClient(this);
        }

        public IPreimageRevealClient CreatePreimageRevealClient()
        {
            return new PreimageRevealClient(this);
        }
    }
}

