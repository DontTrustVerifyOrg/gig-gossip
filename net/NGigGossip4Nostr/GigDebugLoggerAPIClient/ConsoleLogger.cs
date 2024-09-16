using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Dynamic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace GigDebugLoggerAPIClient;

public class ConsoleLogger : IFlowLogger
{

    public bool Enabled { get; set; } = false;

    ConcurrentQueue<MemLogEntry> memLogEntries = new();

    Thread writeThread;

    public ConsoleLogger()
    {
    }

    public void Initialize(bool traceEnabled)
    {
        this.Enabled = traceEnabled;
        writeThread = new(async () =>
        {
            while (true)
            {
                while (memLogEntries.TryDequeue(out var entry))
                    PrintEntry(entry);
                Thread.Sleep(250);
            }
        });
        writeThread.Start();
    }

    // Initialize dictionaries and sets
    Dictionary<string, string> evidtoname = new Dictionary<string, string>();
    HashSet<string> funcfilter = new HashSet<string>();

    public void WriteToLog(TraceEventType eventType, string message)
    {
        if (!Enabled)
            return;

        memLogEntries.Enqueue(new MemLogEntry
        {
            EvType = eventType.ToString(),
            Message = message,
        });

        if (memLogEntries.Count > 1000)
            while (memLogEntries.Count > 100)
                Thread.Sleep(250);
    }


    private void PrintEntry(MemLogEntry entry)
    {

        lock (this)
        {
            var writeArgs = true;
            var lineOutput = $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"eventtype\":{(int)Enum.Parse<TraceEventType>(entry.EvType)},\"body\":{entry.Message.Replace("\n", "\\n")}}}";

            try
            {

                var doc = JObject.Parse(lineOutput);
                var mdoc = doc["body"];
                DateTime tim = DateTimeOffset.FromUnixTimeMilliseconds((long)doc["timestamp"]).DateTime;

                if ((int)doc["eventtype"] != 4096)
                {
                    if (mdoc != null)
                    {
                        string kind = (string)mdoc["kind"];
                        string id = mdoc["id"].ToString();

                        if (kind == "call")
                        {
                            string cname = $"{mdoc["type"]}.{mdoc["method"]}";
                            evidtoname[id] = cname;
                            if (!funcfilter.Contains(cname))
                            {
                                Console.WriteLine($"{tim} > {cname}");
                            }
                        }
                        else
                        {
                            if (evidtoname.ContainsKey(id))
                            {
                                string cname = evidtoname[id];
                                if (!funcfilter.Contains(cname))
                                {
                                    switch (kind)
                                    {
                                        case "args":
                                            var argsVal = mdoc["args"];
                                            if (writeArgs && argsVal != null && argsVal.HasValues)
                                            {
                                                Console.WriteLine($"{tim}   + {cname} {argsVal}");
                                            }
                                            break;
                                        case "retval":
                                            Console.WriteLine($"{tim}   < {cname} {mdoc["value"]}");
                                            break;
                                        case "return":
                                            Console.WriteLine($"{tim} <<<< {cname}");
                                            break;
                                        case "iteration":
                                            Console.WriteLine($"{tim}   * {cname} {mdoc["value"]}");
                                            break;
                                        case "exception":
                                            Console.WriteLine($"{tim}   ! {cname} {mdoc["exception"]}");
                                            break;
                                        case "info":
                                            Console.WriteLine($"{tim} (i) {cname} {mdoc["message"]}");
                                            break;
                                        case "warning":
                                            Console.WriteLine($"{tim} (w) {cname} {mdoc["message"]}");
                                            break;
                                        case "error":
                                            Console.WriteLine($"{tim} (e) {cname} {mdoc["message"]}");
                                            break;
                                        default:
                                            Console.WriteLine(lineOutput);
                                            break;
                                    }
                                }
                            }
                            else
                            {
                                Console.WriteLine(lineOutput);
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine(lineOutput);
                    }
                }
                else
                {
                    if (mdoc != null)
                    {
                        string id = mdoc["id"].ToString();
                        if (evidtoname.ContainsKey(id))
                        {
                            string cname = evidtoname[id];
                            Console.WriteLine($"{tim} (t) {cname} {mdoc["message"]}");
                        }
                        else
                        {
                            Console.WriteLine(lineOutput);
                        }
                    }
                    else
                    {
                        Console.WriteLine(lineOutput);
                    }
                }
            }
            catch (Exception)
            {
                Console.WriteLine(lineOutput);
            }
        }
    }
}


