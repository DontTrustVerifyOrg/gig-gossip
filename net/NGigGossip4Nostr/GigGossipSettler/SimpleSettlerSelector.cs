using System;
using CryptoToolkit;
using GigGossipSettlerAPIClient;
using NBitcoin.Secp256k1;
using GigLNDWalletAPIClient;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace GigGossipSettler;

public interface ISettlerSelector : ICertificationAuthorityAccessor
{
    GigGossipSettlerAPIClient.swaggerClient GetSettlerClient(Uri ServiceUri);
}

public class SimpleSettlerSelector : ISettlerSelector
{
    ConcurrentDictionary<Uri, GigGossipSettlerAPIClient.swaggerClient> swaggerClients = new();
    ConcurrentDictionary<Guid, bool> revokedCertificates = new();

    HttpClient _httpClient;

    public SimpleSettlerSelector(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<ECXOnlyPubKey> GetPubKeyAsync(Uri serviceUri)
    {
        return (await GetSettlerClient(serviceUri).GetCaPublicKeyAsync()).AsECXOnlyPubKey();
    }

    public GigGossipSettlerAPIClient.swaggerClient GetSettlerClient(Uri serviceUri)
    {
        return swaggerClients.GetOrAdd(serviceUri, (serviceUri) => new GigGossipSettlerAPIClient.swaggerClient(serviceUri.AbsoluteUri, _httpClient));
    }

    public async Task<bool> IsRevokedAsync(Uri serviceUri, Guid id)
    {
        return await revokedCertificates.GetOrAddAsync(id, async (id) => await GetSettlerClient(serviceUri).IsCertificateRevokedAsync(id.ToString()));
    }
}

