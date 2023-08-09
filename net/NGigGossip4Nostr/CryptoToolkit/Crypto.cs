
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using NBitcoin;
using NBitcoin.Secp256k1;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace CryptoToolkit;

[Serializable]
public class SerializedECXOnlyPubKey
{
    public string Value { get; set; }

    public SerializedECXOnlyPubKey(ECXOnlyPubKey pubkey)
    {
        Value = pubkey.AsHex();
    }

    public ECXOnlyPubKey Get()
    {
        return ECXOnlyPubKey.Create(Convert.FromHexString(Value));
    }

    public static implicit operator ECXOnlyPubKey(SerializedECXOnlyPubKey d) => d.Get();
    public static implicit operator SerializedECXOnlyPubKey(ECXOnlyPubKey d) => new SerializedECXOnlyPubKey(d);
}

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
}

public static class Crypto
{
    [Serializable]
    public struct TimedGuidToken
    {
        public DateTime DateTime { get; set; }
        public Guid Guid { get; set; }
        public byte[] Signature { get; set; }
    }

    public static string MakeSignedTimedToken(ECPrivKey ecpriv, DateTime dateTime, Guid guid)
    {
        var tt = new TimedGuidToken();
        tt.DateTime = dateTime;
        tt.Guid = guid;
        tt.Signature = SignObject(tt,ecpriv);
        return Convert.ToBase64String(SerializeObject(tt));
    }

    public static Guid? VerifySignedTimedToken(ECXOnlyPubKey ecpub, string TimedTokenBase64, double seconds)
    {
        var serialized = Convert.FromBase64String(TimedTokenBase64);
        TimedGuidToken timedToken = (TimedGuidToken)DeserializeObject(serialized);
        if ((DateTime.Now - timedToken.DateTime).TotalSeconds > seconds)
            return null;
        var signature = timedToken.Signature;
        timedToken.Signature = null;
        if (!VerifyObject(timedToken, signature, ecpub))
            return null;
        return timedToken.Guid;
    }

    public static byte[] ComputePaymentHash(byte[] preimage)
    {
        Span<byte> buf = stackalloc byte[32];
        var sha256 = System.Security.Cryptography.SHA256.Create();
        sha256.TryComputeHash(preimage, buf, out _);
        return buf.ToArray();
    }

    public static byte[] GenerateRandomPreimage()
    {
        return RandomUtils.GetBytes(32);
    }

    public struct EncryptedData
    {
        public byte[] Data;
        public byte[] IV;
    }

    public static ECPrivKey GeneratECPrivKey()
    {
        return ECPrivKey.Create(RandomUtils.GetBytes(32));
    }

    //from https://github.com/Kukks/NNostr/blob/master/NNostr.Client/Protocols/NIP04.cs
    private static bool TryGetSharedPubkey(this ECXOnlyPubKey ecxOnlyPubKey, ECPrivKey key,
        [NotNullWhen(true)] out ECPubKey? sharedPublicKey)
    {
        // 32 + 1 byte for the compression (0x02) prefix.
        Span<byte> input = stackalloc byte[33];
        input[0] = 0x02;
        ecxOnlyPubKey.WriteToSpan(input.Slice(1));

        bool success = Context.Instance.TryCreatePubKey(input, out var publicKey);
        sharedPublicKey = publicKey?.GetSharedPubkey(key);
        return success;
    }


    public static byte[] EncryptObject(object obj, ECXOnlyPubKey theirXPublicKey, ECPrivKey myPrivKey)
    {
        byte[] attachpubKey = null;
        if (myPrivKey == null)
        {
            myPrivKey = GeneratECPrivKey();
            attachpubKey = myPrivKey.CreateXOnlyPubKey().ToBytes();
        }

        if (!TryGetSharedPubkey(theirXPublicKey, myPrivKey, out var sharedKey))
            throw new CryptographicException("Failed to get a shared key.");

        byte[] encryptionKey = sharedKey.ToBytes().AsSpan(1).ToArray();

        byte[] ret = SymmetricEncrypt(encryptionKey, obj);

        if (attachpubKey != null)
            ret = attachpubKey.Concat(ret).ToArray();
        return ret;
    }

