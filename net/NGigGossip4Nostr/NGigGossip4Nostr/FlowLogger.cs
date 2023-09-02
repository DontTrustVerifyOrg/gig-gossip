using System;
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
                logLines.Add(new LogLine() { Line = str, Moment = DateTime.Now });
                FlushLogLines();
            }
        }

        public static void Start(string fileName)
        {
            thefileName = fileName;
            WriteLine("```mermaid");
            WriteLine("sequenceDiagram");
        }

        public static void Stop()
        {
            if (thefileName == null)
                return;
            WriteLine("```");
        }
        static Dictionary<string, string> participantAliases = new();
        static string thefileName = null;


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

    }
}

