using System;
using System.Threading.Tasks;
using NBitcoin.Secp256k1;

namespace GigGossip;


/// <summary>
/// This static class contains extension methods for working with hexadecimal values.
/// </summary>
public static class HexExtensions
{
    public static string AsHex(this ECPrivKey key)
    {
        Span<byte> span = stackalloc byte[32];
        key.WriteToSpan(span);
        return span.AsHex();
    }
    public static string AsHex(this Span<byte> bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
    public static string AsHex(this byte[] bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
    public static string AsHex(this ECXOnlyPubKey key)
    {
        return key.ToBytes().AsSpan().AsHex();
    }
    public static ECPrivKey AsECPrivKey(this string key)
    {
        return Context.Instance.CreateECPrivKey(Convert.FromHexString(key));
    }
    public static ECXOnlyPubKey AsECXOnlyPubKey(this string key)
    {
        return Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(key));
    }
    public static byte[] AsBytes(this string data)
    {
        return Convert.FromHexString(data);
    }
}

