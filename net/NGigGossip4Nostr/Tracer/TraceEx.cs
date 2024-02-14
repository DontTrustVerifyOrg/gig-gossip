using System.Diagnostics;
using System.Globalization;
using System.Text;
using Spectre.Console;

namespace TraceExColor;

public static class TraceEx
{
    static Dictionary<TraceEventType, string> pfxcol = new()
    {
        { TraceEventType.Critical,"red" },
        { TraceEventType.Error,"red" },
        { TraceEventType.Warning,"orange1" },
        { TraceEventType.Information,"white" },
        { TraceEventType.Verbose,"gray" },
        { TraceEventType.Start,"green" },
        { TraceEventType.Stop,"red" },
        { TraceEventType.Suspend,"orange1" },
        { TraceEventType.Resume,"blue" },
        { TraceEventType.Transfer,"blue" },
    };

    private static void TraceEvent(TraceEventType eventType, string? message)
    {
        WriteLine($"[[gray]]{DateTime.Now.ToString("hh:mm:ss.fff", CultureInfo.InvariantCulture)}[[/]] [[{pfxcol[eventType]}]]{message}[[/]]");
    }

    private static void Write(string? message)
    {
        if (message == null)
            return;
        AnsiConsole.Markup(message.Replace("[", "[[").Replace("]", "]]").Replace("[[[[", "[").Replace("]]]]", "]"));
    }

    private static void WriteLine(string? message)
    {
        if (message == null)
            AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(message.Replace("[", "[[").Replace("]", "]]").Replace("[[[[", "[").Replace("]]]]", "]"));
    }

    public static void TraceInformation(string? message)
    {
        TraceEvent(TraceEventType.Information, message);
    }

    public static void TraceWarning(string? message)
    {
        TraceEvent(TraceEventType.Warning, message);
    }

    public static void TraceError(string? message)
    {
        TraceEvent(TraceEventType.Error, message);
    }

    public static void TraceException(Exception exception)
    {
        Write($"[[gray]]{DateTime.Now.ToString("hh:mm:ss.fff", CultureInfo.InvariantCulture)}[[/]] ");
        AnsiConsole.WriteException(exception);
    }
}

