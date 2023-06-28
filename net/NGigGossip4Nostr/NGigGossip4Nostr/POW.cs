using System;
namespace NGigGossip4Nostr;

using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Threading.Tasks;

public class ProofOfWork
{
    public string PowScheme { get; set; }
    public long PowTarget { get; set; }
    public int Nuance { get; set; }

    public bool Validate<T>(T obj)
    {
        if (PowTarget == 0)
        {
            return true;
        }
        if (PowScheme.ToLower() == "sha256")
        {
            var buf = ToBytes(obj);
            return ValidateSHA256Pow(buf, Nuance, PowTarget);
        }
        return false;
    }

#pragma warning disable SYSLIB0011

    private byte[] ToBytes<T>(T obj)
    {
        using (var stream = new MemoryStream())
        {
            var formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
            formatter.Serialize(stream, obj);
            return stream.ToArray();
        }
    }

    private bool ValidateSHA256Pow(byte[] buf, int nuance, long powTarget)
    {
        using (var sha256 = SHA256.Create())
        {
            byte[] hash = null;
            for (int i = 0; i <= nuance; i++)
            {
                hash = sha256.ComputeHash(buf.Concat(BitConverter.GetBytes(i)).ToArray());
            }
            var hashNumber = BitConverter.ToInt64(hash.Reverse().ToArray());
            return hashNumber <= powTarget;
        }
    }

    public static long PowTargetFromComplexity(string powScheme, int complexity)
    {
        if (complexity == 0)
        {
            return 0;
        }
        if (powScheme.ToLower() == "sha256")
        {
            return long.MaxValue / complexity;
        }
        throw new NotImplementedException();
    }
}

public class WorkRequest
{
    public string PowScheme { get; set; }
    public long PowTarget { get; set; }

    public ProofOfWork ComputeProof<T>(T obj)
    {
        return new ProofOfWork
        {
            PowScheme = PowScheme,
            PowTarget = PowTarget,
            Nuance = ComputePow(obj),
        };
    }

    private int ComputePow<T>(T obj)
    {
        if (PowTarget == 0)
        {
            return 0;
        }
        if (PowScheme.ToLower() == "sha256")
        {
            var buf = ToBytes(obj);
            return Enumerable.Range(0, int.MaxValue)
                .FirstOrDefault(nuance => ValidateSHA256Pow(buf, nuance, PowTarget));
        }
        return 0;
    }

    private byte[] ToBytes<T>(T obj)
    {
        using (var stream = new MemoryStream())
        {
            var formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
            formatter.Serialize(stream, obj);
            return stream.ToArray();
        }
    }

    private bool ValidateSHA256Pow(byte[] buf, int nuance, long powTarget)
    {
        using (var sha256 = SHA256.Create())
        {
            byte[] hash = null;
            for (int i = 0; i <= nuance; i++)
            {
                hash = sha256.ComputeHash(buf.Concat(BitConverter.GetBytes(i)).ToArray());
            }
            var hashNumber = BitConverter.ToInt64(hash.Reverse().ToArray());
            return hashNumber <= powTarget;
        }
    }
}

