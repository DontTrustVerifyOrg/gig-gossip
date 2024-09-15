using NBitcoin.Secp256k1;

using System.Data;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using LNDClient;
using Grpc.Core;
using Lnrpc;
using System.Collections.Concurrent;
using TraceExColor;
using Routerrpc;
using Invoicesrpc;
using System.Security.Cryptography.X509Certificates;
using Spectre.Console;
using Walletrpc;
using System.Runtime.ConstrainedExecution;
using GigGossip;
using System.Diagnostics.Tracing;

namespace LNDWallet;

[Serializable]
public enum InvoiceState
{
    Open = 0,
    Settled = 1,
    Cancelled = 2,
    Accepted = 3,
}

[Serializable]
public enum PaymentStatus
{
    InFlight = 1,
    Succeeded = 2,
    Failed = 3,
    Initiated = 4,
}

[Serializable]
public enum PayoutState
{
    Open = 0,
    Processing = 1,
    Sending = 2,
    Sent = 3,
    Failure = 4,
}

[Serializable]
public enum PaymentFailureReason
{
    None = 0,
    Timeout = 1,
    NoRoute = 2,
    Error = 3,
    IncorrectPaymentDetails = 4,
    InsufficientBalance = 5,
    Canceled = 6,
    EmptyReturnStream = 101,
    InvoiceAlreadySettled = 102,
    InvoiceAlreadyCancelled = 103,
    InvoiceAlreadyAccepted = 104,
    FeeLimitTooSmall = 105,

}

[Serializable]
public class RouteFeeRecord
{
    public required long RoutingFeeMsat { get; set; }
    public required long TimeLockDelay { get; set; }
    public required PaymentFailureReason FailureReason { get; set; }
}

[Serializable]
public class PaymentRequestRecord
{
    public required string PaymentHash { get; set; }
    public required long Satoshis { get; set; }

    public required string PaymentAddr { get; set; }
    public required string Memo { get; set; }
    public required DateTime CreationTime { get; set; }
    public required DateTime ExpiryTime { get; set; }
}


[Serializable]
public class InvoiceRecord : PaymentRequestRecord
{
    public required string PaymentRequest { get; set; }
    public required InvoiceState State { get; set; }
    public required bool IsHodl { get; set; }

    public DateTime? SettleTime { get; set; }

    public static InvoiceRecord FromPaymentRequestRecord(PaymentRequestRecord payreq, string paymentRequest, InvoiceState state, bool isHodl, DateTime? settleTime = null)
    {
        return new InvoiceRecord
        {
            PaymentHash = payreq.PaymentHash,
            Satoshis = payreq.Satoshis,
            PaymentAddr = payreq.PaymentAddr,
            Memo = payreq.Memo,
            CreationTime = payreq.CreationTime,
            ExpiryTime = payreq.ExpiryTime,
            IsHodl = isHodl,
            PaymentRequest = paymentRequest,
            State = state,
            SettleTime = settleTime,
        };
    }
}

[Serializable]
public class PaymentRecord
{
    public required string PaymentHash { get; set; }
    public required PaymentStatus Status { get; set; }
    public required PaymentFailureReason FailureReason { get; set; }
    public required long Satoshis { get; set; }
    public required long FeeMsat { get; set; }
}


[Serializable]
public class AccountBalanceDetails
{
    /// <summary>
    /// Amount that is available for the user at the time
    /// </summary>
    public required long AvailableAmount { get; set; }

    /// <summary>
    /// Amount of topped up by sending to the NewAddress but not yet confirmed (less than 6 confirmations)
    /// </summary>
    public required long IncomingNotConfirmed { get; set; }

    /// <summary>
    /// Amount on accepted invoices that are still not Settled (can be Cancelled or Settled in the future)
    /// </summary>
    public required long IncomingAcceptedNotSettled { get; set; }


    /// <summary>
    /// Amount that of payments that are not yet Successful (still can Fail and be given back)
    /// </summary>
    public required long OutgoingInFlightPayments { get; set; }

    /// <summary>
    /// Amount that is locked in the system for the payouts that are still in progress 
    /// </summary>
    public required long OutgoingInProgressPayouts { get; set; }

    /// <summary>
    /// Total amount on payment fees (incuding those in InFlight state)
    /// </summary>
    public required long OutgoingPaymentFees { get; set; }

    /// <summary>
    /// Amount of inflight payment fees
    /// </summary>
    public required long OutgoingInFlightPaymentFees { get; set; }

    /// <summary>
    /// Total amount on payout fees (including these that are in progress)
    /// </summary>
    public required long OutgoingPayoutFees { get; set; }

    /// <summary>
    /// Total amount of inprogress payout fees
    /// </summary>
    public required long OutgoingInProgressPayoutFees { get; set; }
}

[Serializable]
public class InvoiceStateChange
{
    public required string PaymentHash { get; set; }
    public required InvoiceState NewState { get; set; }
}

[Serializable]
public class PaymentStatusChanged
{
    public required string PaymentHash { get; set; }
    public required PaymentStatus NewStatus { get; set; }
    public required PaymentFailureReason FailureReason { get; set; }
}

public class InvoiceStateChangedEventArgs : EventArgs
{
    public required InvoiceStateChange InvoiceStateChange { get; set; }
}

public delegate void InvoiceStateChangedEventHandler(object sender, InvoiceStateChangedEventArgs e);

public class PaymentStatusChangedEventArgs : EventArgs
{
    public required PaymentStatusChanged PaymentStatusChanged { get; set; }
}

public delegate void PaymentStatusChangedEventHandler(object sender, PaymentStatusChangedEventArgs e);

public class LNDEventSource
{
    public event InvoiceStateChangedEventHandler OnInvoiceStateChanged;
    public event PaymentStatusChangedEventHandler OnPaymentStatusChanged;

    public void FireOnInvoiceStateChanged(string paymentHash, InvoiceState invstate)
    {
        OnInvoiceStateChanged?.Invoke(this, new InvoiceStateChangedEventArgs()
        {
             InvoiceStateChange = new InvoiceStateChange
             {
                 PaymentHash = paymentHash,
                 NewState = invstate
             }
        });
    }

    public void FireOnPaymentStatusChanged(string paymentHash, PaymentStatus paystatus, PaymentFailureReason failureReason)
    {
        OnPaymentStatusChanged?.Invoke(this, new PaymentStatusChangedEventArgs()
        {
             PaymentStatusChanged= new PaymentStatusChanged
             {
                 PaymentHash = paymentHash,
                 NewStatus = paystatus,
                 FailureReason = failureReason,
             }
        });
    }

