using System;
using CryptoToolkit;
using GigGossipSettlerAPIClient;
using NBitcoin.Secp256k1;
using GigGossipSettlerAPIClient;
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
            var task = Task.Run(async () => await GetSettlerClient(serviceUri).GetCaPublicKeyAsync());
            return Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(task.Result));
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
            var task = Task.Run(async () => await GetSettlerClient(certificate.ServiceUri).IsCertificateRevokedAsync(certificate.Id.ToString()));
            if (task.Result)
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

