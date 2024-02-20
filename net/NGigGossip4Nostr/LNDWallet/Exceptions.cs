using System;
namespace LNDWallet;


/// <summary>
/// Defines the error codes for the LND Wallet.
/// </summary>
public enum LNDWalletErrorCode
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

    /// <summary>Indicates an invoice that is already cancelled.</summary>
    InvoiceAlreadyCancelled = 5,

    /// <summary>Indicates an invoice that is already accepted.</summary>
    InvoiceAlreadyAccepted = 6,

    /// <summary>Indicates an invoice that is already settled.</summary>
    InvoiceAlreadySettled = 7,

    /// <summary>Indicates that invoice was not accepted.</summary>
    InvoiceNotAccepted = 8,

    /// <summary>Indicates that payment was already payed.</summary>
    AlreadyPayed = 9,

    /// <summary>Indicates that the payout has already been completed.</summary>
    PayoutNotOpened = 10,

    /// <summary>Indicates that the payout has already been completed.</summary>
    PayoutAlreadySent = 11,

    /// <summary>Indicates that operation failed</summary>
    OperationFailed = 12,
}

/// <summary>
/// Provides extension methods for the <see cref="LNDWalletErrorCode"/> enumeration.
/// </summary>
public static class Extensions
{
    static string[] errorMesssages = new string[]{
        "",
        "Invalid authToken",
        "Not enough funds",
        "Unknown payment",
        "Unknown invoice",
        "Invoice already cancelled",
        "Invoice already accepted",
        "Invoice already settled",
        "Invoice not accepted",
        "Payment already payed",
        "Payout is not opened",
        "Payout is already completed",
        "Operation failed",
    };

    /// <summary>
    /// Gets a user-friendly message that corresponds to the specified <paramref name="errorCode"/>.
    /// </summary>
    /// <param name="errorCode">The error code to get a message for.</param>
    /// <returns>A user-friendly message that describes the error.</returns>
    public static string Message(this LNDWalletErrorCode errorCode)
    {
        return errorMesssages[(int)errorCode];
    }
}

/// <summary>
/// Represents errors that occur while dealing with the LND Wallet.
/// </summary>
[Serializable]
public class LNDWalletException : Exception
{
    /// <summary>Represents the error code of the exception.</summary>
    public LNDWalletErrorCode ErrorCode { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LNDWalletException"/> class with a specified error code.
    /// </summary>
    /// <param name="lndWalletErrorCode">The error code for exception.</param>
    public LNDWalletException(LNDWalletErrorCode lndWalletErrorCode) : base(lndWalletErrorCode.Message())
    {
        ErrorCode = lndWalletErrorCode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LNDWalletException"/> class with a specified error code and a detailed description.
    /// </summary>
    /// <param name="lndWalletErrorCode">The error code for exception.</param>
    /// <param name="message">The detail message that describes the current exception.</param>
    public LNDWalletException(LNDWalletErrorCode lndWalletErrorCode, string message) : base(message)
    {
        ErrorCode = lndWalletErrorCode;
    }
}
