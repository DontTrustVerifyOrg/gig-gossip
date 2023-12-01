using System;
namespace GigLNDWalletAPIClient;

public enum GigLNDWalletAPIErrorCode
{
    /// <summary>Indicates no error.</summary>
    Ok = 0,

    /// <summary>Indicates an invalid authentication token.</summary>
    InvalidToken = 1,

    /// <summary>Indicates insufficient funds.</summary>
    NotEnoughFunds = 2,

    /// <summary>Indicates an unknown payment.</summary>
    UnknownPayment = 3,

    /// <summary>Indicates an unknown invoice.</summary>
    UnknownInvoice = 4,

    /// <summary>Indicates that the payout has already been completed.</summary>
    PayoutAlreadyCompleted = 5,
}

[Serializable]
public class GigLNDWalletAPIException : Exception
{
    /// <summary>Represents the error code of the exception.</summary>
    public GigLNDWalletAPIErrorCode ErrorCode { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LNDWalletException"/> class with a specified error code and a detailed description.
    /// </summary>
    /// <param name="lndWalletErrorCode">The error code for exception.</param>
    /// <param name="message">The detail message that describes the current exception.</param>
    public GigLNDWalletAPIException(GigLNDWalletAPIErrorCode lndWalletErrorCode, string message) : base(message)
    {
        ErrorCode = lndWalletErrorCode;
    }
}

public static class WalletAPIResult
{
    public static void Check(dynamic t)
    {
        if ((int)t.ErrorCode != (int)GigLNDWalletAPIErrorCode.Ok)
            throw new GigLNDWalletAPIException((GigLNDWalletAPIErrorCode)((int)t.ErrorCode), t.ErrorMessage);
    }

    public static T Get<T>(dynamic t)
    {
        Check(t);
        return t.Value;
    }
}

