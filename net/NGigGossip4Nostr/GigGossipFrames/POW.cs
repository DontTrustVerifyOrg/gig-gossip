using System;
using CryptoToolkit;
namespace NGigGossip4Nostr;

using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Numerics;

/// <summary>
/// This class provides methods for generating and verifying proofs of work.
/// </summary>
[Serializable]
public class ProofOfWork
{
    /// <summary>
    /// Represents the maximum target value for the SHA256 proof of work scheme.
    /// </summary>
    public static BigInteger MaxPowTargetSha256 = BigInteger.Parse("0FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", System.Globalization.NumberStyles.AllowHexSpecifier);

    /// <summary>
    /// Calculates the proof of work target based on the complexity level.
    /// </summary>
    /// <param name="powScheme">The proof of work scheme to use. Currently only `sha256` value is accepted.</param>
    /// <param name="complexity">The complexity level.</param>
    /// <returns>The proof of work target.</returns>
    /// <exception cref="NotImplementedException">Thrown when an unsupported proof of work scheme is provided.</exception>
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

    /// <summary>
    /// Gets or sets the proof of work scheme.
    /// </summary>
    public string PowScheme { get; set; }

    /// <summary>
    /// Gets or sets the proof of work target.
    /// </summary>
    public BigInteger PowTarget { get; set; }

    /// <summary>
    /// Gets or sets the nuance used in the proof of work computation.
    /// </summary>
    public int Nuance { get; set; }

    /// <summary>
    /// Validates the proof of work.
    /// </summary>
    /// <param name="obj">The object to validate.</param>
    /// <returns>True if the proof of work is valid; otherwise, false.</returns>
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

    /// <summary>
    /// Validates a SHA256 proof of work.
    /// </summary>
    /// <param name="buf">The data to validate.</param>
    /// <param name="nuance">The nuance used in the computation of the proof of work.</param>
    /// <param name="powTarget">The target proof of work value.</param>
    /// <returns>True if the proof of work is valid; otherwise, false.</returns>
    public static bool ValidateSHA256Pow(byte[] buf, int nuance, BigInteger powTarget)
    {
        var sc = new BigInteger(Crypto.ComputeSha256(new List<byte[]>() { buf, BitConverter.GetBytes(nuance) }), isUnsigned: true, isBigEndian: false);
        return  sc <= powTarget;
    }

}

/// <summary>
/// Represents a request to compute a proof of work.
/// </summary>
[Serializable]
public class WorkRequest
{
    /// <summary>
    /// Gets or sets the proof of work scheme.
    /// </summary>
    public string PowScheme { get; set; }

    /// <summary>
    /// Gets or sets the proof of work target. Currently only `sha256` is supported.
    /// </summary>
    public BigInteger PowTarget { get; set; }

    /// <summary>
    /// Computes a proof of work based on the specified object.
    /// </summary>
    /// <param name="obj">The object to compute the proof of work based on.</param>
    /// <returns>A <see cref="ProofOfWork"/> instance that represents the computed proof of work.</returns>
    public ProofOfWork ComputeProof(object obj)
    {
        return new ProofOfWork
        {
            PowScheme = PowScheme,
            PowTarget = PowTarget,
            Nuance = ComputePow(obj),
        };
    }

    /// <summary>
    /// Computes the proof of work.
    /// </summary>
    /// <param name="obj">The object to compute the proof of work based on.</param>
    /// <returns>The computed proof of work.</returns>
    /// <exception cref="NotImplementedException">Thrown when an unsupported proof of work scheme is provided.</exception>
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

