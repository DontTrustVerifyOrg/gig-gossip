from enum import Enum

class LNDWalletErrorCode(Enum):
    Ok = 0
    InvalidToken = 1
    NotEnoughFunds = 2
    UnknownPayment = 3
    UnknownInvoice = 4
    InvoiceAlreadyCancelled = 5
    InvoiceAlreadyAccepted = 6
    InvoiceAlreadySettled = 7
    InvoiceNotAccepted = 8
    AlreadyPayed = 9
    PayoutNotOpened = 10
    PayoutAlreadySent = 11
    OperationFailed = 12
    AccessDenied = 13
    FeeLimitTooSmall = 14

class InvoiceState(Enum):
    Open = 0
    Settled = 1
    Cancelled = 2
    Accepted = 3

class PaymentStatus(Enum):
    InFlight = 1
    Succeeded = 2
    Failed = 3
    Initiated = 4

class PayoutState(Enum):
    Open = 0
    Processing = 1
    Sending = 2
    Sent = 3
    Failure = 4

class PaymentFailureReason(Enum):
    Nothing = 0
    Timeout = 1
    NoRoute = 2
    Error = 3
    IncorrectPaymentDetails = 4
    InsufficientBalance = 5
    Canceled = 6
    EmptyReturnStream = 101
    InvoiceAlreadySettled = 102
    InvoiceAlreadyCancelled = 103
    InvoiceAlreadyAccepted = 104
    FeeLimitTooSmall = 105
