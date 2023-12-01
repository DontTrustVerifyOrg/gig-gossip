using System;
namespace GigGossipSettlerAPIClient;

public enum GigGossipSettlerAPIErrorCode
{
    /// <summary>
    /// Represents successful operation.
    /// </summary>
    Ok = 0,
    /// <summary>
    /// Represents invalid or expired authentication token.
    /// </summary>
    InvalidToken = 1,
    /// <summary>
    /// Property was not granted to the subject.
    /// </summary>
    PropertyNotGranted = 2,
    /// <summary>
    /// Unknown certificate was detected.
    /// </summary>
    UnknownCertificate = 3,
    /// <summary>
    /// Unknown preimage was detected.
    /// </summary>
    UnknownPreimage = 4
}

[Serializable]
public class GigGossipSettlerAPIException : Exception
{
    /// <summary>Represents the error code of the exception.</summary>
    public GigGossipSettlerAPIErrorCode ErrorCode { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GigGossipSettlerAPIErrorCode"/> class with a specified error code and a detailed description.
    /// </summary>
    /// <param name="settlerErrorCode">The error code for exception.</param>
    /// <param name="message">The detail message that describes the current exception.</param>
    public GigGossipSettlerAPIException(GigGossipSettlerAPIErrorCode settlerErrorCode, string message) : base(message)
    {
        ErrorCode = settlerErrorCode;
    }
}

public static class SettlerAPIResult
{
    public static void Check(dynamic t)
    {
        if ((int)t.ErrorCode != (int)GigGossipSettlerAPIErrorCode.Ok)
            throw new GigGossipSettlerAPIException((GigGossipSettlerAPIErrorCode)((int)t.ErrorCode), t.ErrorMessage);
    }

    public static T Get<T>(dynamic t)
    {
        Check(t);
        return t.Value;
    }
}

