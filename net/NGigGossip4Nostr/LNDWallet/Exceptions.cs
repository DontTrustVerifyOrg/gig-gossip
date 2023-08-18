using System;
namespace LNDWallet;


public enum LNDWalletErrorCode
{
    Ok = 0,
    InvalidToken = 1,
    NotEnoughFunds = 2,
    UnknownPayment = 3,
    UnknownInvoice = 4,
    PayoutAlreadyCompleted = 5,
}
public static class Extensions
{
    static string[] errorMesssages = new string[]{
        "",
        "Invalid authToken",
        "Not enough funds",
        "Unknown payment",
        "Unknown invoice",
        "Payout is already completed",
    };
    public static string Message(this LNDWalletErrorCode errorCode)
    {
        return errorMesssages[(int)errorCode];
    }
}

[Serializable]
public class LNDWalletException : Exception
{
    LNDWalletErrorCode ErrorCode { get; set; }
    public LNDWalletException(LNDWalletErrorCode lndWalletErrorCode) : base(lndWalletErrorCode.Message())
    {
        ErrorCode = lndWalletErrorCode;
    }
    public LNDWalletException(LNDWalletErrorCode lndWalletErrorCode, string message) : base(message)
    {
        ErrorCode = lndWalletErrorCode;
    }
}

