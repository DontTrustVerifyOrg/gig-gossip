using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Dynamic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using CryptoToolkit;
using GigDebugLoggerAPIClient;
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
        private Uri loggerUri;
        private IGigDebugLoggerAPI loggerAPI;
        private string pubkey;
        private SemaphoreSlim guard = new(1, 1);
        public bool Enabled { get; set; }
        CancellationTokenSource CancellationTokenSource = new();

        public FlowLogger(bool traceEnabled, string pubkey, Uri loggerUri, Func<HttpClient> httpFactory)
        {
            this.loggerUri = loggerUri;
            this.loggerAPI = new swaggerClient(loggerUri.AbsoluteUri, httpFactory());
            this.pubkey = pubkey;
            this.Enabled = traceEnabled;
        }

        public async Task WriteToLogAsync( System.Diagnostics.TraceEventType eventType, string message)
        {
            if (!Enabled) return;

            await guard.WaitAsync();
            try
            {
                LoggerAPIResult.Check(await loggerAPI.LogEventAsync(
                    "API_KEY",
                    pubkey,
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
                LoggerAPIResult.Check(await loggerAPI.LogEventAsync(
                "API_KEY",
                pubkey,
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

