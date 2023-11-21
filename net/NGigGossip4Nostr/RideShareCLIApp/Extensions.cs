using System;
namespace RideShareCLIApp;


using System;
using System.Diagnostics;
using System.Text;

public static class Extensions
{
    /// from https://github.com/AlexMelw/EasySharp/blob/master/NHelpers/ExceptionsDealing/Extensions/ExceptionExtensions.cs
    /// <summary>
    ///     Gets the entire stack trace consisting of exception's footprints (File, Method, LineNumber)
    /// </summary>
    /// <param name="exception">Source <see cref="Exception" /></param>
    /// <returns>
    ///     <see cref="string" /> that represents the entire stack trace consisting of exception's footprints (File,
    ///     Method, LineNumber)
    /// </returns>
    public static string GetExceptionFootprints(this Exception exception)
    {
        StackTrace stackTrace = new StackTrace(exception, true);
        StackFrame[] frames = stackTrace.GetFrames();

        if (ReferenceEquals(frames, null))
        {
            return string.Empty;
        }

        var traceStringBuilder = new StringBuilder();

        traceStringBuilder.AppendLine(exception.Message);

        for (var i = 0; i < frames.Length; i++)
        {
            StackFrame frame = frames[i];

            if (frame.GetFileLineNumber() < 1)
                continue;

            traceStringBuilder.AppendLine($"File: {frame.GetFileName()}");
            traceStringBuilder.AppendLine($"Method: {frame.GetMethod().Name}");
            traceStringBuilder.AppendLine($"LineNumber: {frame.GetFileLineNumber()}");

            if (i == frames.Length - 1)
                break;

            traceStringBuilder.AppendLine(" ---> ");
        }

        string stackTraceFootprints = traceStringBuilder.ToString();

        if (string.IsNullOrWhiteSpace(stackTraceFootprints))
            return "NO DETECTED FOOTPRINTS";

        return stackTraceFootprints + "\r\n" + exception.StackTrace;
    }

}
