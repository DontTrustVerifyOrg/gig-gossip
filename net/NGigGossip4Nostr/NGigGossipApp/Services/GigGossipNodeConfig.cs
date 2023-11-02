using System;
namespace GigMobile.Services
{
    public static class GigGossipNodeConfig
    {
        public static string GigWalletOpenApi => $"https://{Localhost}:7101/";
        public static string[] NostrRelays => new string[] { $"ws://{Localhost}:6969" };
        public static Uri SettlerOpenApi => new($"https://{Localhost}:7189/");
        public const string DatabaseFile = "GigGossip.db3";
        public const int Fanout = 2;
        public const long PriceAmountForRouting = 1000;
        public const int BroadcastConditionsTimeoutMs = 1000000;
        public const string BroadcastConditionsPowScheme = "sha256";
        public const int BroadcastConditionsPowComplexity = 0;
        public const int TimestampToleranceMs = 1000000;
        public const int InvoicePaymentTimeoutSec = 1000;
        public const int ChunkSize = 2048;

#if DEBUG
        private static string Localhost => DeviceInfo.Platform == DevicePlatform.Android ? "10.0.2.2" : "localhost";
#else
        private static string _localhost = "localhost";
#endif
    }
}

