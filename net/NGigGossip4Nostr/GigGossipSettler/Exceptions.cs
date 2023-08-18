using System;
namespace GigGossipSettler;

public enum SettlerErrorCode { Ok = 0, InvalidToken = 1, PropertyNotGranted = 2, UnknownCertificate = 3, UnknownPreimage = 4 }
public static class SettlerErrorCodeExtensions
{
    static string[] errorMesssages = new string[]{
        "",
        "Invalid authtoken",
        "Property is not granted to the subject",
        "Unknown certificate",
        "Unknown preimage",
    };
    public static string Message(this SettlerErrorCode errorCode)
    {
        return errorMesssages[(int)errorCode];
    }
}

[Serializable]
public class SettlerException : Exception
{
    SettlerErrorCode ErrorCode { get; set; }
    public SettlerException(SettlerErrorCode settlerErrorCode) : base(settlerErrorCode.Message())
    {
        ErrorCode = settlerErrorCode;
    }
    public SettlerException(SettlerErrorCode settlerErrorCode, string message) : base(message)
    {
        ErrorCode = settlerErrorCode;
    }
}
