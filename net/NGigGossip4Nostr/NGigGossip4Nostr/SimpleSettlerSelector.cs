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

        public ECXOnlyPubKey GetPubKey(Uri serviceUri)
        {
            return GetSettlerClient(serviceUri).GetCaPublicKeyAsync().Result.AsECXOnlyPubKey();
        }

        public GigGossipSettlerAPIClient.swaggerClient GetSettlerClient(Uri serviceUri)
        {
            if (!swaggerClients.ContainsKey(serviceUri))
                swaggerClients[serviceUri] = new GigGossipSettlerAPIClient.swaggerClient(serviceUri.AbsoluteUri, httpClient);
            return swaggerClients[serviceUri];
        }

        public bool IsRevoked(Certificate certificate)
        {
            lock (revokedCertificates)
            {
                if (revokedCertificates.Contains(certificate.Id))
                    return true;
            }
            if (GetSettlerClient(certificate.ServiceUri).IsCertificateRevokedAsync(certificate.Id.ToString()).Result)
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

