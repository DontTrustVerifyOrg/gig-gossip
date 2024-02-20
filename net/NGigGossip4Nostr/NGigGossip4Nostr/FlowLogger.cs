using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using CryptoToolkit;
using GigGossipSettlerAPIClient;
using GoogleApi.Entities.Places.Common;
using NBitcoin.Protocol;
using NBitcoin.Secp256k1;

namespace NGigGossip4Nostr
{
    public static class FlowLogger
    {
        static Uri settlerUri;
        static ISettlerSelector settlerSelector;
        static ECPrivKey privKey;
        static string publicKey;
        static ConcurrentDictionary<string, string> participantAliases = new();


        public static void Start(Uri settlerUri, ISettlerSelector settlerSelector, ECPrivKey eCPrivKey)
        {
            FlowLogger.settlerUri = settlerUri;
            FlowLogger.settlerSelector = settlerSelector;
            FlowLogger.privKey = eCPrivKey;
            FlowLogger.publicKey = eCPrivKey.CreateXOnlyPubKey().AsHex();
        }

        public static async Task<string> MakeSettlerAuthTokenAsync()
        {
            return Crypto.MakeSignedTimedToken(
                privKey, DateTime.UtcNow,
                SettlerAPIResult.Get<Guid>(await settlerSelector.GetSettlerClient(settlerUri).GetTokenAsync(publicKey)));
        }

        public static void Stop()
        {
        }

        public static async Task WriteToLogAsync(System.Diagnostics.TraceEventType eventType, string message)
        {
            SettlerAPIResult.Check(await settlerSelector.GetSettlerClient(settlerUri).LogEventAsync(
                await MakeSettlerAuthTokenAsync(),
                eventType.ToString(),
                new FileParameter(new MemoryStream(message.AsBytes())),
                new FileParameter(new MemoryStream("".AsBytes()))
                ));
        }

        public static async Task WriteExceptionAsync(System.Diagnostics.TraceEventType eventType, Exception exception)
        {
            SettlerAPIResult.Check(await settlerSelector.GetSettlerClient(settlerUri).LogEventAsync(
                await MakeSettlerAuthTokenAsync(),
                eventType.ToString(),
                new FileParameter(new MemoryStream(exception.Message.AsBytes())),
                new FileParameter(new MemoryStream(exception.ToJsonString().AsBytes()))
                ));
        }

        public static async Task SetupParticipantAsync(string id, bool isActor, string alias = null)
        {
            if (settlerUri == null)
                return;

            await WriteToLogAsync(
                System.Diagnostics.TraceEventType.Information,
                (isActor ? "\tactor " : "\tparticipant ") + id + (alias == null ? "" : " as " + alias));
        }

        public static async Task NewMessageAsync(string a, string b, string message)
        {
            if (settlerUri == null)
                return;
            if (!participantAliases.ContainsKey(a))
                return;
            if (!participantAliases.ContainsKey(b))
                return;
            await WriteToLogAsync(
                 System.Diagnostics.TraceEventType.Information,
                 "\t" + a + "->>" + b + ": " + message);
        }

        public static async Task NewReplyAsync(string a, string b, string message)
        {
            if (settlerUri == null)
                return;
            if (!participantAliases.ContainsKey(a))
                return;
            if (!participantAliases.ContainsKey(b))
                return;
            await WriteToLogAsync(
                 System.Diagnostics.TraceEventType.Information,
                "\t" + a + "-->>" + b + ": " + message);
        }

        public static async Task NewConnected(string a, string b, string message)
        {
            if (settlerUri == null)
                return;
            if (!participantAliases.ContainsKey(a))
                return;
            if (!participantAliases.ContainsKey(b))
                return;
            await WriteToLogAsync(
                 System.Diagnostics.TraceEventType.Information,
                "\t" + a + "--)" + b + ": " + message);
        }

        public static async Task NewEvent(string a,string message)
        {
            if (settlerUri == null)
                return;
            if (!participantAliases.ContainsKey(a))
                return;
            await WriteToLogAsync(
                 System.Diagnostics.TraceEventType.Information,
                 "\t Note over " + a + ": " + message);
        }

    }
}

