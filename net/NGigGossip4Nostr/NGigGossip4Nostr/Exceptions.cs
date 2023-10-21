using System;
namespace NGigGossip4Nostr;


public enum GigGossipNodeErrorCode
{
    Ok = 0,
    SelfConnection = 1,
    FrameTypeNotRegistered = 2,
}

public static class Extensions
{
    static string[] gigGossipErrorMesssages = new string[]{
        "",
        "Cannot connect node to itself",
        "This frame type is not registered",
    };
    public static string Message(this GigGossipNodeErrorCode errorCode)
    {
        return gigGossipErrorMesssages[(int)errorCode];
    }
}

[Serializable]
public class GigGossipException : Exception
{
    GigGossipNodeErrorCode ErrorCode { get; set; }
    public GigGossipException(GigGossipNodeErrorCode gigGossipErrorCode) : base(gigGossipErrorCode.Message())
    {
        ErrorCode = gigGossipErrorCode;
    }
    public GigGossipException(GigGossipNodeErrorCode gigGossipErrorCode, string message) : base(message)
    {
        ErrorCode = gigGossipErrorCode;
    }
}