    public virtual void CancelSingleInvoiceTracking(string paymentHash) { }
}

    public class LNDAccountManager
    {
        private LND.NodeSettings lndConf;
        private BitcoinNode bitcoinNode;
        private ThreadLocal<WaletContext> walletContext;
        public string PublicKey;
        private LNDEventSource eventSource;
        private CancellationTokenSource trackPaymentsCancallationTokenSource = new();

        internal LNDAccountManager(BitcoinNode nd, LND.NodeSettings conf, DBProvider provider, string connectionString, ECXOnlyPubKey pubKey, LNDEventSource eventSource)
        {
            this.bitcoinNode = nd;
            this.lndConf = conf;
            this.PublicKey = pubKey.AsHex();
            this.walletContext = new ThreadLocal<WaletContext>(() => new WaletContext(provider, connectionString));
            this.eventSource = eventSource;
        }

        public string CreateNewTopupAddress()
        {
            var newaddress = LND.NewAddress(lndConf);
            walletContext.Value
                .INSERT(new TopupAddress() { BitcoinAddress = newaddress, PublicKey = PublicKey })
                .SAVE();
            return newaddress;
        }

        public Guid RegisterNewPayoutForExecution(long satoshis, string btcAddress, long payoutfee)
        {
            using var TX = walletContext.Value.BEGIN_TRANSACTION(IsolationLevel.Serializable);

            var availableAmount = GetAccountBallance().AvailableAmount;
            if (availableAmount < satoshis + payoutfee)
                throw new LNDWalletException(LNDWalletErrorCode.NotEnoughFunds); ;

            var myid = Guid.NewGuid();

            walletContext.Value
                .INSERT(new Payout()
                {
                    PayoutId = myid,
                    BitcoinAddress = btcAddress,
                    PublicKey = PublicKey,
                    PayoutFee = payoutfee,
                    State = PayoutState.Open,
                    Satoshis = satoshis
                })
                .INSERT(new Reserve()
                {
                    ReserveId = myid,
                    Satoshis = satoshis
                })
                .SAVE();

            TX.Commit();
            return myid;
        }

        private long GetExecutedTopupTotalAmount(int minConf)
        {
            var myaddrs = new HashSet<string>(
                from a in walletContext.Value.TopupAddresses
                where a.PublicKey == PublicKey
                select a.BitcoinAddress);

            var transactuinsResp = LND.GetTransactions(lndConf);
            long balance = 0;
            foreach (var transation in transactuinsResp.Transactions)
                if (transation.NumConfirmations >= minConf)
                    foreach (var outp in transation.OutputDetails)
                        if (outp.IsOurAddress)
                            if (myaddrs.Contains(outp.Address))
                                balance += outp.Amount;

            return balance;
        }

        public (long all, long allTxFee, long confirmed, long confirmedTxFee) GetExecutedPayoutTotalAmount(int minConf)
        {
            using var TX = walletContext.Value.BEGIN_TRANSACTION(IsolationLevel.Serializable);

            Dictionary<string, LNDWallet.Payout> mypayouts;
            mypayouts = new Dictionary<string, LNDWallet.Payout>(
                from a in walletContext.Value.Payouts
                where a.PublicKey == PublicKey
                select new KeyValuePair<string, LNDWallet.Payout>(a.BitcoinAddress, a));

            var transactuinsResp = LND.GetTransactions(lndConf);
            long confirmed = 0;
            long confirmedTxFee = 0;
            long all = 0;
            long allTxFee = 0;
            foreach (var transation in transactuinsResp.Transactions)
                foreach (var outp in transation.OutputDetails)
                    if (!outp.IsOurAddress)
                        if (mypayouts.ContainsKey(outp.Address))
                        {
                            if (transation.NumConfirmations >= minConf)
                            {
                                confirmed += outp.Amount;
                                confirmedTxFee += (long)mypayouts[outp.Address].PayoutFee;
                            }
                            all += outp.Amount;
                            allTxFee += (long)mypayouts[outp.Address].PayoutFee;
                        }

            TX.Commit();

            return (all, allTxFee, confirmed, confirmedTxFee);
        }

        public InvoiceRecord CreateNewHodlInvoice(long satoshis, string memo, byte[] hash, long expiry)
        {
            var inv = LND.AddHodlInvoice(lndConf, satoshis, memo, hash, expiry);
            walletContext.Value
                .INSERT(new HodlInvoice()
                {
                    PaymentHash = hash.AsHex(),
                    PublicKey = PublicKey,
                })
                .SAVE();
            var payreq = DecodeInvoice(inv.PaymentRequest);
            return InvoiceRecord.FromPaymentRequestRecord(payreq,
                paymentRequest: inv.PaymentRequest,
                state: InvoiceState.Open,
                isHodl: true);
        }

        public InvoiceRecord CreateNewClassicInvoice(long satoshis, string memo, long expiry)
        {
            var inv = LND.AddInvoice(lndConf, satoshis, memo, expiry);
            walletContext.Value
                .INSERT(new ClassicInvoice()
                {
                    PaymentHash = inv.RHash.ToArray().AsHex(),
                    PublicKey = PublicKey,
                })
                .SAVE();
            var payreq = DecodeInvoice(inv.PaymentRequest);
            return InvoiceRecord.FromPaymentRequestRecord(payreq,
                paymentRequest: inv.PaymentRequest,
                state: InvoiceState.Open,
                isHodl: false);
        }

    public PaymentRequestRecord DecodeInvoice(string paymentRequest)
    {
        var dec = LND.DecodeInvoice(lndConf, paymentRequest);
        return new PaymentRequestRecord
        {
            PaymentHash = dec.PaymentHash,
            PaymentAddr = dec.PaymentAddr.ToArray().AsHex(),
            Satoshis = dec.NumSatoshis,
            CreationTime = DateTimeOffset.FromUnixTimeSeconds(dec.Timestamp).UtcDateTime,
            ExpiryTime = DateTimeOffset.FromUnixTimeSeconds(dec.Timestamp).UtcDateTime.AddSeconds(dec.Expiry),
            Memo = dec.Description,
        };
    }

    public RouteFeeRecord EstimateRouteFee(string paymentRequest, long ourRouteFeeSat)
    {
        var decinv = LND.DecodeInvoice(lndConf, paymentRequest);
        var invoice = LND.LookupInvoiceV2(lndConf, decinv.PaymentHash.AsBytes());
        if (invoice.State == Lnrpc.Invoice.Types.InvoiceState.Settled)
            throw new LNDWalletException(LNDWalletErrorCode.InvoiceAlreadySettled);
        else if (invoice.State == Lnrpc.Invoice.Types.InvoiceState.Canceled)
            throw new LNDWalletException(LNDWalletErrorCode.InvoiceAlreadyCancelled);
        else if (invoice.State == Lnrpc.Invoice.Types.InvoiceState.Accepted)
            throw new LNDWalletException(LNDWalletErrorCode.InvoiceAlreadyAccepted);

        using var TX = walletContext.Value.BEGIN_TRANSACTION(IsolationLevel.Serializable);
        HodlInvoice? selfHodlInvoice = (from inv in walletContext.Value.HodlInvoices
                                        where inv.PaymentHash == decinv.PaymentHash
                                        select inv).FirstOrDefault();

        ClassicInvoice? selfClsInv = null;
        if (selfHodlInvoice == null)
            selfClsInv = (from inv in walletContext.Value.ClassicInvoices
                          where inv.PaymentHash == decinv.PaymentHash
                          select inv).FirstOrDefault();

        if (selfHodlInvoice != null || selfClsInv != null) // selfpayment
        {

            var payment = (from pay in walletContext.Value.InternalPayments
                           where pay.PaymentHash == decinv.PaymentHash
                           select pay).FirstOrDefault();

            if (payment != null)
            {
                if (payment.Status == InternalPaymentStatus.InFlight)
                    throw new LNDWalletException(LNDWalletErrorCode.InvoiceAlreadyAccepted);
                else //if (payment.Status == InternalPaymentStatus.Succeeded)
                    throw new LNDWalletException(LNDWalletErrorCode.InvoiceAlreadySettled);
            }
            TX.Commit();

            return new RouteFeeRecord { RoutingFeeMsat = ourRouteFeeSat * 1000, TimeLockDelay = 0, FailureReason = PaymentFailureReason.None };
        }
        else
        {
            TX.Commit();
            var rsp = LND.EstimateRouteFee(lndConf, paymentRequest);
            return new RouteFeeRecord { RoutingFeeMsat = rsp.RoutingFeeMsat, TimeLockDelay = rsp.TimeLockDelay, FailureReason = (PaymentFailureReason)rsp.FailureReason };
        }
    }

    private void failedPaymentRecordStore(PayReq payReq, PaymentFailureReason reason, bool delete)
    {
        walletContext.Value
            .INSERT(new FailedPayment
            {
                Id = Guid.NewGuid(),
                DateTime = DateTime.UtcNow,
                PaymentHash = payReq.PaymentHash,
                PublicKey = PublicKey,
                FailureReason = reason,
            })
            .SAVE();

        if (delete)
        {
            walletContext.Value
                .DELETE_IF_EXISTS(from pay in walletContext.Value.InternalPayments
                                  where pay.PaymentHash == payReq.PaymentHash
                                  select pay)
                .DELETE_IF_EXISTS(
                                from pay in walletContext.Value.ExternalPayments
                                where pay.PaymentHash == payReq.PaymentHash
                                select pay)
                .SAVE();
        }
    }

    private PaymentRecord failedPaymentRecordAndCommit(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction TX, PayReq payReq, PaymentFailureReason reason, bool delete)
    {
        failedPaymentRecordStore(payReq, reason,delete);

        TX.Commit();
        return new PaymentRecord
        {
            FeeMsat = 0,
            PaymentHash = payReq.PaymentHash,
            Satoshis = payReq.NumSatoshis,
            Status = PaymentStatus.Failed,
            FailureReason = reason,
        };
    }

    public async Task<PaymentRecord> SendPaymentAsync(string paymentRequest, int timeout, long ourRouteFeeSat, long feelimit)
    {

        var decinv = LND.DecodeInvoice(lndConf, paymentRequest);
        var invoice = LND.LookupInvoiceV2(lndConf, decinv.PaymentHash.AsBytes());

        using var TX = walletContext.Value.BEGIN_TRANSACTION(IsolationLevel.Serializable);

        if (invoice.State == Lnrpc.Invoice.Types.InvoiceState.Settled)
            return failedPaymentRecordAndCommit(TX, decinv, PaymentFailureReason.InvoiceAlreadySettled,false);
        else if (invoice.State == Lnrpc.Invoice.Types.InvoiceState.Canceled)
        {
            var internalPayment = (from pay in walletContext.Value.InternalPayments
                                   where pay.PaymentHash == decinv.PaymentHash
                                   select pay).FirstOrDefault();

            if (internalPayment != null)
                return failedPaymentRecordAndCommit(TX, decinv, internalPayment.Status== InternalPaymentStatus.InFlight? PaymentFailureReason.InvoiceAlreadyAccepted: PaymentFailureReason.InvoiceAlreadySettled, false);
            else
                return failedPaymentRecordAndCommit(TX, decinv, PaymentFailureReason.InvoiceAlreadyCancelled, false);
        }
        else if (invoice.State == Lnrpc.Invoice.Types.InvoiceState.Accepted)
            return failedPaymentRecordAndCommit(TX, decinv, PaymentFailureReason.InvoiceAlreadyAccepted, false);


        var availableAmount = GetAccountBallance().AvailableAmount;

        HodlInvoice? selfHodlInvoice = (from inv in walletContext.Value.HodlInvoices
                                        where inv.PaymentHash == decinv.PaymentHash
                                        select inv).FirstOrDefault();

        ClassicInvoice? selfClsInv = null;
        if (selfHodlInvoice == null)
            selfClsInv = (from inv in walletContext.Value.ClassicInvoices
                          where inv.PaymentHash == decinv.PaymentHash
                          select inv).FirstOrDefault();

        if (selfHodlInvoice != null || selfClsInv != null) // selfpayment
        {
            if (feelimit < ourRouteFeeSat)
                return failedPaymentRecordAndCommit(TX, decinv, PaymentFailureReason.FeeLimitTooSmall, false);

            if (availableAmount < decinv.NumSatoshis + ourRouteFeeSat)
                return failedPaymentRecordAndCommit(TX, decinv, PaymentFailureReason.InsufficientBalance, false);

            var internalPayment = (from pay in walletContext.Value.InternalPayments
                                   where pay.PaymentHash == decinv.PaymentHash
                                   select pay).FirstOrDefault();

            if (internalPayment != null)
            {
                if (internalPayment.Status == InternalPaymentStatus.InFlight)
                    return failedPaymentRecordAndCommit(TX, decinv, PaymentFailureReason.InvoiceAlreadyAccepted, false);
                else //if (internalPayment.Status == InternalPaymentStatus.Succeeded)
                    return failedPaymentRecordAndCommit(TX, decinv, PaymentFailureReason.InvoiceAlreadySettled, false);
            }

            walletContext.Value
                .INSERT(new InternalPayment()
                {
                    PaymentHash = decinv.PaymentHash,
                    PublicKey = PublicKey,
                    PaymentFee = ourRouteFeeSat,
                    Satoshis = decinv.NumSatoshis,
                    Status = selfHodlInvoice != null ? InternalPaymentStatus.InFlight : InternalPaymentStatus.Succeeded,
                })
                .SAVE();

            eventSource.CancelSingleInvoiceTracking(decinv.PaymentHash);

            eventSource.FireOnInvoiceStateChanged(decinv.PaymentHash, InvoiceState.Accepted);

            if (selfClsInv != null)
                eventSource.FireOnInvoiceStateChanged(decinv.PaymentHash, InvoiceState.Settled);

            eventSource.FireOnPaymentStatusChanged(decinv.PaymentHash,
                selfHodlInvoice != null ? PaymentStatus.InFlight : PaymentStatus.Succeeded,
                PaymentFailureReason.None);

            TX.Commit();

            return new PaymentRecord
            {
                PaymentHash = decinv.PaymentHash,
                Satoshis = decinv.NumSatoshis,
                FailureReason = PaymentFailureReason.None,
                FeeMsat = ourRouteFeeSat * 1000,
                Status = selfHodlInvoice != null ? PaymentStatus.InFlight : PaymentStatus.Succeeded
            };
        }
        else
        {
            if (availableAmount < decinv.NumSatoshis + feelimit + ourRouteFeeSat)
                return failedPaymentRecordAndCommit(TX, decinv, PaymentFailureReason.InsufficientBalance, false);

            var externalPayment = (from pay in walletContext.Value.ExternalPayments
                                   where pay.PaymentHash == decinv.PaymentHash
                                   select pay).FirstOrDefault();

            if (externalPayment != null)
            {
                var stream = LND.TrackPaymentV2(lndConf, decinv.PaymentHash.AsBytes(), false);
                Payment cur = null;
                while (await stream.ResponseStream.MoveNext(trackPaymentsCancallationTokenSource.Token))
                {
                    cur = stream.ResponseStream.Current;
                    if (cur.Status == Payment.Types.PaymentStatus.InFlight)
                        return failedPaymentRecordAndCommit(TX, decinv, PaymentFailureReason.InvoiceAlreadyAccepted, false);
                    else if (cur.Status == Payment.Types.PaymentStatus.Initiated)
                        return failedPaymentRecordAndCommit(TX, decinv, PaymentFailureReason.InvoiceAlreadyAccepted, false);
                    else if (cur.Status == Payment.Types.PaymentStatus.Succeeded)
                        return failedPaymentRecordAndCommit(TX, decinv, PaymentFailureReason.InvoiceAlreadySettled, false);
                    else
                        break;
                }

                // previously failed - retrying with my publickey
                failedPaymentRecordStore(decinv, cur == null ? PaymentFailureReason.EmptyReturnStream : (PaymentFailureReason)cur.FailureReason, false);
            }
            {
                walletContext.Value
                    .INSERT(new ExternalPayment()
                    {
                        PaymentHash = decinv.PaymentHash,
                        PublicKey = PublicKey,
                        Status = ExternalPaymentStatus.Initiated,
                    })
                    .SAVE();

                var stream = LND.SendPaymentV2(lndConf, paymentRequest, timeout, feelimit);
                Payment cur = null;
                while (await stream.ResponseStream.MoveNext(trackPaymentsCancallationTokenSource.Token))
                {
                    cur = stream.ResponseStream.Current;
                    if (cur.Status == Payment.Types.PaymentStatus.Initiated
                        || cur.Status == Payment.Types.PaymentStatus.InFlight
                        || cur.Status == Payment.Types.PaymentStatus.Succeeded)
                        break;
                    else if (cur.Status == Payment.Types.PaymentStatus.Failed)
                        return failedPaymentRecordAndCommit(TX, decinv, (PaymentFailureReason)cur.FailureReason,true);
                }

                if (cur == null)
                    return failedPaymentRecordAndCommit(TX, decinv, PaymentFailureReason.EmptyReturnStream,true);

                var payment = (from pay in walletContext.Value.ExternalPayments
                               where pay.PaymentHash == decinv.PaymentHash && pay.PublicKey == PublicKey
                               select pay).FirstOrDefault();

                if(payment!=null)
                {
                    payment.Status = (ExternalPaymentStatus)cur.Status;
                    walletContext.Value.UPDATE(payment);
                    walletContext.Value.SAVE();
                }

                TX.Commit();
                return new PaymentRecord
                {
                    PaymentHash = decinv.PaymentHash,
                    Satoshis = decinv.NumSatoshis,
                    FailureReason = PaymentFailureReason.None,
                    FeeMsat = cur.FeeMsat,
                    Status = (PaymentStatus)cur.Status
                };
            }
        }

    }

    public void SettleInvoice(byte[] preimage)
    {
        using var TX = walletContext.Value.BEGIN_TRANSACTION(IsolationLevel.Serializable);

        var paymentHashBytes = Crypto.ComputePaymentHash(preimage);
        var paymentHash = paymentHashBytes.AsHex();

        var invoice = LND.LookupInvoiceV2(lndConf, paymentHashBytes);

        HodlInvoice? selfHodlInvoice = (from inv in walletContext.Value.HodlInvoices
                                        where inv.PaymentHash == paymentHash && inv.PublicKey == PublicKey
                                        select inv).FirstOrDefault();

        if (selfHodlInvoice == null)
            throw new LNDWalletException(LNDWalletErrorCode.UnknownInvoice);

        var internalPayment = (from pay in walletContext.Value.InternalPayments
                               where pay.PaymentHash == paymentHash
                               select pay).FirstOrDefault();

        if (internalPayment != null)
        {
            if (internalPayment.Status == InternalPaymentStatus.Succeeded)
                throw new LNDWalletException(LNDWalletErrorCode.InvoiceAlreadySettled);
            else if (internalPayment.Status != InternalPaymentStatus.InFlight) //this should be always true
                throw new LNDWalletException(LNDWalletErrorCode.InvoiceNotAccepted);

            var payment = (from pay in walletContext.Value.InternalPayments
                           where pay.PaymentHash == paymentHash
                           select pay).FirstOrDefault();
            if(payment!=null)
            {
                payment.Status = InternalPaymentStatus.Succeeded;
                walletContext.Value
                    .UPDATE(payment)
                    .SAVE();
            }

            eventSource.FireOnInvoiceStateChanged(paymentHash, InvoiceState.Settled);
            eventSource.FireOnPaymentStatusChanged(paymentHash, PaymentStatus.Succeeded, PaymentFailureReason.None);
        }
        else
        {
            LND.SettleInvoice(lndConf, preimage);
        }
        TX.Commit();
    }

    public void CancelInvoice(string paymentHash)
    {
        using var TX = walletContext.Value.BEGIN_TRANSACTION(IsolationLevel.Serializable);

        var invoice = LND.LookupInvoiceV2(lndConf, paymentHash.AsBytes());

        HodlInvoice? selfHodlInvoice = (from inv in walletContext.Value.HodlInvoices
                                        where inv.PaymentHash == paymentHash && inv.PublicKey == PublicKey
                                        select inv).FirstOrDefault();

        ClassicInvoice? selfClsInv = null;
        if (selfHodlInvoice == null)
            selfClsInv = (from inv in walletContext.Value.ClassicInvoices
                          where inv.PaymentHash == paymentHash
                          select inv).FirstOrDefault();

        if (selfHodlInvoice == null)
            throw new LNDWalletException(LNDWalletErrorCode.UnknownInvoice);

        var internalPayment = (from pay in walletContext.Value.InternalPayments
                               where pay.PaymentHash == paymentHash
                               select pay).FirstOrDefault();

        if (internalPayment != null)
        {
            if (internalPayment.Status == InternalPaymentStatus.Succeeded)
                throw new LNDWalletException(LNDWalletErrorCode.InvoiceAlreadySettled);
            else if (internalPayment.Status != InternalPaymentStatus.InFlight) // this should be always true
                throw new LNDWalletException(LNDWalletErrorCode.InvoiceNotAccepted);

            walletContext.Value
                .DELETE_IF_EXISTS(from pay in walletContext.Value.InternalPayments
                                  where pay.PaymentHash == paymentHash
                                  select pay)
                .INSERT(new FailedPayment
                {
                    Id = Guid.NewGuid(),
                    DateTime = DateTime.UtcNow,
                    PaymentHash = paymentHash,
                    PublicKey = PublicKey,
                    FailureReason = PaymentFailureReason.Canceled,
                })
                .SAVE();

            eventSource.CancelSingleInvoiceTracking(paymentHash);
            eventSource.FireOnPaymentStatusChanged(paymentHash, PaymentStatus.Failed, PaymentFailureReason.Canceled);
        }
        LND.CancelInvoice(lndConf, paymentHash.AsBytes());
        TX.Commit();
    }

    public InvoiceRecord[] ListInvoices(bool includeClassic, bool includeHodl)
    {
        var allInvs = new Dictionary<string, InvoiceRecord>(
            (from inv in LND.ListInvoices(lndConf).Invoices
             select KeyValuePair.Create(inv.RHash.ToArray().AsHex(),
             new InvoiceRecord
             {
                 PaymentHash = inv.RHash.ToArray().AsHex(),
                 IsHodl = false,
                 PaymentRequest = inv.PaymentRequest,
                 Satoshis = inv.Value,
                 State = (InvoiceState)inv.State,
                 Memo = inv.Memo,
                 PaymentAddr = inv.PaymentAddr.ToArray().AsHex(),
                 CreationTime = DateTimeOffset.FromUnixTimeSeconds(inv.CreationDate).UtcDateTime,
                 ExpiryTime = DateTimeOffset.FromUnixTimeSeconds(inv.CreationDate).UtcDateTime.AddSeconds(inv.Expiry),
                 SettleTime = DateTimeOffset.FromUnixTimeSeconds(inv.SettleDate).UtcDateTime,
             })));

        var ret = new Dictionary<string, InvoiceRecord>();

        if (includeClassic)
        {
            var myInvoices = (from inv in walletContext.Value.ClassicInvoices where inv.PublicKey == this.PublicKey select inv);
            foreach (var inv in myInvoices)
            {
                if (allInvs.ContainsKey(inv.PaymentHash))
                    ret.Add(inv.PaymentHash, allInvs[inv.PaymentHash]);
            }
        }

        if (includeHodl)
        {
            var myInvoices = (from inv in walletContext.Value.HodlInvoices where inv.PublicKey == this.PublicKey select inv);
            foreach (var inv in myInvoices)
            {
                if (allInvs.ContainsKey(inv.PaymentHash))
                {
                    allInvs[inv.PaymentHash].IsHodl = true;
                    ret.Add(inv.PaymentHash, allInvs[inv.PaymentHash]);
                }
            }
        }

        {
            var internalPayments = (from pay in walletContext.Value.InternalPayments
                                    select pay);
            foreach (var pay in internalPayments)
            {
                if (ret.ContainsKey(pay.PaymentHash))
                {
                    ret[pay.PaymentHash].State = (pay.Status == InternalPaymentStatus.InFlight) ? InvoiceState.Accepted : (pay.Status == InternalPaymentStatus.Succeeded ? InvoiceState.Settled : InvoiceState.Cancelled);
                }
            }
        }

        return ret.Values.ToArray();
    }

    public PaymentRecord[] ListNotFailedPayments()
    {
        var allPays = new Dictionary<string, PaymentRecord>(
            (from pay in LND.ListPayments(lndConf).Payments
             select KeyValuePair.Create(pay.PaymentHash, new PaymentRecord
             {
                 PaymentHash = pay.PaymentHash,
                 Satoshis = pay.ValueSat,
                 FeeMsat = pay.FeeMsat,
                 Status = (PaymentStatus)pay.Status,
                 FailureReason = (PaymentFailureReason)pay.FailureReason,
             })));

        var ret = new Dictionary<string, PaymentRecord>();

        {
            var myPayments = (from pay in walletContext.Value.ExternalPayments where pay.PublicKey == this.PublicKey select pay);
            foreach (var pay in myPayments)
            {
                if (allPays[pay.PaymentHash].Status != PaymentStatus.Failed)
                    if (allPays.ContainsKey(pay.PaymentHash))
                        ret.Add(pay.PaymentHash, allPays[pay.PaymentHash]);
            }
        }
        {
            var myPayments = (from pay in walletContext.Value.InternalPayments where pay.PublicKey == this.PublicKey select pay);
            foreach (var pay in myPayments)
            {
                ret.Add(pay.PaymentHash, new PaymentRecord
                {
                    PaymentHash = pay.PaymentHash,
                    Satoshis = pay.Satoshis,
                    FeeMsat = pay.PaymentFee * 1000,
                    Status = (PaymentStatus)pay.Status,
                    FailureReason = PaymentFailureReason.None,
                });
            }
        }

        return ret.Values.ToArray();
    }

    public async Task<PaymentRecord> GetPaymentAsync(string paymentHash)
    {
        var internalPayment = (from pay in walletContext.Value.InternalPayments
                               where pay.PaymentHash == paymentHash
                               select pay).FirstOrDefault();

        if (internalPayment != null)
        {
            if (internalPayment.PublicKey != PublicKey)
                throw new LNDWalletException(LNDWalletErrorCode.UnknownPayment);

            return new PaymentRecord
            {
                PaymentHash = internalPayment.PaymentHash,
                Satoshis = internalPayment.Satoshis,
                FeeMsat = internalPayment.PaymentFee * 1000,
                Status = (PaymentStatus)internalPayment.Status,
                FailureReason = PaymentFailureReason.None,
            };
        }
        else
        {
            var externalPayment = (from pay in walletContext.Value.ExternalPayments
                                   where pay.PaymentHash == paymentHash
                                   && pay.PublicKey == PublicKey
                                   select pay).FirstOrDefault();

            if (externalPayment == null)
                throw new LNDWalletException(LNDWalletErrorCode.UnknownPayment);

            var stream = LND.TrackPaymentV2(lndConf, paymentHash.AsBytes(), false);
            while (await stream.ResponseStream.MoveNext(trackPaymentsCancallationTokenSource.Token))
            {
                var cur = stream.ResponseStream.Current;
                return new PaymentRecord
                {
                    PaymentHash = cur.PaymentHash,
                    Satoshis = cur.ValueSat,
                    FeeMsat = cur.FeeMsat,
                    Status = (PaymentStatus)cur.Status,
                    FailureReason = (PaymentFailureReason)cur.FailureReason,
                };
            }
            return new PaymentRecord
            {
                PaymentHash = paymentHash,
                Satoshis = 0,
                FeeMsat = 0,
                Status = PaymentStatus.Failed,
                FailureReason = PaymentFailureReason.EmptyReturnStream,
            };
        }
    }

    public async Task<InvoiceRecord> GetInvoiceAsync(string paymentHash)
    {
        using var TX = walletContext.Value.BEGIN_TRANSACTION(IsolationLevel.Serializable);
        HodlInvoice? selfHodlInvoice = (from inv in walletContext.Value.HodlInvoices
                                        where inv.PaymentHash == paymentHash
                                        select inv).FirstOrDefault();

        var invoice = LND.LookupInvoiceV2(lndConf, paymentHash.AsBytes());
        if ((invoice.State != Lnrpc.Invoice.Types.InvoiceState.Open) && (invoice.State != Lnrpc.Invoice.Types.InvoiceState.Canceled))
        {
            TX.Commit();
            return new InvoiceRecord
            {
                PaymentHash = invoice.RHash.ToArray().AsHex(),
                IsHodl = selfHodlInvoice != null,
                PaymentRequest = invoice.PaymentRequest,
                Satoshis = invoice.Value,
                State = (InvoiceState)invoice.State,
                Memo = invoice.Memo,
                PaymentAddr = invoice.PaymentAddr.ToArray().AsHex(),
                CreationTime = DateTimeOffset.FromUnixTimeSeconds(invoice.CreationDate).UtcDateTime,
                ExpiryTime = DateTimeOffset.FromUnixTimeSeconds(invoice.CreationDate).UtcDateTime.AddSeconds(invoice.Expiry),
                SettleTime = DateTimeOffset.FromUnixTimeSeconds(invoice.SettleDate).UtcDateTime,
            };
        }

        // if InvoiceState is Open or Cancelled

        ClassicInvoice? selfClsInv = null;
        if (selfHodlInvoice == null)
            selfClsInv = (from inv in walletContext.Value.ClassicInvoices
                          where inv.PaymentHash == paymentHash
                          select inv).FirstOrDefault();

        if (selfHodlInvoice != null || selfClsInv != null) // our invoice
        {

            var payment = (from pay in walletContext.Value.InternalPayments
                           where pay.PaymentHash == paymentHash
                           select pay).FirstOrDefault();

            TX.Commit();
            return new InvoiceRecord
            {
                PaymentHash = invoice.RHash.ToArray().AsHex(),
                IsHodl = selfHodlInvoice != null,
                PaymentRequest = invoice.PaymentRequest,
                Satoshis = invoice.Value,
                State = payment == null ? (InvoiceState)invoice.State : (payment.Status == InternalPaymentStatus.InFlight ? InvoiceState.Accepted : InvoiceState.Settled),
                Memo = invoice.Memo,
                PaymentAddr = invoice.PaymentAddr.ToArray().AsHex(),
                CreationTime = DateTimeOffset.FromUnixTimeSeconds(invoice.CreationDate).UtcDateTime,
                ExpiryTime = DateTimeOffset.FromUnixTimeSeconds(invoice.CreationDate).UtcDateTime.AddSeconds(invoice.Expiry),
                SettleTime = DateTimeOffset.FromUnixTimeSeconds(invoice.SettleDate).UtcDateTime,
            };
        }
        throw new LNDWalletException(LNDWalletErrorCode.UnknownInvoice);
    }

    public AccountBalanceDetails GetBallance()
    {
        using var TX = walletContext.Value.BEGIN_TRANSACTION(IsolationLevel.Serializable);
        var bal= GetAccountBallance();
        TX.Commit();
        return bal;
    }

    private AccountBalanceDetails GetAccountBallance()
    {

        var channelfunds = GetExecutedTopupTotalAmount(6);
        var payedout = (from a in walletContext.Value.Payouts
                        where a.PublicKey == PublicKey
                        select a.Satoshis).Sum();

        var payedoutfee = (from a in walletContext.Value.Payouts
                        where a.PublicKey == PublicKey
                        select a.PayoutFee).Sum();

        var invoices = ListInvoices(true, true);
        var payments = ListNotFailedPayments();

        var earnedFromSettledInvoices = (from inv in invoices
                                         where inv.State == InvoiceState.Settled
                                         select inv.Satoshis).Sum();

        var sendOrLockedPayments = (from pay in payments
                                    select pay.Satoshis).Sum();

        var sendOrLockedPaymentFeesMsat = (from pay in payments
                                           select pay.FeeMsat).Sum();


        var incomingFunds = GetExecutedTopupTotalAmount(0) - channelfunds;

        var earnedFromAcceptedInvoices = (from inv in invoices
                                          where inv.State == InvoiceState.Accepted
                                          select inv.Satoshis).Sum();

        var lockedPayedout = (from a in walletContext.Value.Payouts
                              where a.PublicKey == PublicKey
                              && a.State != PayoutState.Sent
                              select a.Satoshis).Sum();

        var lockedPayedoutFee = (from a in walletContext.Value.Payouts
                              where a.PublicKey == PublicKey
                              && a.State != PayoutState.Sent
                              select a.PayoutFee).Sum();

        var lockedPayments = (from pay in payments
                              where pay.Status != PaymentStatus.Succeeded
                              select pay.Satoshis).Sum();

        var lockedPaymentFeesMsat = (from pay in payments
                                     where pay.Status != PaymentStatus.Succeeded
                                     select pay.FeeMsat).Sum();

        return new AccountBalanceDetails
        {
            AvailableAmount = channelfunds - payedout - payedoutfee + earnedFromSettledInvoices - sendOrLockedPayments - sendOrLockedPaymentFeesMsat / 1000,
            IncomingNotConfirmed = incomingFunds,
            IncomingAcceptedNotSettled = earnedFromAcceptedInvoices,

            OutgoingPayoutFees = payedoutfee,
            OutgoingPaymentFees = sendOrLockedPaymentFeesMsat / 1000,

            OutgoingInFlightPayments = lockedPayments,
            OutgoingInFlightPaymentFees = lockedPaymentFeesMsat / 1000,

            OutgoingInProgressPayouts = lockedPayedout,
            OutgoingInProgressPayoutFees = lockedPayedoutFee,
        };
    }

}

