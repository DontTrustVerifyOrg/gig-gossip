using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using CryptoToolkit;
using GigGossipSettlerAPIClient;
using GoogleApi.Entities.Places.Common;
using NBitcoin.Protocol;
using NBitcoin.Secp256k1;

namespace NGigGossip4Nostr
{
    public interface IFlowLogger
    {
        bool Enabled { get; set; }
        Task TraceInformationAsync(string? message);
        Task TraceWarningAsync(string? message);
        Task TraceErrorAsync(string? message);
        Task TraceExceptionAsync(Exception exception);
        Task NewMessageAsync(string a, string b, string message);
        Task NewReplyAsync(string a, string b, string message);
        Task NewConnected(string a, string b, string message);
        Task NewNote(string a, string message);
    }

    public class FlowLogger : IFlowLogger
    {
        private Uri settlerUri;
        private ISettlerSelector settlerSelector;
        private ECPrivKey privKey;
        private string publicKey;
        private SemaphoreSlim guard = new(1, 1);
        public bool Enabled { get; set; }

        public FlowLogger(bool traceEnabled, Uri settlerUri, ISettlerSelector settlerSelector, ECPrivKey eCPrivKey)
        {
            this.settlerUri = settlerUri;
            this.settlerSelector = settlerSelector;
            this.privKey = eCPrivKey;
            this.publicKey = eCPrivKey.CreateXOnlyPubKey().AsHex();
            this.Enabled = traceEnabled;
        }

        private async Task<string> MakeSettlerAuthTokenAsync()
        {
            return Crypto.MakeSignedTimedToken(
                privKey, DateTime.UtcNow,
                SettlerAPIResult.Get<Guid>(await settlerSelector.GetSettlerClient(settlerUri).GetTokenAsync(publicKey)));
        }

        public async Task WriteToLogAsync(System.Diagnostics.TraceEventType eventType, string message)
        {
            if (!Enabled) return;

            await guard.WaitAsync();
            try
            {
                SettlerAPIResult.Check(await settlerSelector.GetSettlerClient(settlerUri).LogEventAsync(
                    await MakeSettlerAuthTokenAsync(),
                    eventType.ToString(),
                    new FileParameter(new MemoryStream(Encoding.UTF8.GetBytes(message))),
                    new FileParameter(new MemoryStream(Encoding.UTF8.GetBytes("")))
                    ));
            }
            catch(Exception ex)
            {
                Trace.TraceError(ex.Message);
            }
            finally
            {
                guard.Release();
            }
        }

        public async Task WriteExceptionAsync(System.Diagnostics.TraceEventType eventType, Exception exception)
        {
            if (!Enabled) return;

            await guard.WaitAsync();
            try
            {
                SettlerAPIResult.Check(await settlerSelector.GetSettlerClient(settlerUri).LogEventAsync(
                await MakeSettlerAuthTokenAsync(),
                eventType.ToString(),
                new FileParameter(new MemoryStream(Encoding.UTF8.GetBytes(exception.Message))),
                new FileParameter(new MemoryStream(Encoding.UTF8.GetBytes(exception.ToJsonString())))
                ));
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.Message);
            }
            finally
            {
                guard.Release();
            }
        }

        public async Task TraceInformationAsync(string? message)
        {
            if (!Enabled) return;

            await WriteToLogAsync(System.Diagnostics.TraceEventType.Information, message);
        }

        public async Task TraceWarningAsync(string? message)
        {
            if (!Enabled) return;

            await WriteToLogAsync(System.Diagnostics.TraceEventType.Warning, message);
        }

        public async Task TraceErrorAsync(string? message)
        {
            if (!Enabled) return;

            await WriteToLogAsync(System.Diagnostics.TraceEventType.Error, message);
        }

        public async Task TraceExceptionAsync(Exception exception)
        {
            if (!Enabled) return;

            await WriteExceptionAsync(System.Diagnostics.TraceEventType.Critical, exception);
        }

        public async Task NewMessageAsync(string a, string b, string message)
        {
            if (!Enabled) return;

            await WriteToLogAsync(
                 System.Diagnostics.TraceEventType.Transfer,
                 "\t" + a + "->>" + b + ": " + message);
        }

        public async Task NewReplyAsync(string a, string b, string message)
        {
            if (!Enabled) return;

            await WriteToLogAsync(
                 System.Diagnostics.TraceEventType.Transfer,
                "\t" + a + "-->>" + b + ": " + message);
        }

        public async Task NewConnected(string a, string b, string message)
        {
            if (!Enabled) return;

            await WriteToLogAsync(
                 System.Diagnostics.TraceEventType.Transfer,
                "\t" + a + "--)" + b + ": " + message);
        }

        public async Task NewNote(string a,string message)
        {
            if (!Enabled) return;

            await WriteToLogAsync(
                 System.Diagnostics.TraceEventType.Transfer,
                 "\t Note over " + a + ": " + message);
        }

    }
}

