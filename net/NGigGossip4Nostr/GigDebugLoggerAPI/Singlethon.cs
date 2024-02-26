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

    public static void SystemLogEvent(string pubkey, System.Diagnostics.TraceEventType eventType, string message, string exception)
    {
        var fs = WriteStreams.GetOrAdd(pubkey, (pubkey) => File.Open(FileName(LogFolder, pubkey), FileMode.Append, FileAccess.Write));
        lock (fs)
        {
            var str = Newtonsoft.Json.JsonConvert.SerializeObject(new SystemLogEntry
            {
                EntryId = Guid.NewGuid(),
                PublicKey = pubkey,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                EventType = eventType,
                Message = message,
                Exception = exception,
            });
            var wr = new StreamWriter(fs);
            wr.WriteLine(str);
            wr.Flush();
            fs.WriteByte(0);
            fs.WriteByte(0);
            fs.WriteByte(0);
            fs.WriteByte(0);
            fs.Flush();
        }
    }
}

