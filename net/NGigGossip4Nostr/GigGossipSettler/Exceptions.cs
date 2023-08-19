using System;
namespace GigGossipSettler;

public enum SettlerErrorCode { 
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

public static class SettlerErrorCodeExtensions
{
    static string[] errorMesssages = new string[]{
        "",
        "Invalid authtoken",
        "Property is not granted to the subject",
        "Unknown certificate",
        "Unknown preimage",
    };

    /// <summary>
    /// This method returns the message associated with the errorCode passed as parameter.
    /// </summary>
    /// <param name="errorCode">The type of error occured.</param>
    /// <returns>A string message describing the error</returns>
    public static string Message(this SettlerErrorCode errorCode)
    {
        return errorMesssages[(int)errorCode];
    }
}

/// <summary>
/// Custom exception class for Settler related exceptions.
/// </summary>
[Serializable]
public class SettlerException : Exception
{
    /// <summary>
    /// Gets and sets <see cref="SettlerErrorCode"/> associated with this exception.
    /// </summary>
    SettlerErrorCode ErrorCode { get; set; }

    /// <summary>
    /// Initializes a new instance of SettlerException with specified error code.
    /// </summary>
    /// <param name="settlerErrorCode">The error code associated with this exception.</param>
   public SettlerException(SettlerErrorCode settlerErrorCode) : base(settlerErrorCode.Message())
    {
        ErrorCode = settlerErrorCode;
    }
 
    /// <summary>
    /// Initializes a new instance of SettlerException with specified error code and custom message.
    /// </summary>
    /// <param name="settlerErrorCode">The error code associated with this exception.</param>
    /// <param name="message">A custom message about the exception.</param>
    public SettlerException(SettlerErrorCode settlerErrorCode, string message) : base(message)
    {
        ErrorCode = settlerErrorCode;
    }
}
