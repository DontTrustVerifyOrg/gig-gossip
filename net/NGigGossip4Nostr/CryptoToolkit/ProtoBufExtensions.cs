using System;
namespace CryptoToolkit;

public static class ProtoBufExtensions
{

    //from https://github.com/rmandvikar/csharp-extensions/blob/dev/src/rm.Extensions/GuidExtension.cs
    public static Google.Protobuf.ByteString AsUUID(this Guid guid)
    {
        var bytes = guid.ToByteArray();
        TweakOrderOfGuidBytesToMatchStringRepresentation(bytes);
        return Google.Protobuf.ByteString.CopyFrom(bytes) ;
    }

    //from https://github.com/rmandvikar/csharp-extensions/blob/dev/src/rm.Extensions/GuidExtension.cs
    public static Guid AsGuid(this Google.Protobuf.ByteString uuid)
    {
        if (uuid.Length != 16)
        {
            throw new ArgumentException("Length should be 16.", "uuid");
        }
        var bytes = uuid.ToArray();
        TweakOrderOfGuidBytesToMatchStringRepresentation(bytes);
        return new Guid(bytes);
    }

    public static long AsUnixTimestamp(this DateTime dateTime)
    {
        return new DateTimeOffset(dateTime).ToUnixTimeSeconds();
    }

    public static DateTime AsUtcDateTime(this long unixTimestamp)
    {
        return DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).UtcDateTime;
    }

    public static Google.Protobuf.ByteString AsByteString(this byte[] bytes)
    {
        return Google.Protobuf.ByteString.CopyFrom(bytes);
    }

    public static Google.Protobuf.ByteString AsByteString(this Span<byte> bytes)
    {
        return Google.Protobuf.ByteString.CopyFrom(bytes);
    }

    //from https://github.com/rmandvikar/csharp-extensions/blob/dev/src/rm.Extensions/GuidExtension.cs
    private static void TweakOrderOfGuidBytesToMatchStringRepresentation(byte[] guidBytes)
    {
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(guidBytes, 0, 4);
            Array.Reverse(guidBytes, 4, 2);
            Array.Reverse(guidBytes, 6, 2);
        }
    }

}

