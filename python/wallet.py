import requests
from random import randbytes
import frames_pb2 as frames_pb2
import hashlib
from schnorr_lib import pubkey_gen, schnorr_sign
from datetime import datetime
import uuid
import base64
from enum import Enum
from typing import Any, Dict, Optional, Union

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

class CommitmentType(Enum):
    UnknownCommitmentType = 0
    Legacy = 1
    StaticRemoteKey = 2
    Anchors = 3
    ScriptEnforcedLease = 4
    SimpleTaproot = 5

class ClosureType(Enum):
    CooperativeClose = 0
    LocalForceClose = 1
    RemoteForceClose = 2
    BreachClose = 3
    FundingCanceled = 4
    Abandoned = 5

class Initiator(Enum):
    Unknown = 0
    Local = 1
    Remote = 2
    Both = 3

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

class PaymentFailureReason(Enum):
    NoFailure = 0
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

class PayoutState(Enum):
    Open = 0
    Sending = 1
    Sent = 2
    Failure = 3

class ResolutionType(Enum):
    TypeUnknown = 0
    Anchor = 1
    IncomingHtlc = 2
    OutgoingHtlc = 3
    Commit = 4

class ResolutionOutcome(Enum):
    OutcomeUnknown = 0
    Claimed = 1
    Unclaimed = 2
    Abandoned = 3
    FirstStage = 4
    Timeout = 5