public class LNDWalletManager : LNDEventSource
{
    public BitcoinNode BitcoinNode;
    private LND.NodeSettings lndConf;
    private ThreadLocal<WaletContext> walletContext;
    private DBProvider provider;
    private string connectionString;
    private string adminPubkey;
    private CancellationTokenSource subscribeInvoicesCancallationTokenSource;
    private Thread subscribeInvoicesThread;
    private CancellationTokenSource trackPaymentsCancallationTokenSource;
    private Thread trackPaymentsThread;
    private ConcurrentDictionary<string, bool> alreadySubscribedSingleInvoices = new();
    private ConcurrentDictionary<string, CancellationTokenSource> alreadySubscribedSingleInvoicesTokenSources = new();

    public LNDWalletManager(DBProvider provider, string connectionString, BitcoinNode bitcoinNode, LND.NodeSettings lndConf, string adminPubkey)
    {
        this.provider = provider;
        this.connectionString = connectionString;
        this.walletContext = new ThreadLocal<WaletContext>(() => new WaletContext(provider, connectionString));
        this.BitcoinNode = bitcoinNode;
        this.lndConf = lndConf;
        this.adminPubkey = adminPubkey;
        walletContext.Value.Database.EnsureCreated();
    }

    public ListPeersResponse ListPeers()
    {
        return LND.ListPeers(lndConf);
    }

