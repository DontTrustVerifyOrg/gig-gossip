using System;
using CryptoToolkit;

namespace NGigGossip4Nostr
{
    public static class FlowLogger
    {
        class LogLine
        {
            public DateTime Moment { get; set; }
            public string Line { get; set; }
        }

        static List<LogLine> logLines = new();

        private static void FlushLogLines()
        {
            using (var w = new StreamWriter(thefileName, false))
            {
                foreach (var l in logLines.OrderBy((a) => a.Moment))
                    w.WriteLine(l.Line);
            }
        }

        private static void WriteLine(string str)
        {
            lock (logLines)
            {
                logLines.Add(new LogLine() { Line = str, Moment = DateTime.UtcNow });
                FlushLogLines();
            }
        }

        public static void Start(string fileName)
        {
            thefileName = fileName;
            WriteLine("```mermaid");
            WriteLine("sequenceDiagram");
            WriteLine("\tautonumber");
        }

        public static void Stop()
        {
            if (thefileName == null)
                return;
            WriteLine("```");
        }
        static Dictionary<string, string> participantAliases = new();
        static string thefileName = null;

        static Dictionary<string, int> autoAliasClassCounters = new();
        static Dictionary<string, string> autoAlias = new();

        public static string AutoAlias(string id,string cls)
        {
            lock(autoAliasClassCounters)
            {
                if (!autoAlias.ContainsKey(id))
                {
                    if(!autoAliasClassCounters.ContainsKey(cls))
                        autoAliasClassCounters[cls] = 1;
                    else
                        autoAliasClassCounters[cls] += 1;
                    autoAlias[id] = cls + "" + autoAliasClassCounters[cls];
                }
                return autoAlias[id];
            }
        }

        public static void SetupParticipantWithAutoAlias(string id, string cls, bool isActor)
        {
            SetupParticipant(id, AutoAlias(id, cls), isActor);
        }

        public static void SetupParticipant(string id, string alias, bool isActor )
        {
            if (thefileName == null)
                return;
            lock (logLines)
            {
                participantAliases[id] = alias;
                if (isActor)
                    WriteLine("\tactor " + id + " as " + alias);
                else
                    WriteLine("\tparticipant " + id + " as " + alias);
            }
        }

        public static void NewMessage(string a, string b, string message)
        {
            if (thefileName == null)
                return;
            if (!participantAliases.ContainsKey(a))
                return;
            if (!participantAliases.ContainsKey(b))
                return;
            WriteLine("\t" + a + "->>" + b + ": " + message);
        }

        public static void NewReply(string a, string b, string message)
        {
            if (thefileName == null)
                return;
            if (!participantAliases.ContainsKey(a))
                return;
            if (!participantAliases.ContainsKey(b))
                return;
            WriteLine("\t" + a + "-->>" + b + ": " + message);
        }

        public static void NewConnected(string a, string b, string message)
        {
            if (thefileName == null)
                return;
            if (!participantAliases.ContainsKey(a))
                return;
            if (!participantAliases.ContainsKey(b))
                return;
            WriteLine("\t" + a + "--)" + b + ": " + message);
        }
        public static void NewEvent(string a,string message)
        {
            if (thefileName == null)
                return;
            if (!participantAliases.ContainsKey(a))
                return;
            WriteLine("\t Note over " + a + ": " + message);
        }

    }
}

