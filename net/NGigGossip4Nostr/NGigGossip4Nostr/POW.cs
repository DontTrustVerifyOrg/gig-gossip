using System;
namespace NGigGossip4Nostr;

using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Numerics;

[Serializable]
public class ProofOfWork
{
    public static BigInteger MaxPowTargetSha256 = BigInteger.Parse("0FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", System.Globalization.NumberStyles.AllowHexSpecifier);

    public static BigInteger PowTargetFromComplexity(string powScheme, int complexity)
    {
        if (complexity == 0)
        {
            return 0;
        }
        if (powScheme.ToLower() == "sha256")
        {
            return MaxPowTargetSha256 / complexity;
        }
        throw new NotImplementedException();
    }

    public string PowScheme { get; set; }
    public BigInteger PowTarget { get; set; }
    public int Nuance { get; set; }

    public bool Validate(object obj)
    {
        if (PowTarget == 0)
        {
            return true;
        }
        if (PowScheme.ToLower() == "sha256")
        {
            var buf = Crypto.SerializeObject(obj);
            return ValidateSHA256Pow(buf, Nuance, PowTarget);
        }
        return false;
    }

    public static bool ValidateSHA256Pow(byte[] buf, int nuance, BigInteger powTarget)
    {
        var sc = new BigInteger(Crypto.ComputeSha256(new List<byte[]>() { buf, BitConverter.GetBytes(nuance) }), isUnsigned: true, isBigEndian: false);
        return  sc <= powTarget;
    }

}

[Serializable]
public class WorkRequest
{
    public string PowScheme { get; set; }
    public BigInteger PowTarget { get; set; }

    public ProofOfWork ComputeProof(object obj)
    {
        return new ProofOfWork
        {
            PowScheme = PowScheme,
            PowTarget = PowTarget,
            Nuance = ComputePow(obj),
        };
    }

    private int ComputePow(object obj)
    {
        if (PowTarget == 0)
        {
            return 0;
        }
        if (PowScheme.ToLower() == "sha256")
        {
            var buf = Crypto.SerializeObject(obj);
            return Enumerable.Range(0, int.MaxValue)
                .FirstOrDefault(nuance => ProofOfWork.ValidateSHA256Pow(buf, nuance, PowTarget));
        }
        throw new NotImplementedException();
    }

}