    public void Connect(string friend)
    {
        var pr = friend.Split('@');
        LND.Connect(lndConf, pr[1], pr[0]);
    }

    private void SubscribeSingleInvoiceTracking(string paymentHash)
    {
        alreadySubscribedSingleInvoices.GetOrAdd(paymentHash, (paymentHash) =>
        {
            Task.Run(async () =>
            {

                try
                {
                    alreadySubscribedSingleInvoicesTokenSources[paymentHash] = new CancellationTokenSource();
                    var stream = LND.SubscribeSingleInvoice(lndConf, paymentHash.AsBytes(), cancellationToken: alreadySubscribedSingleInvoicesTokenSources[paymentHash].Token);
                    while (await stream.ResponseStream.MoveNext(alreadySubscribedSingleInvoicesTokenSources[paymentHash].Token))
                    {
                        var inv = stream.ResponseStream.Current;
                        this.FireOnInvoiceStateChanged(inv.RHash.ToArray().AsHex(), (InvoiceState)inv.State);
                        if (inv.State == Invoice.Types.InvoiceState.Settled || inv.State == Invoice.Types.InvoiceState.Canceled)
                            break;
                    }
                }
                catch (RpcException e) when (e.Status.StatusCode == StatusCode.Cancelled)
                {
                    TraceEx.TraceInformation("Streaming was cancelled from the client!");
                }
                finally
                {
                    alreadySubscribedSingleInvoicesTokenSources.TryRemove(paymentHash, out _);
                    alreadySubscribedSingleInvoices.TryRemove(paymentHash, out _);
                }
            });
            return true;
        });
    }

