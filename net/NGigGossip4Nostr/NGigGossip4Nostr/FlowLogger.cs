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
using GoogleApi.Entities.Search.Video.Common.Enums;
using NBitcoin.Protocol;
using NBitcoin.Secp256k1;

namespace NGigGossip4Nostr
{
    struct MemLogEntry
    {
        public required string EvType;
        public required string Message;
        public required Exception Except;
    }

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
        ConcurrentQueue<MemLogEntry> memLogEntries = new();

        Thread writeThread = null;

        private IGigDebugLoggerAPI loggerAPI;
        public bool Enabled { get; set; }
        CancellationTokenSource CancellationTokenSource = new();

        public FlowLogger(bool traceEnabled, string pubkey, Uri loggerUri, Func<HttpClient> httpFactory)
        {
            this.loggerAPI = new swaggerClient(loggerUri.AbsoluteUri, httpFactory());
            this.Enabled = traceEnabled;
            writeThread = new(async () =>
            {
                while (true)
                {
                    while (memLogEntries.TryDequeue(out var entry))
                    {
                        try
                        {
                            LoggerAPIResult.Check(await loggerAPI.LogEventAsync(
                            "API_KEY",
                                pubkey,
                                entry.EvType,
                                new FileParameter(new MemoryStream(Encoding.UTF8.GetBytes(entry.Message))),
                                new FileParameter(new MemoryStream(Encoding.UTF8.GetBytes(entry.Except == null ? "" : entry.Except.ToJsonString()))),
                                CancellationTokenSource.Token
                                ));
                        }
                        catch (Exception ex)
                        {
                            Trace.TraceError(ex.Message);
                        }
                    }
                    Thread.Sleep(250);
                }
            });
            writeThread.Start();
        }

        public async Task WriteToLogAsync( System.Diagnostics.TraceEventType eventType, string message)
        {
            if (!Enabled) return;

            memLogEntries.Enqueue(new MemLogEntry
            {
                EvType = eventType.ToString(),
                Message = message,
                Except = null
            });
            if (memLogEntries.Count > 1000)
                Thread.Sleep(10000);
        }

        public async Task WriteExceptionAsync(System.Diagnostics.TraceEventType eventType, Exception exception, string? message = null)
        {
            if (!Enabled) return;

            memLogEntries.Enqueue(new MemLogEntry
            {
                EvType = eventType.ToString(),
                Message = string.IsNullOrWhiteSpace(message) ? exception.Message : message,
                Except = exception,
            });
            if (memLogEntries.Count > 1000)
                Thread.Sleep(10000);
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

