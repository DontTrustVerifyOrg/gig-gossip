using System;
namespace GigLNDWalletAPIClient;


[Serializable]
public class GigLNDWalletAPIException : Exception
{
    /// <summary>Represents the error code of the exception.</summary>
    public LNDWalletErrorCode ErrorCode { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LNDWalletException"/> class with a specified error code and a detailed description.
    /// </summary>
    /// <param name="lndWalletErrorCode">The error code for exception.</param>
    /// <param name="message">The detail message that describes the current exception.</param>
    public GigLNDWalletAPIException(LNDWalletErrorCode lndWalletErrorCode, string message) : base(message)
    {
        ErrorCode = lndWalletErrorCode;
    }
}

public static class WalletAPIResult
{
    public static LNDWalletErrorCode Status(dynamic t)
    {
        return t.ErrorCode;
    }

    public static void Check(dynamic t)
    {
        if (t.ErrorCode != LNDWalletErrorCode.Ok)
            throw new GigLNDWalletAPIException(t.ErrorCode, t.ErrorMessage);
    }

    public static T Get<T>(dynamic t)
    {
        Check(t);
        return t.Value;
    }
}