    public override void CancelSingleInvoiceTracking(string paymentHash)
    {
        if (alreadySubscribedSingleInvoicesTokenSources.ContainsKey(paymentHash))
            alreadySubscribedSingleInvoicesTokenSources[paymentHash].Cancel();
    }

    public void Start()
    {
        alreadySubscribedSingleInvoicesTokenSources = new();
        subscribeInvoicesCancallationTokenSource = new CancellationTokenSource();
        try
        {
            walletContext.Value.INSERT(new TrackingIndex { Id = TackingIndexId.AddInvoice, Value = 0 }).SAVE();
            walletContext.Value.INSERT(new TrackingIndex { Id = TackingIndexId.SettleInvoice, Value = 0 }).SAVE();
        }
        catch (DbUpdateException)
        {
            //Ignore already inserted
        }
        subscribeInvoicesThread = new Thread(async () =>
        {
            try
            {
                var allInvs = new Dictionary<string, InvoiceRecord>(
                    from inv in LND.ListInvoices(lndConf).Invoices
                    select KeyValuePair.Create(inv.RHash.ToArray().AsHex(),
                        new InvoiceRecord
                        {
                            PaymentHash = inv.RHash.ToArray().AsHex(),
                            IsHodl = false,
                            PaymentRequest = inv.PaymentRequest,
                            Satoshis = inv.Value,
                            State = (InvoiceState)inv.State,
                            Memo = inv.Memo,
                            PaymentAddr = inv.PaymentAddr.ToArray().AsHex(),
                            CreationTime = DateTimeOffset.FromUnixTimeSeconds(inv.CreationDate).UtcDateTime,
                            ExpiryTime = DateTimeOffset.FromUnixTimeSeconds(inv.CreationDate).UtcDateTime.AddSeconds(inv.Expiry),
                            SettleTime = DateTimeOffset.FromUnixTimeSeconds(inv.SettleDate).UtcDateTime,

                        }));

                var internalPayments = new HashSet<string>(from pay in walletContext.Value.InternalPayments
                                                           where pay.Status == InternalPaymentStatus.InFlight
                                                           select pay.PaymentHash);

                foreach (var inv in allInvs.Values)
                {
                    if (!internalPayments.Contains(inv.PaymentHash) && inv.State == InvoiceState.Accepted)
                        SubscribeSingleInvoiceTracking(inv.PaymentHash);
                }

                while (true)
                {
                    try
                    {
                        var trackIdxes = new Dictionary<TackingIndexId, ulong>(from idx in walletContext.Value.TrackingIndexes
                                                                               select KeyValuePair.Create(idx.Id, idx.Value));

                        var stream = LND.SubscribeInvoices(lndConf, trackIdxes[TackingIndexId.AddInvoice], trackIdxes[TackingIndexId.SettleInvoice],
                            cancellationToken: subscribeInvoicesCancallationTokenSource.Token);

                        while (await stream.ResponseStream.MoveNext(subscribeInvoicesCancallationTokenSource.Token))
                        {
                            var inv = stream.ResponseStream.Current;
                            this.FireOnInvoiceStateChanged(inv.RHash.ToArray().AsHex(), (InvoiceState)inv.State);
                            if (inv.State == Lnrpc.Invoice.Types.InvoiceState.Accepted)
                                SubscribeSingleInvoiceTracking(inv.RHash.ToArray().AsHex());

                            walletContext.Value.UPDATE(new TrackingIndex { Id = TackingIndexId.AddInvoice, Value = inv.AddIndex }).SAVE();
                            walletContext.Value.UPDATE(new TrackingIndex { Id = TackingIndexId.SettleInvoice, Value = inv.SettleIndex }).SAVE();
                        }
                    }
                    catch (RpcException e) when (e.Status.StatusCode == StatusCode.Unavailable)
                    {
                        //retry
                    }
                }
            }
            catch (RpcException e) when (e.Status.StatusCode == StatusCode.Cancelled)
            {
                TraceEx.TraceInformation("Streaming was cancelled from the client!");
            }

        });

        trackPaymentsCancallationTokenSource = new CancellationTokenSource();
        trackPaymentsThread = new Thread(async () =>
        {
            try
            {
                while (true)
                {
                    try
                    {
                        var stream = LND.TrackPayments(lndConf, cancellationToken: trackPaymentsCancallationTokenSource.Token);
                        while (await stream.ResponseStream.MoveNext(trackPaymentsCancallationTokenSource.Token))
                        {
                            var pm = stream.ResponseStream.Current;
                            this.FireOnPaymentStatusChanged(pm.PaymentHash, (PaymentStatus)pm.Status, (PaymentFailureReason)pm.FailureReason);
                        }
                    }
                    catch (RpcException e) when (e.Status.StatusCode == StatusCode.Unavailable)
                    {
                        //retry
                    }
                }
            }
            catch (RpcException e) when (e.Status.StatusCode == StatusCode.Cancelled)
            {
                TraceEx.TraceInformation("Streaming was cancelled from the client!");
            }

        });

        subscribeInvoicesThread.Start();
        trackPaymentsThread.Start();

    }

