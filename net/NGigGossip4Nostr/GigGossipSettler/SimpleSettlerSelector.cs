using System;
using CryptoToolkit;
using GigGossipSettlerAPIClient;
using NBitcoin.Secp256k1;
using GigLNDWalletAPIClient;
using System.Threading.Tasks;

namespace GigGossipSettler;

public interface ISettlerSelector : ICertificationAuthorityAccessor
{
    GigGossipSettlerAPIClient.swaggerClient GetSettlerClient(Uri ServiceUri);
}

public class SimpleSettlerSelector : ISettlerSelector
{
    Dictionary<Uri, GigGossipSettlerAPIClient.swaggerClient> swaggerClients = new();
    HashSet<Guid> revokedCertificates = new();

    HttpClient _httpClient;

    public SimpleSettlerSelector(HttpClient? httpClient=null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<ECXOnlyPubKey> GetPubKeyAsync(Uri serviceUri)
    {
        return (await GetSettlerClient(serviceUri).GetCaPublicKeyAsync()).AsECXOnlyPubKey();
    }

    public GigGossipSettlerAPIClient.swaggerClient GetSettlerClient(Uri serviceUri)
    {
        lock (swaggerClients)
        {
            if (!swaggerClients.ContainsKey(serviceUri))
                swaggerClients[serviceUri] = new GigGossipSettlerAPIClient.swaggerClient(serviceUri.AbsoluteUri, _httpClient);
            return swaggerClients[serviceUri];
        }
    }

    public async Task<bool> IsRevokedAsync(Uri serviceUri,Guid id)
    {
        lock (revokedCertificates)
        {
            if (revokedCertificates.Contains(id))
                return true;
        }
        if (await GetSettlerClient(serviceUri).IsCertificateRevokedAsync(id.ToString()))
        {
            lock (revokedCertificates)
            {
                revokedCertificates.Add(id);
            }
            return true;
        }
        return false;
    }
}

