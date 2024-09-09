using System;
namespace RideShareCLIApp;


using System;
using System.Diagnostics;
using System.Text;
using NGeoHash;
using GigGossip;

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

    //geospheric distance in kilometers
    //https://stackoverflow.com/a/51839058
    public static double Distance(double longitude, double latitude, double otherLongitude, double otherLatitude)
    {
        var d1 = latitude * (Math.PI / 180.0);
        var num1 = longitude * (Math.PI / 180.0);
        var d2 = otherLatitude * (Math.PI / 180.0);
        var num2 = otherLongitude * (Math.PI / 180.0) - num1;
        var d3 = Math.Pow(Math.Sin((d2 - d1) / 2.0), 2.0) + Math.Cos(d1) * Math.Cos(d2) * Math.Pow(Math.Sin(num2 / 2.0), 2.0);

        return 6376500.0 * (2.0 * Math.Atan2(Math.Sqrt(d3), Math.Sqrt(1.0 - d3)))/1000.0;
    }

    public static double Distance(this GeoLocation x, GeoLocation y)
    {
        return Distance(x.Latitude, x.Longitude, y.Latitude, y.Longitude);
    }

    public static double Distance(this Coordinates x, Coordinates y)
    {
        return Distance(x.Lat, x.Lon, y.Lat, y.Lon);
    }
}