    public void Stop()
    {
        foreach (var s in alreadySubscribedSingleInvoicesTokenSources)
            s.Value.Cancel();

        subscribeInvoicesCancallationTokenSource.Cancel();
        trackPaymentsCancallationTokenSource.Cancel();
        subscribeInvoicesThread.Join();
        trackPaymentsThread.Join();
    }

    private string ValidateAuthToken(string authTokenBase64)
    {
        var timedToken = GigGossip.AuthToken.Verify(authTokenBase64, 120.0);
        if (timedToken == null)
            throw new LNDWalletException(LNDWalletErrorCode.InvalidToken);

        var tk = (from token in walletContext.Value.Tokens where token.PublicKey == GigGossip.ProtoBufExtensions.AsHex(timedToken.Header.PublicKey) && token.Id == GigGossip.ProtoBufExtensions.AsGuid(timedToken.Header.TokenId) select token).FirstOrDefault();
        if (tk == null)
            throw new LNDWalletException(LNDWalletErrorCode.InvalidToken);
        return tk.PublicKey;
    }

    public string ValidateAuthToken(string authTokenBase64, bool admin = false)
    {
        var pubkey = ValidateAuthToken(authTokenBase64);
        if (admin)
            if (!HasAdminRights(pubkey))
                throw new LNDWalletException(LNDWalletErrorCode.AccessDenied);
        return pubkey;
    }

