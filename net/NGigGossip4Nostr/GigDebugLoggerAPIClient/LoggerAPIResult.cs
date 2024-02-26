using System;
namespace GigDebugLoggerAPIClient;

public enum GigDebugLoggerAPIErrorCode
{
    Ok = 0,
    InvalidApiKey = 1,
    OperationFailed = 2,
}

[Serializable]
public class GigDebugLoggerAPIException : Exception
{
    /// <summary>Represents the error code of the exception.</summary>
    public GigDebugLoggerAPIErrorCode ErrorCode { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GigGossipSettlerAPIErrorCode"/> class with a specified error code and a detailed description.
    /// </summary>
    /// <param name="loggerErrorCode">The error code for exception.</param>
    /// <param name="message">The detail message that describes the current exception.</param>
    public GigDebugLoggerAPIException(GigDebugLoggerAPIErrorCode loggerErrorCode, string message) : base(message)
    {
        ErrorCode = loggerErrorCode;
    }
}

public static class LoggerAPIResult
{
    public static void Check(dynamic t)
    {
        if ((int)t.ErrorCode != (int)GigDebugLoggerAPIErrorCode.Ok)
            throw new GigDebugLoggerAPIException((GigDebugLoggerAPIErrorCode)((int)t.ErrorCode), t.ErrorMessage);
    }

    public static T Get<T>(dynamic t)
    {
        Check(t);
        return t.Value;
    }
}

