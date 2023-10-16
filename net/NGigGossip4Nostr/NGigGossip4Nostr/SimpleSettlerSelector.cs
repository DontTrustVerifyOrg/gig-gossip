using System;
using CryptoToolkit;
using GigGossipSettlerAPIClient;
using NBitcoin.Secp256k1;
using GigLNDWalletAPIClient;
using System.Threading.Tasks;

namespace NGigGossip4Nostr
{

    public class SimpleSettlerSelector : ISettlerSelector
    {
        Dictionary<Uri, GigGossipSettlerAPIClient.swaggerClient> swaggerClients = new();
        HashSet<Guid> revokedCertificates = new();

        HttpClient httpClient = new HttpClient();

        public SimpleSettlerSelector()
        {
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
                    swaggerClients[serviceUri] = new GigGossipSettlerAPIClient.swaggerClient(serviceUri.AbsoluteUri, httpClient);
                return swaggerClients[serviceUri];
            }
        }

        public async Task<bool> IsRevokedAsync(Certificate certificate)
        {
            lock (revokedCertificates)
            {
                if (revokedCertificates.Contains(certificate.Id))
                    return true;
            }
            if (await GetSettlerClient(certificate.ServiceUri).IsCertificateRevokedAsync(certificate.Id.ToString()))
            {
                lock (revokedCertificates)
                {
                    revokedCertificates.Add(certificate.Id);
                }
                return true;
            }
            return false;
        }
    }
}

