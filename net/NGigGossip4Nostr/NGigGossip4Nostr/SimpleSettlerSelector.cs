using System;
using CryptoToolkit;
using GigLNDWalletAPIClient;
using NBitcoin.Secp256k1;
using GigLNDWalletAPIClient;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace NGigGossip4Nostr;

public interface IGigLNDWalletSelector
{
    GigLNDWalletAPIClient.swaggerClient GetWalletClient(Uri ServiceUri);
}

public class SimpleGigLNDWalletSelector : IGigLNDWalletSelector
{
    ConcurrentDictionary<Uri, GigLNDWalletAPIClient.swaggerClient> swaggerClients = new();

    Func<HttpClient> _httpClientFactory;

    public SimpleGigLNDWalletSelector(Func<HttpClient> httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public GigLNDWalletAPIClient.swaggerClient GetWalletClient(Uri serviceUri)
    {
        return swaggerClients.GetOrAdd(serviceUri, (serviceUri) => new GigLNDWalletAPIClient.swaggerClient(serviceUri.AbsoluteUri, _httpClientFactory()));
    }

}

