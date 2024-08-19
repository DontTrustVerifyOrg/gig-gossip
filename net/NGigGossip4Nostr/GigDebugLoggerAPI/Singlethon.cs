using System;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Spectre.Console;
#pragma warning disable 1591

namespace GigDebugLoggerAPI;

public static class Singlethon
{
    public static ConcurrentDictionary<string, FileStream> WriteStreams = new();

    public static string FileName(string rootdir, string pubkey)
    {
        return Path.Combine(rootdir, pubkey + ".log");
    }

    public static string LogFolder;

    public static void SystemLogEvent(string pubkey, System.Diagnostics.TraceEventType eventType, string message)
    {
        var fs = WriteStreams.GetOrAdd(pubkey, (pubkey) => File.Open(FileName(LogFolder, pubkey), FileMode.Append, FileAccess.Write));
        lock (fs)
        {
            var wr = new StreamWriter(fs);
            wr.WriteLine($"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"eventtype\":{(int)eventType},\"body\":{message.Replace("\n", "\\n")}}}");
            wr.Flush();
        }
    }
}

