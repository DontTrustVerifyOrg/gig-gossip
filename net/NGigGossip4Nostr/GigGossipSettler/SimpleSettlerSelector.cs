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
    ISettlerAPI GetSettlerClient(Uri ServiceUri);
}

public class SimpleSettlerSelector : ISettlerSelector
{
    ConcurrentDictionary<Uri, ISettlerAPI> swaggerClients = new();
    ConcurrentDictionary<Guid, bool> revokedCertificates = new();

    HttpClient _httpClient;

    public SimpleSettlerSelector(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<ECXOnlyPubKey> GetPubKeyAsync(Uri serviceUri, CancellationToken cancellationToken)
    {
        return SettlerAPIResult.Get<string>(await GetSettlerClient(serviceUri).GetCaPublicKeyAsync(cancellationToken)).AsECXOnlyPubKey();
    }

    public ISettlerAPI GetSettlerClient(Uri serviceUri)
    {
        return swaggerClients.GetOrAdd(serviceUri, (serviceUri) => new GigGossipSettlerAPIClient.swaggerClient(serviceUri.AbsoluteUri, _httpClient));
    }

    public async Task<bool> IsRevokedAsync(Uri serviceUri, Guid id, CancellationToken cancellationToken)
    {
        return await revokedCertificates.GetOrAddAsync(id, async (id) => SettlerAPIResult.Get<bool>(await GetSettlerClient(serviceUri).IsCertificateRevokedAsync(id.ToString(), cancellationToken)));
    }
}