    public LNDAccountManager ValidateAuthTokenAndGetAccount(string authTokenBase64)
    {
        return GetAccount(ValidateAuthToken(authTokenBase64).AsECXOnlyPubKey());
    }

    public LNDAccountManager ValidateAuthTokenAndGetAccount(string authTokenBase64, bool admin)
    {
        return GetAccount(ValidateAuthToken(authTokenBase64, admin).AsECXOnlyPubKey());
    }

    public LNDAccountManager GetAccount(ECXOnlyPubKey pubkey)
    {
        return new LNDAccountManager(BitcoinNode, lndConf, this.provider, this.connectionString, pubkey, this);
    }

    public bool HasAdminRights(string pubkey)
    {
        return pubkey == this.adminPubkey;
    }

    public Guid GetTokenGuid(string pubkey)
    {
        var t = (from token in walletContext.Value.Tokens where pubkey == token.PublicKey select token).FirstOrDefault();
        if (t == null)
        {
            t = new Token() { Id = Guid.NewGuid(), PublicKey = pubkey };
            walletContext.Value
                .INSERT(t)
                .SAVE();
        }
        return t.Id;
    }

    public string SendCoins(string address, long satoshis, ulong satspervbyte, string memo)
    {
        return LND.SendCoins(lndConf, address, memo, satoshis, satspervbyte);
    }

    public Guid OpenReserve(long satoshis)
    {
        var myid = Guid.NewGuid();

        walletContext.Value
            .INSERT(new LNDWallet.Reserve()
            {
                ReserveId = myid,
                Satoshis = satoshis
            })
            .SAVE();
        return myid;
    }

    public void CloseReserve(Guid id)
    {
        walletContext.Value
            .DELETE_IF_EXISTS(from po in walletContext.Value.Reserves where po.ReserveId == id select po)
            .SAVE();
    }

    internal bool MarkPayoutAsSending(Guid id)
    {
        var payout = (from po in walletContext.Value.Payouts where po.PayoutId == id && po.State == PayoutState.Open select po).FirstOrDefault();
        if (payout == null)
            return false;
        payout.State = PayoutState.Sending;
        walletContext.Value
            .UPDATE(payout)
            .SAVE();
        return true;
    }

