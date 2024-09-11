using System;
using NBitcoin;
using NBitcoin.Secp256k1;

namespace GigGossip;


public static class ProtoBufExtensions
{

    //from https://github.com/rmandvikar/csharp-extensions/blob/dev/src/rm.Extensions/GuidExtension.cs
    public static UUID AsUUID(this Guid guid)
    {
        var bytes = guid.ToByteArray();
        TweakOrderOfGuidBytesToMatchStringRepresentation(bytes);
        return new UUID { Value = Google.Protobuf.ByteString.CopyFrom(bytes) };
    }

    //from https://github.com/rmandvikar/csharp-extensions/blob/dev/src/rm.Extensions/GuidExtension.cs
    public static Guid AsGuid(this UUID uuid)
    {
        if (uuid.Value.Length != 16)
        {
            throw new ArgumentException("Length should be 16.", "uuid");
        }
        var bytes = uuid.Value.ToArray();
        TweakOrderOfGuidBytesToMatchStringRepresentation(bytes);
        return new Guid(bytes);
    }

    public static Uri AsUri(this URI uri)
    {
        return new Uri(uri.Value);
    }

    public static URI AsURI(this Uri uri)
    {
        return new URI { Value = uri.AbsoluteUri };
    }

    public static Timestamp AsUnixTimestamp(this DateTime dateTime)
    {
        return new Timestamp { Value = new DateTimeOffset(dateTime).ToUnixTimeSeconds() };
    }

    public static DateTime AsUtcDateTime(this Timestamp unixTimestamp)
    {
        return DateTimeOffset.FromUnixTimeSeconds(unixTimestamp.Value).UtcDateTime;
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

    public static PublicKey AsPublicKey(this ECXOnlyPubKey pubKey)
    {
        return new PublicKey { Value = pubKey.ToBytes().AsByteString() };
    }

    public static ECXOnlyPubKey AsECXOnlyPubKey(this PublicKey pubkey)
    {
        return ECXOnlyPubKey.Create(pubkey.Value.ToArray());
    }

    public static string AsHex(this PublicKey pubkey)
    {
        return pubkey.Value.ToArray().AsHex();
    }

    public static PublicKey AsPublicKey(this string pubkey)
    {
        return new PublicKey { Value = pubkey.AsECXOnlyPubKey().ToBytes().AsByteString() };
    }

    public static Signature Sign<T>(this T message, ECPrivKey ecpriv) where T : Google.Protobuf.IMessage<T>
    {
        return new Signature { Value = Crypto.SignObject(message, ecpriv).AsByteString() };
    }

    public static bool Verify<T>(this T message, Signature signature, ECXOnlyPubKey pubkey) where T : Google.Protobuf.IMessage<T>
    {
        return Crypto.VerifyObject(message, signature.Value.ToArray(), pubkey);
    }

    public static T Decrypt<T>(this EncryptedData data, ECPrivKey ecpriv, ECXOnlyPubKey theirpubkey = null) where T : Google.Protobuf.IMessage<T>, new()
    {
        return Crypto.DecryptObject<T>(data.Value.ToArray(), ecpriv, theirpubkey); ;
    }

    public static EncryptedData Encrypt<T>(this T obj, ECXOnlyPubKey theirpubkey, ECPrivKey ecpriv = null) where T : Google.Protobuf.IMessage<T>
    {
        return new EncryptedData { Value = Crypto.EncryptObject(obj, theirpubkey, ecpriv).AsByteString() };
    }

    public static T Decrypt<T>(this EncryptedData data, byte[] symmetrickey) where T : Google.Protobuf.IMessage<T>, new()
    {
        return Crypto.SymmetricObjectDecrypt<T>(symmetrickey, data.Value.ToArray());
    }

    public static EncryptedData Encrypt<T>(this T obj, byte[] symmetrickey) where T : Google.Protobuf.IMessage<T>
    {
        return new EncryptedData { Value = Crypto.SymmetricObjectEncrypt(symmetrickey, obj).AsByteString() };
    }

}