class Wallet:
    def __init__(self, base_url: str, privkey: str):
        self.base_url = base_url
        self.privkey = privkey
        self.pubkey = pubkey_gen(bytes.fromhex(privkey)).hex()
        
    def _get_token(self) -> bytes:
        api_url = f"{self.base_url}/gettoken?pubkey=" + self.pubkey
        response = requests.get(api_url)
        response.raise_for_status()
        return uuid.UUID(response.json()["value"]).bytes

    def _create_authtoken(self) -> str:
        token = self._get_token()
        authTok = frames_pb2.AuthToken()
        authTok.Header.PublicKey.Value = bytes.fromhex(self.pubkey)
        authTok.Header.Timestamp.Value = int(datetime.now().timestamp())
        authTok.Header.TokenId.Value = token
        authTok.Signature.Value = schnorr_sign(
            hashlib.sha256(authTok.Header.SerializeToString()).digest(),
            bytes.fromhex(self.privkey),
            randbytes(32))
        return base64.b64encode(authTok.SerializeToString())
    
    def topupandmine6blocks(self, bitcoinAddr: str, satoshis: int) -> Any:
        """
        In RegTest mode only: Sends the specified amount of satoshis from the local Bitcoin wallet to the provided Bitcoin address, then automatically mines 6 blocks to ensure transaction confirmation. This function is useful for testing and development purposes in a controlled environment.

        Args:
            bitcoinAddr: Bitcoin address to receive the funds
            satoshis: Amount of satoshis to send

        Returns:
            Result object indicating success or failure
        """
        api_url = f"{self.base_url}/topupandmine6blocks"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken(), "bitcoinAddr": bitcoinAddr, "satoshis": satoshis})
        response.raise_for_status()
        return self.parse_response(response.json())

    def sendtoaddress(self, bitcoinAddr: str, satoshis: int) -> Any:
        """
        Transfers the specified amount of satoshis from the local Bitcoin wallet to the provided Bitcoin address. In RegTest mode, this function is available to all users. In other modes (TestNet, MainNet), only administrators can use this function.

        Args:
            bitcoinAddr: Bitcoin address
            satoshis: Number of satoshis

        Returns:
            Result object indicating success or failure
        """
        api_url = f"{self.base_url}/sendtoaddress"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken(), "bitcoinAddr": bitcoinAddr, "satoshis": satoshis})
        response.raise_for_status()
        return self.parse_response(response.json())

    def generateblocks(self, blocknum: int) -> Any:
        """
        Mines a specified number of new blocks in the Bitcoin network. This operation is only available in RegTest mode, which is used for testing and development purposes.

        Args:
            blocknum: The number of new blocks to generate. Must be a positive integer.

        Returns:
            Result object indicating success or failure
        """
        api_url = f"{self.base_url}/generateblocks"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken(), "blocknum": blocknum})
        response.raise_for_status()
        return self.parse_response(response.json())

    def newbitcoinaddress(self) -> Any:
        """
        Creates and returns a new Bitcoin address associated with the local Bitcoin wallet. This endpoint provides different access levels based on the network mode: in RegTest mode, it's accessible to all users, while in TestNet and MainNet modes, it's restricted to administrators only. This feature enables secure fund management and testing in various network environments.

        Returns:
            String result containing the new Bitcoin address
        """
        api_url = f"{self.base_url}/newbitcoinaddress"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken()})
        response.raise_for_status()
        return self.parse_response(response.json())

    def getbitcoinwalletbalance(self, minConf: int) -> Any:
        """
        Fetches and returns the current balance of the Bitcoin wallet in satoshis. The balance returned is based on the specified minimum number of confirmations. This endpoint has different access levels: in RegTest mode, it's accessible to all users, while in TestNet and MainNet modes, it's restricted to administrators only.

        Args:
            minConf: The minimum number of confirmations required for transactions to be included in the balance calculation. This parameter allows for flexibility in determining the level of certainty for the reported balance.

        Returns:
            Int64Result containing the wallet balance
        """
        api_url = f"{self.base_url}/getbitcoinwalletbalance"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken(), "minConf": minConf})
        response.raise_for_status()
        return self.parse_response(response.json())

    def getlndwalletbalance(self) -> Any:
        """
        Fetches and returns the current balance of the LND (Lightning Network Daemon) wallet, including confirmed, unconfirmed, total, reserved, and locked balances. This endpoint provides different access levels based on the network mode: in RegTest mode, it's accessible to all users, while in TestNet and MainNet modes, it's restricted to administrators only.

        Returns:
            LndWalletBalanceRet object containing detailed wallet balance information
        """
        api_url = f"{self.base_url}/getlndwalletbalance"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken()})
        response.raise_for_status()
        return self.parse_response(response.json())

    def openreserve(self, satoshis: int) -> Any:
        """
        Creates a new reserve in the LND wallet, allocating a specified amount of satoshis. This operation is useful for setting aside funds for future transactions or channel openings. The endpoint returns a unique identifier (GUID) for the newly created reserve. Access to this endpoint varies based on the network mode: in RegTest mode, it's accessible to all users, while in TestNet and MainNet modes, it's restricted to administrators only.

        Args:
            satoshis: The amount of satoshis to allocate to the new reserve. This value must be a positive integer representing the number of satoshis (1 satoshi = 0.00000001 BTC).

        Returns:
            GuidResult containing the unique identifier of the new reserve
        """
        api_url = f"{self.base_url}/openreserve"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken(), "satoshis": satoshis})
        response.raise_for_status()
        return self.parse_response(response.json())

    def closereserve(self, reserveId: str) -> Any:
        """
        Closes a previously opened reserve in the LND wallet, identified by its unique GUID. This operation releases the allocated funds back to the main wallet balance. Access to this endpoint varies based on the network mode: in RegTest mode, it's accessible to all users, while in TestNet and MainNet modes, it's restricted to administrators only.

        Args:
            reserveId: The unique identifier (GUID) of the reserve to be closed. This GUID was returned when the reserve was initially opened using the OpenReserve endpoint.

        Returns:
            Result object indicating success or failure
        """
        api_url = f"{self.base_url}/closereserve"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken(), "reserveId": reserveId})
        response.raise_for_status()
        return self.parse_response(response.json())

    def listorphanedreserves(self):
        api_url = f"{self.base_url}/listorphanedreserves"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken()})
        response.raise_for_status()
        return response.json()
    
    def listchannels(self,activeOnly):
        api_url = f"{self.base_url}/listchannels"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken(),"activeOnly":activeOnly})
        response.raise_for_status()
        return response.json()

    def listclosedchannels(self):
        api_url = f"{self.base_url}/listclosedchannels"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken()})
        response.raise_for_status()
        return response.json()
        
    def estimatefee(self, address, satoshis):
        api_url = f"{self.base_url}/estimatefee"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken(), "address": address, "satoshis": satoshis})
        response.raise_for_status()
        return self.parse_response(response.json())
    
    def getbalance(self) -> Any:
        """
        This endpoint provides detailed information about the user's account balance. The balance is returned as an AccountBalanceDetails object, which includes the total balance, available balance, and any pending transactions. All amounts are in satoshis (1 BTC = 100,000,000 satoshis).

        Returns:
            AccountBalanceDetailsResult containing detailed balance information
        """
        api_url = f"{self.base_url}/getbalance"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken()})
        response.raise_for_status()
        return self.parse_response(response.json())
    
    def newaddress(self) -> Any:
        """
        Creates and returns a new Bitcoin address associated with the user's Lightning Network account. This address can be used to receive on-chain Bitcoin payments, which will then be credited to the user's Lightning Network balance. This feature enables seamless integration between on-chain and off-chain funds management.

        Returns:
            StringResult containing the new Bitcoin address
        """
        api_url = f"{self.base_url}/newaddress"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken()})
        response.raise_for_status()
        return self.parse_response(response.json())
    
    def listtransactions(self) -> Any:
        """
        List Topup transactions.

        Returns:
            TransactionRecordArrayResult containing list of transaction records
        """
        api_url = f"{self.base_url}/listtransactions"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken()})
        response.raise_for_status()
        return self.parse_response(response.json())
    
    def registerpayout(self, satoshis: int, btcAddress: str) -> Any:
        """
        Initiates a new payout request from the user's Lightning Network wallet to a specified Bitcoin address on the blockchain. This operation registers the payout for execution, which may involve closing Lightning channels if necessary to fulfill the requested amount. The method returns a unique identifier (GUID) for tracking the payout request.

        Args:
            satoshis: The amount to be paid out, specified in satoshis (1 satoshi = 0.00000001 BTC). Must be a positive integer representing the exact payout amount.
            btcAddress: The destination Bitcoin address where the payout will be sent. This should be a valid Bitcoin address on the blockchain.

        Returns:
            GuidResult containing the unique identifier for the payout request
        """
        api_url = f"{self.base_url}/registerpayout"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken(), "satoshis": satoshis, "btcAddress": btcAddress})
        response.raise_for_status()
        return self.parse_response(response.json())
    
    def listpayouts(self) -> Any:
        """
        List registered payouts.

        Returns:
            PayoutRecordArrayResult containing list of payout records
        """
        api_url = f"{self.base_url}/listpayouts"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken()})
        response.raise_for_status()
        return response.json()
    
    def getpayout(self,payoutId):
        api_url = f"{self.base_url}/getpayout"
        response = requests.get(url=api_url, params={"authToken":self.create_authtoken(),"payoutId":payoutId})
        response.raise_for_status()
        return response.json()
    
    def addinvoice(self, satoshis, memo, expiry):
        api_url = f"{self.base_url}/addinvoice"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken(), "satoshis": satoshis, "memo": memo, "expiry": expiry})
        response.raise_for_status()
        return self.parse_response(response.json())

    def addhodlinvoice(self, satoshis: int, hash: str, memo: str, expiry: int) -> Any:
        """
        Creates and returns a new Lightning Network HODL invoice. HODL invoices enable escrow-like functionalities by allowing the recipient to claim the payment only when a specific preimage is revealed using the SettleInvoice method. This preimage must be provided by the payer or a trusted third party. This mechanism provides an additional layer of security and enables conditional payments in the Lightning Network, making it suitable for implementing escrow accounts and other advanced payment scenarios.

        Args:
            satoshis: The amount of the invoice in satoshis (1 BTC = 100,000,000 satoshis). Must be a positive integer representing the exact payment amount requested.
            hash: The SHA-256 hash of the preimage. The payer or a trusted third party must provide the corresponding preimage, which will be used with the SettleInvoice method to claim the payment, enabling escrow-like functionality.
            memo: An optional memo or description for the invoice. This can be used to provide additional context or details about the payment or escrow conditions to the payer. The memo will be included in the encoded payment request.
            expiry: The expiration time for the payment request, in seconds. After this duration, the HODL invoice will no longer be valid for payment. Consider setting an appropriate duration based on the expected escrow period.

        Returns:
            InvoiceRecordResult containing the created HODL invoice details
        """
        api_url = f"{self.base_url}/addhodlinvoice"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken(), "satoshis": satoshis, "hash": hash, "memo": memo, "expiry": expiry})
        response.raise_for_status()
        return self.parse_response(response.json())

    def decodeinvoice(self, paymentrequest: str) -> Any:
        """
        This endpoint decodes a Lightning Network invoice (also known as a payment request) and returns detailed information about its contents. It provides insights into the payment amount, recipient, expiry time, and other relevant metadata encoded in the invoice.

        Args:
            paymentrequest: The Lightning Network invoice string to be decoded. This is typically a long string starting with 'lnbc' for mainnet or 'lntb' for testnet.

        Returns:
            PaymentRequestRecordResult containing the decoded invoice information
        """
        api_url = f"{self.base_url}/decodeinvoice"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken(), "paymentrequest": paymentrequest})
        response.raise_for_status()
        return self.parse_response(response.json())

    def sendpayment(self, paymentrequest: str, timeout: int, feelimit: int) -> Any:
        """
        Initiates a Lightning Network payment based on the provided payment request. This endpoint attempts to route the payment to its final destination, handling all necessary channel operations and routing decisions.

        Args:
            paymentrequest: The Lightning Network payment request (invoice) to be paid. This encoded string contains all necessary details for the payment, including amount and recipient.
            timeout: Maximum time (in seconds) allowed for finding a route for the payment. If a route isn't found within this time, the payment attempt will be aborted.
            feelimit: Maximum fee (in millisatoshis) that the user is willing to pay for this transaction. If the calculated fee exceeds this limit, the payment will not be sent.

        Returns:
            PaymentRecordResult containing the payment details
        """
        api_url = f"{self.base_url}/sendpayment"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken(), "paymentrequest": paymentrequest, "timeout": timeout, "feelimit": feelimit})
        response.raise_for_status()
        return self.parse_response(response.json())

    def cancelinvoicesendpayment(self, paymenthash: str, paymentrequest: str, timeout: int, feelimit: int) -> Any:
        """
        Cancels invoice and Initiates a Lightning Network payment atomically. This endpoint attempts to route the payment to its final destination, handling all necessary channel operations and routing decisions.

        Args:
            paymenthash: The Lightning Network payment hash of the invoice to be cancelled
            paymentrequest: The Lightning Network payment request (invoice) to be paid. This encoded string contains all necessary details for the payment, including amount and recipient.
            timeout: Maximum time (in seconds) allowed for finding a route for the payment. If a route isn't found within this time, the payment attempt will be aborted.
            feelimit: Maximum fee (in millisatoshis) that the user is willing to pay for this transaction. If the calculated fee exceeds this limit, the payment will not be sent.

        Returns:
            PaymentRecordResult containing the payment details
        """
        api_url = f"{self.base_url}/cancelinvoicesendpayment"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken(), "paymenthash": paymenthash, "paymentrequest": paymentrequest, "timeout": timeout, "feelimit": feelimit})
        response.raise_for_status()
        return self.parse_response(response.json())

    def estimateroutefee(self, paymentrequest: str, timeout: int) -> Any:
        """
        This endpoint calculates and returns an estimated fee for routing a Lightning Network payment based on the provided payment request. It helps users anticipate the cost of sending a payment before actually initiating the transaction.

        Args:
            paymentrequest: The Lightning Network payment request (invoice) for which the route fee is to be estimated. This encoded string contains necessary details such as the payment amount and recipient.
            timeout: Maximum probing time (in seconds) allowed for finding a routing fee for the payment.

        Returns:
            RouteFeeRecordResult containing the fee estimation
        """
        api_url = f"{self.base_url}/estimateroutefee"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken(), "paymentrequest": paymentrequest, "timeout": timeout})
        response.raise_for_status()
        return self.parse_response(response.json())

    def settleinvoice(self, preimage: str) -> Any:
        """
        Settles a previously accepted hold invoice using the provided preimage. This action finalizes the payment process for a hold invoice, releasing the funds to the invoice creator.

        Args:
            preimage: The preimage (32-byte hash preimage) that corresponds to the payment hash of the hold invoice to be settled. This preimage serves as proof of payment.

        Returns:
            Result object indicating success or failure
        """
        api_url = f"{self.base_url}/settleinvoice"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken(), "preimage": preimage})
        response.raise_for_status()
        return self.parse_response(response.json())

    def cancelinvoice(self, paymenthash: str) -> Any:
        """
        Cancels an open Lightning Network invoice. This endpoint allows users to cancel an invoice that hasn't been paid yet. If the invoice is already canceled, the operation succeeds. However, if the invoice has been settled, the cancellation will fail.

        Args:
            paymenthash: The payment hash of the invoice to be canceled. This unique identifier is used to locate the specific invoice in the system.

        Returns:
            Result object indicating success or failure
        """
        api_url = f"{self.base_url}/cancelinvoice"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken(), "paymenthash": paymenthash})
        response.raise_for_status()
        return self.parse_response(response.json())

    def getinvoice(self, paymenthash: str) -> Any:
        """
        Fetches and returns detailed information about a specific Lightning Network invoice identified by its payment hash. This endpoint allows users to access invoice details such as amount, status, and creation date.

        Args:
            paymenthash: The payment hash of the invoice to retrieve. This unique identifier is used to locate the specific invoice in the system.

        Returns:
            InvoiceRecordResult containing the invoice details
        """
        api_url = f"{self.base_url}/getinvoice"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken(), "paymenthash": paymenthash})
        response.raise_for_status()
        return self.parse_response(response.json())

    def listinvoices(self) -> Any:
        """
        This endpoint returns a comprehensive list of all invoices associated with the authenticated user's account. It includes both paid and unpaid invoices, providing a complete overview of the account's invoice history.

        Returns:
            InvoiceRecordArrayResult containing list of all invoices
        """
        api_url = f"{self.base_url}/listinvoices"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken()})
        response.raise_for_status()
        return self.parse_response(response.json())

    def listpayments(self) -> Any:
        """
        This endpoint provides a list of all payments associated with the authenticated user's account that have not failed. This includes successful payments and those that are still in progress, offering a clear view of the account's payment activity.

        Returns:
            PaymentRecordArrayResult containing list of payments
        """
        api_url = f"{self.base_url}/listpayments"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken()})
        response.raise_for_status()
        return self.parse_response(response.json())

    def getpayment(self, paymenthash: str) -> Any:
        """
        This endpoint fetches and returns detailed information about a specific payment identified by its payment hash. It provides comprehensive data about the payment, including its status, amount, and other relevant details.

        Args:
            paymenthash: Unique identifier (hash) of the payment to be retrieved. This hash is used to locate the specific payment record in the system.

        Returns:
            PaymentRecordResult containing the payment details
        """
        api_url = f"{self.base_url}/getpayment"
        response = requests.get(url=api_url, params={"authToken": self._create_authtoken(), "paymenthash": paymenthash})
        response.raise_for_status()
        return self.parse_response(response.json())

    def parse_response(self, response_json: Dict[str, Any]) -> Optional[Union[Dict[str, Any], str]]:
        error_code_value = response_json.get('errorCode', 0)
        error_code = LNDWalletErrorCode(error_code_value)
        if error_code != LNDWalletErrorCode.Ok:
            error_message = response_json.get('errorMessage', 'An error occurred')
            raise Exception(f"{error_code.name}: {error_message}")
        return response_json.get('value')