    internal bool MarkPayoutAsFailure(Guid id, string tx)
    {
        var payout = (from po in walletContext.Value.Payouts where po.PayoutId == id && po.State == PayoutState.Open select po).FirstOrDefault();
        if (payout == null)
            return false;
        payout.State = PayoutState.Failure;
        payout.Tx = tx;
        walletContext.Value
            .UPDATE(payout)
            .SAVE();
        return true;
    }

    internal void MarkPayoutAsSent(Guid id, string tx)
    {
        using var TX = walletContext.Value.BEGIN_TRANSACTION();

        var payout = (from po in walletContext.Value.Payouts where po.PayoutId == id && po.State == PayoutState.Open select po).FirstOrDefault();
        if (payout == null)
            throw new LNDWalletException(LNDWalletErrorCode.PayoutAlreadySent);
        payout.State = PayoutState.Sent;
        payout.Tx = tx;
        walletContext.Value
            .UPDATE(payout)
            .SAVE();

        CloseReserve(id);

        TX.Commit();
    }

    public (long feeSat, ulong satpervbyte) EstimateFee(string addr, long satoshis)
    {
        try
        {
            var est = LND.FeeEstimate(lndConf, new List<(string, long)>() { (addr, satoshis) }, 6, 6);
            return (est.FeeSat, est.SatPerVbyte);
        }
        catch (RpcException ex)
        {
            throw new LNDWalletException(LNDWalletErrorCode.OperationFailed, ex.Status.Detail);
        }
    }

    /*
    public long GetTransactions(int minConf)
    {
        var myaddrs = new Dictionary<string, long>(
            from a in walletContext.Value.FundingAddresses
            where a.PublicKey == PublicKey
            select new KeyValuePair<string, long>(a.BitcoinAddress, a.TxFee));

        var transactuinsResp = LND.GetTransactions(conf);
        long balance = 0;
        foreach (var transation in transactuinsResp.Transactions)
            if (transation.NumConfirmations >= minConf)
                foreach (var outp in transation.OutputDetails)
                    if (outp.IsOurAddress)
                        if (myaddrs.ContainsKey(outp.Address))
                        {
                            balance += outp.Amount;
                            balance -= (long)myaddrs[outp.Address];
                        }
        return balance;
    }
    */

    public long GetChannelFundingBalance(int minconf)
    {
        return (from utxo in LND.ListUnspent(lndConf, minconf).Utxos select utxo.AmountSat).Sum();
    }

    public WalletBalanceResponse GetWalletBalance()
    {
        return LND.WalletBallance(lndConf);
    }

    public long GetRequiredReserve(uint additionalChannelsNum)
    {
        return LND.RequiredReserve(lndConf, additionalChannelsNum).RequiredReserve;
    }

    public long GetRequestedReserveAmount()
    {
        return (from r in this.walletContext.Value.Reserves select r.Satoshis).FromCache(this.walletContext.Value.Reserves).Sum();
    }

    public List<Reserve> GetRequestedReserves()
    {
        return (from r in this.walletContext.Value.Reserves select r).FromCache(this.walletContext.Value.Reserves).ToList();
    }

    public List<Payout> GetPendingPayouts(List<Guid> payoutIds)
    {
        return (from p in this.walletContext.Value.Payouts where payoutIds.Contains(p.PayoutId) select p).FromCache(this.walletContext.Value.Payouts).ToList();
    }

    public AsyncServerStreamingCall<OpenStatusUpdate> OpenChannel(string nodePubKey, long fundingSatoshis)
    {
        return LND.OpenChannel(lndConf, nodePubKey, fundingSatoshis);
    }

    public BatchOpenChannelResponse BatchOpenChannel(List<(string, long)> amountsPerNode)
    {
        return LND.BatchOpenChannel(lndConf, amountsPerNode);
    }

    public string OpenChannelSync(string nodePubKey, long fundingSatoshis)
    {
        var channelpoint = LND.OpenChannelSync(lndConf, nodePubKey, fundingSatoshis);
        string channelTx;
        if (channelpoint.HasFundingTxidBytes)
            channelTx = channelpoint.FundingTxidBytes.ToByteArray().Reverse().ToArray().AsHex();
        else
            channelTx = channelpoint.FundingTxidStr;

        var chanpoint = channelTx + ":" + channelpoint.OutputIndex;

        return chanpoint;
    }

    public ListChannelsResponse ListChannels(bool openOnly)
    {
        return LND.ListChannels(lndConf, openOnly);
    }

    public AsyncServerStreamingCall<CloseStatusUpdate> CloseChannel(string chanpoint, ulong maxFeePerVByte)
    {
        return LND.CloseChannel(lndConf, chanpoint, maxFeePerVByte);
    }

    public decimal EstimateFeeSatoshiPerByte()
    {
        return BitcoinNode.EstimateFeeSatoshiPerByte(6);
    }


    public void GoForCancellingInternalInvoices()
    {
        var allInvs = new Dictionary<string, InvoiceRecord>(
                   (from inv in LND.ListInvoices(lndConf).Invoices
                    select KeyValuePair.Create(inv.RHash.ToArray().AsHex(),
                    new InvoiceRecord
                    {
                        PaymentHash = inv.RHash.ToArray().AsHex(),
                        IsHodl = false,
                        PaymentRequest = inv.PaymentRequest,
                        Satoshis = inv.Value,
                        State = (InvoiceState)inv.State,
                        Memo = inv.Memo,
                        PaymentAddr = inv.PaymentAddr.ToArray().AsHex(),
                        CreationTime = DateTimeOffset.FromUnixTimeSeconds(inv.CreationDate).UtcDateTime,
                        ExpiryTime = DateTimeOffset.FromUnixTimeSeconds(inv.CreationDate).UtcDateTime.AddSeconds(inv.Expiry),
                        SettleTime = DateTimeOffset.FromUnixTimeSeconds(inv.SettleDate).UtcDateTime,
                    })));


        {
            var internalPayments = (from pay in walletContext.Value.InternalPayments
                                    select pay).FromCache(walletContext.Value.InternalPayments);
            foreach (var pay in internalPayments)
            {
                try
                {
                    if (allInvs.ContainsKey(pay.PaymentHash))
                    {
                        var inv = allInvs[pay.PaymentHash];
                        if (pay.Status == InternalPaymentStatus.InFlight)
                        {
                            if (inv.ExpiryTime < DateTime.UtcNow)
                            {
                                using var TX = walletContext.Value.BEGIN_TRANSACTION();

                                walletContext.Value
                                    .DELETE_IF_EXISTS(from p in walletContext.Value.InternalPayments
                                                     where p.PaymentHash == pay.PaymentHash
                                                     select p)
                                    .INSERT(new FailedPayment
                                    {
                                        Id = Guid.NewGuid(),
                                        DateTime = DateTime.UtcNow,
                                        PaymentHash = pay.PaymentHash,
                                        PublicKey = pay.PublicKey,
                                        FailureReason = PaymentFailureReason.Canceled,
                                    })
                                    .SAVE();

                                this.CancelSingleInvoiceTracking(pay.PaymentHash);
                                this.FireOnPaymentStatusChanged(pay.PaymentHash, PaymentStatus.Failed, PaymentFailureReason.Canceled);

                                TX.Commit();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    TraceEx.TraceException(ex);
                }
            }
        }
    }
}

