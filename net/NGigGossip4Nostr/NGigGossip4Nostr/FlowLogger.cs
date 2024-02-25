using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Dynamic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
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
        Task TraceExceptionAsync(Exception exception, string? message=null);
        Task NewMessageAsync(string a, string b, string message);
        Task NewReplyAsync(string a, string b, string message);
        Task NewConnectedAsync(string a, string b, string message);
        Task NewNoteAsync(string a, string message);
    }

    public class FlowLogger : IFlowLogger
    {
        private Uri settlerUri;
        private ISettlerAPI settlerAPI;
        private ECPrivKey privKey;
        private string publicKey;
        private SemaphoreSlim guard = new(1, 1);
        public bool Enabled { get; set; }
        CancellationTokenSource CancellationTokenSource = new();

        public FlowLogger(bool traceEnabled, ECPrivKey eCPrivKey, Uri settlerUri, Func<HttpClient> httpFactory)
        {
            this.settlerUri = settlerUri;
            this.settlerAPI = new swaggerClient(settlerUri.AbsoluteUri, httpFactory());
            this.privKey = eCPrivKey;
            this.publicKey = eCPrivKey.CreateXOnlyPubKey().AsHex();
            this.Enabled = traceEnabled;
        }

        private async Task<string> MakeSettlerAuthTokenAsync()
        {
            return Crypto.MakeSignedTimedToken(
                privKey, DateTime.UtcNow,
                SettlerAPIResult.Get<Guid>(await settlerAPI.GetTokenAsync(publicKey, CancellationTokenSource.Token)));
        }

        public async Task WriteToLogAsync(System.Diagnostics.TraceEventType eventType, string message)
        {
            if (!Enabled) return;

            await guard.WaitAsync();
            try
            {
                SettlerAPIResult.Check(await settlerAPI.LogEventAsync(
                    await MakeSettlerAuthTokenAsync(),
                    eventType.ToString(),
                    new FileParameter(new MemoryStream(Encoding.UTF8.GetBytes(message))),
                    new FileParameter(new MemoryStream(Encoding.UTF8.GetBytes(""))),
                    CancellationTokenSource.Token
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

        public async Task WriteExceptionAsync(System.Diagnostics.TraceEventType eventType, Exception exception, string? message = null)
        {
            if (!Enabled) return;

            await guard.WaitAsync();
            try
            {
                SettlerAPIResult.Check(await settlerAPI.LogEventAsync(
                await MakeSettlerAuthTokenAsync(),
                eventType.ToString(),
                new FileParameter(new MemoryStream(Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(message) ? exception.Message : message))),
                new FileParameter(new MemoryStream(Encoding.UTF8.GetBytes(exception.ToJsonString()))),
                CancellationTokenSource.Token
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

        public async Task TraceExceptionAsync(Exception exception, string? message = null)
        {
            if (!Enabled) return;

            await WriteExceptionAsync(System.Diagnostics.TraceEventType.Critical, exception, message);
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

        public async Task NewConnectedAsync(string a, string b, string message)
        {
            if (!Enabled) return;

            await WriteToLogAsync(
                 System.Diagnostics.TraceEventType.Transfer,
                "\t" + a + "--)" + b + ": " + message);
        }

        public async Task NewNoteAsync(string a, string message)
        {
            if (!Enabled) return;

            await WriteToLogAsync(
                 System.Diagnostics.TraceEventType.Transfer,
                 "\t Note over " + a + ": " + message);
        }

    }

}

