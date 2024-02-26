using System;

namespace GigDebugLoggerAPI;

public enum LoggerErrorCode
{
    Ok = 0,
    InvalidApiKey = 1,
    OperationFailed = 2,
}

public static class LoggerErrorCodeExtensions
{
    static string[] errorMesssages = new string[]{
        "",
        "Invalid api key",
        "Operation failed",
    };

    /// <summary>
    /// This method returns the message associated with the errorCode passed as parameter.
    /// </summary>
    /// <param name="errorCode">The type of error occured.</param>
    /// <returns>A string message describing the error</returns>
    public static string Message(this LoggerErrorCode errorCode)
    {
        return errorMesssages[(int)errorCode];
    }
}

/// <summary>
/// Custom exception class for Settler related exceptions.
/// </summary>
[Serializable]
public class LoggerException : Exception
{
    public LoggerErrorCode ErrorCode { get; set; }

    public LoggerException(LoggerErrorCode settlerErrorCode) : base(settlerErrorCode.Message())
    {
        ErrorCode = settlerErrorCode;
    }

    public LoggerException(LoggerErrorCode settlerErrorCode, string message) : base(message)
    {
        ErrorCode = settlerErrorCode;
    }
}
