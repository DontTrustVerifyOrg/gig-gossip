using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Dynamic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using GigDebugLoggerAPIClient;

namespace GigDebugLoggerAPIClient;

struct MemLogEntry
{
    public required string EvType;
    public required string Message;
}

public interface IFlowLogger
{
    bool Enabled { get; set; }
    void WriteToLog(TraceEventType eventType, string message);
}

public class FlowLogger : IFlowLogger
{
    ConcurrentQueue<MemLogEntry> memLogEntries = new();

    Thread writeThread;

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

    public void WriteToLog(TraceEventType eventType, string message)
    {
        if (!Enabled) return;

        memLogEntries.Enqueue(new MemLogEntry
        {
            EvType = eventType.ToString(),
            Message = message,
        });

        while (memLogEntries.Count > 1000)
            Thread.Sleep(250);
    }

}


