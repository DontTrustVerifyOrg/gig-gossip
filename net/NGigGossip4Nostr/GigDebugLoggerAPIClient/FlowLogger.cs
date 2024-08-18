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
    public required Exception Except;
}

public interface IFlowLogger
{
    bool Enabled { get; set; }
    void TraceInformation(string? message);
    void TraceWarning(string? message);
    void TraceError(string? message);
    void TraceException(Exception exception, string? message=null);
    void NewMessage(string a, string b, string message);
    void NewReply(string a, string b, string message);
    void NewConnected(string a, string b, string message);
    void NewNote(string a, string message);
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

    public void WriteToLog( System.Diagnostics.TraceEventType eventType, string message)
    {
        if (!Enabled) return;

        memLogEntries.Enqueue(new MemLogEntry
        {
            EvType = eventType.ToString(),
            Message = message,
            Except = null
        });

        while (memLogEntries.Count > 1000)
            Thread.Sleep(250);
    }

    public void WriteException(System.Diagnostics.TraceEventType eventType, Exception exception, string? message = null)
    {
        if (!Enabled) return;

        memLogEntries.Enqueue(new MemLogEntry
        {
            EvType = eventType.ToString(),
            Message = string.IsNullOrWhiteSpace(message) ? exception.Message : message,
            Except = exception,
        });

        while (memLogEntries.Count > 1000)
            Thread.Sleep(250);
    }

    public void TraceInformation(string? message)
    {
        if (!Enabled) return;
        WriteToLog(System.Diagnostics.TraceEventType.Information, message);
    }

    public void TraceWarning(string? message)
    {
        if (!Enabled) return;
        WriteToLog(System.Diagnostics.TraceEventType.Warning, message);
    }

    public void TraceError(string? message)
    {
        if (!Enabled) return;
        WriteToLog(System.Diagnostics.TraceEventType.Error, message);
    }

    public void TraceException(Exception exception, string? message = null)
    {
        if (!Enabled) return;
        WriteException(System.Diagnostics.TraceEventType.Critical, exception, message);
    }

    public void NewMessage(string a, string b, string message)
    {
        if (!Enabled) return;
        WriteToLog(
                System.Diagnostics.TraceEventType.Transfer,
                "\t" + a + "->>" + b + ": " + message);
    }

    public void NewReply(string a, string b, string message)
    {
        if (!Enabled) return;
        WriteToLog(
                System.Diagnostics.TraceEventType.Transfer,
            "\t" + a + "-->>" + b + ": " + message);
    }

    public void NewConnected(string a, string b, string message)
    {
        if (!Enabled) return;
        WriteToLog(
                System.Diagnostics.TraceEventType.Transfer,
            "\t" + a + "--)" + b + ": " + message);
    }

    public void NewNote(string a, string message)
    {
        if (!Enabled) return;
        WriteToLog(
                System.Diagnostics.TraceEventType.Transfer,
                "\t Note over " + a + ": " + message);
    }

}


