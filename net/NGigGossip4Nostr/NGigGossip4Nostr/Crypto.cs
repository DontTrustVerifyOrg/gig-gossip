using System;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NBitcoin.Secp256k1;
using NBitcoin;
using NBitcoin.Protocol;
using System.Diagnostics.CodeAnalysis;
using NNostr.Client.Crypto;
using NBitcoin.Crypto;
using System.Runtime.Serialization;
using NNostr.Client;

namespace NGigGossip4Nostr
{

    public static class Crypto
	{
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
            if(myPrivKey==null)
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

        public class ECXOnlyPubKeySurrogate : ISerializationSurrogate
        {
            public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
            {
                var key = (ECXOnlyPubKey)obj;
                info.AddValue("XOnlyPubKey", key.ToHex());
            }

            public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector? selector)
            {
                return ECXOnlyPubKey.Create(Convert.FromHexString(info.GetString("XOnlyPubKey")));
            }
        }

#pragma warning disable SYSLIB0011
        public static byte[] SerializeObject(object obj)
        {
            //Configure our surrogate selectors.
            var surrogateSelector = new SurrogateSelector();
            surrogateSelector.AddSurrogate(typeof(ECXOnlyPubKey), new StreamingContext(StreamingContextStates.All),
                                           new ECXOnlyPubKeySurrogate());

            using (MemoryStream ms = new MemoryStream())
            {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.SurrogateSelector = surrogateSelector;
                formatter.Serialize(ms, obj);

                return ms.ToArray();
            }
        }

        public static object DeserializeObject(byte[] data)
        {
            using (MemoryStream ms = new MemoryStream(data))
            {
                var surrogateSelector = new SurrogateSelector();
                surrogateSelector.AddSurrogate(typeof(ECXOnlyPubKey), new StreamingContext(StreamingContextStates.All),
                                               new ECXOnlyPubKeySurrogate());

                BinaryFormatter formatter = new BinaryFormatter();
                formatter.SurrogateSelector = surrogateSelector;
                object obj = formatter.Deserialize(ms);

                return obj;
            }
        }

    }
}


   