    public static object DecryptObject(byte[] encryptedData, ECPrivKey myPrivKey, ECXOnlyPubKey theirXPublicKey)
    {
        byte[] encryptedX = encryptedData;
        if (theirXPublicKey == null)
        {
            using (MemoryStream ms = new MemoryStream(encryptedData))
            {
                byte[] pub_key_bytes = new byte[32];
                ms.Read(pub_key_bytes, 0, pub_key_bytes.Length);
                encryptedX = new byte[encryptedData.Length - pub_key_bytes.Length];
                ms.Read(encryptedX, 0, encryptedX.Length);
                theirXPublicKey = ECXOnlyPubKey.Create(pub_key_bytes);
            }
        }

        if (!TryGetSharedPubkey(theirXPublicKey, myPrivKey, out var sharedKey))
            throw new CryptographicException("Failed to get a shared key.");

        byte[] decryptionKey = sharedKey.ToBytes().AsSpan(1).ToArray();

        return SymmetricDecrypt(decryptionKey, encryptedX);
    }

    public static byte[] SignObject(object obj, ECPrivKey myPrivKey)
    {
        byte[] serializedObj = SerializeObject(obj);

        Span<byte> buf = stackalloc byte[64];
        using var sha256 = System.Security.Cryptography.SHA256.Create();

        sha256.TryComputeHash(serializedObj, buf, out _);
        myPrivKey.SignBIP340(buf[..32]).WriteToSpan(buf);
        return buf.ToArray();
    }

    public static bool VerifyObject(object obj, byte[] signature, ECXOnlyPubKey theirKey)
    {
        SecpSchnorrSignature sign;
        if (!SecpSchnorrSignature.TryCreate(signature, out sign))
            return false;

        byte[] serializedObj = SerializeObject(obj);

        Span<byte> buf = stackalloc byte[64];
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        sha256.TryComputeHash(serializedObj, buf, out _);

        return theirKey.SigVerifyBIP340(sign, buf);
    }

    public static byte[] ComputeSha256(List<byte[]> items)
    {
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            foreach (var item in items)
            {
                sha256.TransformBlock(item, 0, item.Length, null, 0);
            }

            sha256.TransformFinalBlock(new byte[0], 0, 0);

            return sha256.Hash;
        }
    }

    public static byte[] ComputeSha512(List<byte[]> items)
    {
        using (var sha512 = System.Security.Cryptography.SHA512.Create())
        {
            foreach (var item in items)
            {
                sha512.TransformBlock(item, 0, item.Length, null, 0);
            }

            sha512.TransformFinalBlock(new byte[0], 0, 0);

            return sha512.Hash;
        }
    }

    public static byte[] GenerateSymmetricKey()
    {
        using (Aes aes = Aes.Create())
        {
            aes.GenerateKey();

            return aes.Key;
        }
    }

    public static byte[] SymmetricEncrypt(byte[] key, object obj)
    {
        byte[] serializedObj = SerializeObject(obj);

        using (Aes aes = Aes.Create())
        {
            aes.Key = key;
            aes.GenerateIV();

            using (MemoryStream ms = new MemoryStream())
            {
                ms.Write(aes.IV, 0, aes.IV.Length);

                using (CryptoStream cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    cs.Write(serializedObj, 0, serializedObj.Length);
                    cs.FlushFinalBlock();
                }

                byte[] encryptedObj = ms.ToArray();
                return encryptedObj;
            }
        }
    }

    public static object SymmetricDecrypt(byte[] key, byte[] encryptedObj)
    {
        using (Aes aes = Aes.Create())
        {
            aes.Key = key;

            using (MemoryStream ms = new MemoryStream(encryptedObj))
            {
                byte[] iv = new byte[aes.IV.Length];
                ms.Read(iv, 0, iv.Length);
                aes.IV = iv;
                byte[] encryptedData = new byte[encryptedObj.Length - iv.Length];
                ms.Read(encryptedData, 0, encryptedData.Length);

                using (MemoryStream decryptedMs = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(decryptedMs, aes.CreateDecryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(encryptedData, 0, encryptedData.Length);
                        cs.FlushFinalBlock();
                    }

                    byte[] decryptedObj = decryptedMs.ToArray();
                    object obj = DeserializeObject(decryptedObj);

                    return obj;
                }
            }
        }
    }

#pragma warning disable SYSLIB0011
    public static byte[] SerializeObject(object obj)
    {

        using (MemoryStream ms = new MemoryStream())
        {
            BinaryFormatter formatter = new BinaryFormatter();
            formatter.Serialize(ms, obj);
            return ms.ToArray();
        }
    }

    public static object DeserializeObject(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        {
            BinaryFormatter formatter = new BinaryFormatter();
            object obj = formatter.Deserialize(ms);
            return obj;
        }
    }

}