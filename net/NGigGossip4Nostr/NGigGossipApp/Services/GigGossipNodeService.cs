using System;
using System.Net.Http;
using CryptoToolkit;

namespace GigMobile.Services
{
    public static class GigGossipNodeService
    {
        public static Lazy<GigLNDWalletAPIClient.swaggerClient> WalletClient = new Lazy<GigLNDWalletAPIClient.swaggerClient>(
            () => new GigLNDWalletAPIClient.swaggerClient(GigGossipNodeConfig.GigWalletOpenApi, new HttpClient())

            );

        public static Lazy<GigGossipNode> GigGossipNode = new Lazy<GigGossipNode>(() =>
        {
            var node = new GigGossipNode(
                Path.Combine(FileSystem.AppDataDirectory, GigGossipNodeConfig.DatabaseFile),
                SecureDatabase.GetPrivateKeyAsync().Result.AsECPrivKey(),
                GigGossipNodeConfig.NostrRelays,
                GigGossipNodeConfig.ChunkSize
            );

            var walletClient = WalletClient.Value;

            node.Init(
                GigGossipNodeConfig.Fanout,
                GigGossipNodeConfig.PriceAmountForRouting,
                TimeSpan.FromMilliseconds(GigGossipNodeConfig.BroadcastConditionsTimeoutMs),
                GigGossipNodeConfig.BroadcastConditionsPowScheme,
                GigGossipNodeConfig.BroadcastConditionsPowComplexity,
                TimeSpan.FromMilliseconds(GigGossipNodeConfig.TimestampToleranceMs),
                TimeSpan.FromSeconds(GigGossipNodeConfig.InvoicePaymentTimeoutSec),
                walletClient);

            return node;
        });
    }
}
