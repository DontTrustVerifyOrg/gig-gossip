using NBitcoin.Secp256k1;
using CryptoToolkit;
using System.Data;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using LNDClient;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using NBitcoin.RPC;
using Grpc.Core;
using Lnrpc;
using Invoicesrpc;
using System.Collections.Generic;
using Walletrpc;
using System.Security.Principal;
using System;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Collections.Concurrent;

namespace LNDWallet;

public enum InvoiceState
{
    Open=0,
    Settled=1,
    Cancelled=2,
    Accepted = 3,
}

public enum PaymentStatus
{
    Unknown = 0,
    InFlight = 1,
    Succeeded =2,
    Failed =3,
}

public class InvoiceStateChangedEventArgs : EventArgs
{
    public required string PaymentHash { get; set; }
    public required InvoiceState NewState { get; set; }
}
public delegate void InvoiceStateChangedEventHandler(object sender, InvoiceStateChangedEventArgs e);

public class PaymentStatusChangedEventArgs : EventArgs
{
    public required string PaymentHash { get; set; }
    public required PaymentStatus NewStatus { get; set; }
}
public delegate void PaymentStatusChangedEventHandler(object sender, PaymentStatusChangedEventArgs e);

public class LNDEventSource
{
    public event InvoiceStateChangedEventHandler OnInvoiceStateChanged;
    public event PaymentStatusChangedEventHandler OnPaymentStatusChanged;

    public void FireOnInvoiceStateChanged(string paymentHash, InvoiceState invstate)
    {
        if (OnInvoiceStateChanged != null)
            OnInvoiceStateChanged.Invoke(this, new InvoiceStateChangedEventArgs()
            {
                PaymentHash = paymentHash,
                NewState = invstate
            });
    }

    public void FireOnPaymentStatusChanged(string paymentHash, PaymentStatus paystatus)
    {
        if (OnPaymentStatusChanged != null)
            OnPaymentStatusChanged.Invoke(this, new PaymentStatusChangedEventArgs()
            {
                PaymentHash = paymentHash,
                NewStatus = paystatus
            });
    }
}

public class LNDAccountManager
{
    private LND.NodeSettings conf;
    private ThreadLocal<WaletContext> walletContext;
    public string PublicKey;
    private LNDEventSource eventSource;

    internal LNDAccountManager(LND.NodeSettings conf, string connectionString, ECXOnlyPubKey pubKey, LNDEventSource eventSource)
    {
        this.conf = conf;
        this.PublicKey = pubKey.AsHex();
        this.walletContext = new ThreadLocal<WaletContext>(() => new WaletContext(connectionString));
        this.eventSource = eventSource;
    }

    public string NewAddress(long txfee)
    {
            var newaddress = LND.NewAddress(conf);
            walletContext.Value.AddObject(new Address() { BitcoinAddress = newaddress, PublicKey = PublicKey, TxFee = txfee });
            return newaddress;
    }

    public Guid RegisterPayout(long satoshis, string btcAddress, long txfee)
    {
        if ((GetAccountBallance() - (long)txfee) < (long)satoshis)
            throw new LNDWalletException(LNDWalletErrorCode.NotEnoughFunds);

        var myid = Guid.NewGuid();

        walletContext.Value.AddObject(new LNDWallet.Payout()
        {
            PayoutId = myid,
            BitcoinAddress = btcAddress,
            PublicKey = PublicKey,
            TxFee = txfee,
            State =  PayoutState.Open,
            Satoshis = satoshis
        });

        walletContext.Value.AddObject(new LNDWallet.Reserve()
        {
            ReserveId = myid,
            Satoshis = satoshis
        });
        return myid;
    }

    public long GetExecutedPayedInFundingAmount(int minConf)
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

    public long GetPayedOutAmount()
    {
            return (from a in walletContext.Value.Payouts
                    where a.PublicKey == PublicKey
                    select ((long)(a.Satoshis + a.TxFee))).Sum();
    }

    /*
    public (long all, long confirmed) GetExecutedPayedInFundingAmount(int minConf)
    {
        var transactuinsResp = LND.GetTransactions(conf);
        long confirmed = 0;
        long confirmedTxFee = 0;
        long all = 0;
        long allTxFee = 0;
        foreach (var transation in transactuinsResp.Transactions)
            foreach (var outp in transation.OutputDetails)
                if (outp.IsOurAddress)
                    if (txFees.ContainsKey(outp.Address))
                    {
                        if (transation.NumConfirmations >= minConf)
                        {
                            confirmed += outp.Amount;
                            confirmedTxFee += (long)txFees[outp.Address];
                        }
                        all += outp.Amount;
                        allTxFee += (long)txFees[outp.Address];
                    }
        return (all, allTxFee, confirmed, confirmedTxFee);
    }*/

/*    public (long amount, long amountTxFee) GetPayedOutAmount()
    {
        var amount = (from a in walletContext.Value.Payouts
                      where a.PublicKey == PublicKey
                      select ((long)(a.Satoshis))).Sum();
        var amountTxFee = (from a in walletContext.Value.Payouts
                           where a.PublicKey == PublicKey
                           select ((long)(a.TxFee))).Sum();
        return (amount, amountTxFee);
    }*/

    public (long amount, long amountTxFee) GetPendingPayedOutAmount()
    {
        var amount = (from a in walletContext.Value.Payouts
                      where a.PublicKey == PublicKey && a.State!= PayoutState.Sent
                      select ((long)(a.Satoshis))).Sum();
        var amountTxFee = (from a in walletContext.Value.Payouts
                           where a.PublicKey == PublicKey && a.State != PayoutState.Sent
                           select ((long)(a.TxFee))).Sum();
        return (amount, amountTxFee);
    }

    public (long all, long allTxFee, long confirmed, long confirmedTxFee) GetExecutedPayedOutAmount(int minConf)
    {
        Dictionary<string, LNDWallet.Payout> mypayouts;
        mypayouts = new Dictionary<string, LNDWallet.Payout>(
            from a in walletContext.Value.Payouts
            where a.PublicKey == PublicKey
            select new KeyValuePair<string, LNDWallet.Payout>(a.BitcoinAddress, a));

        var transactuinsResp = LND.GetTransactions(conf);
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
                            confirmedTxFee += (long)mypayouts[outp.Address].TxFee;
                        }
                        all += outp.Amount;
                        allTxFee += (long)mypayouts[outp.Address].TxFee;
                    }
        return (all, allTxFee, confirmed, confirmedTxFee);
    }

    public Invoicesrpc.AddHoldInvoiceResp AddHodlInvoice(long satoshis, string memo, byte[] hash, long txfee, long expiry)
    {
        var inv = LND.AddHodlInvoice(conf, satoshis, memo, hash, expiry);
            walletContext.Value.AddObject(new Invoice() {
                PaymentHash = hash.AsHex(),
                PublicKey = PublicKey,
                PaymentRequest = inv.PaymentRequest,
                Satoshis = (long)satoshis,
                State = InvoiceState.Open,
                TxFee = txfee,
                IsHodlInvoice = true,
                IsSelfManaged = false,
            }) ;
        return inv;
    }

    public Lnrpc.AddInvoiceResponse AddInvoice(long satoshis, string memo, long txfee, long expiry)
    {
        var inv = LND.AddInvoice(conf, satoshis, memo, expiry);
        walletContext.Value.AddObject(new Invoice()
        {
            PaymentHash = inv.RHash.ToArray().AsHex(),
            PublicKey = PublicKey,
            PaymentRequest = inv.PaymentRequest,
            Satoshis = (long)satoshis,
            State = InvoiceState.Open,
            TxFee = txfee,
            IsHodlInvoice = false,
            IsSelfManaged = false,
        });
        return inv;
    }

    public PayReq DecodeInvoice(string paymentRequest)
    {
        return LND.DecodeInvoice(conf, paymentRequest);
    }

    CancellationTokenSource trackPaymentsCancallationTokenSource = new();

    public async Task SendPaymentAsync(string paymentRequest, int timeout, long txfee, long feelimit)
    {
        var decinv = LND.DecodeInvoice(conf, paymentRequest);
        var accountBallance = GetAccountBallance();
        if (decinv.NumSatoshis > accountBallance -feelimit)
            throw new LNDWalletException(LNDWalletErrorCode.NotEnoughFunds);

        var selfInvQuery = (from inv in walletContext.Value.Invoices
                            where inv.PaymentHash == decinv.PaymentHash
                            select inv);
        var selfInv = selfInvQuery.FirstOrDefault();
        if (selfInv != null) // selfpayment
        {
            if (selfInv.State == InvoiceState.Settled)
                throw new LNDWalletException(LNDWalletErrorCode.InvoiceAlreadySettled);

            if (selfInv.State == InvoiceState.Cancelled)
                throw new LNDWalletException(LNDWalletErrorCode.InvoiceAlreadyCancelled);

            if ((selfInv.State == InvoiceState.Accepted))
                throw new LNDWalletException(LNDWalletErrorCode.InvoiceAlreadyAccepted);

            selfInv.State = selfInv.IsHodlInvoice ? InvoiceState.Accepted : InvoiceState.Settled;
            selfInv.IsSelfManaged = true;
            walletContext.Value.SaveObject(selfInv);
            eventSource.FireOnInvoiceStateChanged(selfInv.PaymentHash, InvoiceState.Accepted);
            if(!selfInv.IsHodlInvoice)
                eventSource.FireOnInvoiceStateChanged(selfInv.PaymentHash, InvoiceState.Settled);

            walletContext.Value.AddObject(new Payment()
            {
                PaymentHash = decinv.PaymentHash,
                PublicKey = PublicKey,
                Satoshis = selfInv.Satoshis,
                TxFee = txfee,
                IsSelfManaged = true,
                PaymentFee = 0,
                Status = selfInv.IsHodlInvoice ? PaymentStatus.InFlight : PaymentStatus.Succeeded,
            });
            eventSource.FireOnPaymentStatusChanged(selfInv.PaymentHash,
                selfInv.IsHodlInvoice ? PaymentStatus.InFlight : PaymentStatus.Succeeded);
        }
        else
        {
            var stream = LND.SendPaymentV2(conf, paymentRequest, timeout, (long)feelimit);
            LNDWallet.PaymentStatus paymentStatus = PaymentStatus.Unknown;
            while (await stream.ResponseStream.MoveNext(trackPaymentsCancallationTokenSource.Token))
            {
                var status = stream.ResponseStream.Current;
                if (status.Status != Lnrpc.Payment.Types.PaymentStatus.Unknown)
                    paymentStatus = (LNDWallet.PaymentStatus)((int)status.Status);
                break;
            }
            walletContext.Value.AddObject(new Payment()
            {
                PaymentHash = decinv.PaymentHash,
                PublicKey = PublicKey,
                Satoshis = (long)decinv.NumSatoshis,
                TxFee = txfee,
                IsSelfManaged = false,
                PaymentFee = feelimit,
                Status = PaymentStatus.Unknown,
            });
        }
    }

    public void SettleInvoice(byte[] preimage)
    {
        var paymentHash = Crypto.ComputePaymentHash(preimage).AsHex();
        var invoice = (from inv in walletContext.Value.Invoices
                              where inv.PaymentHash == paymentHash && inv.IsSelfManaged
                              select inv).FirstOrDefault();

        if(invoice!=null)
        {
            if (invoice.PublicKey != PublicKey)
                throw new LNDWalletException(LNDWalletErrorCode.UnknownInvoice);

            if (invoice.State == InvoiceState.Settled)
                return;

            if (invoice.State == InvoiceState.Cancelled)
                throw new LNDWalletException(LNDWalletErrorCode.InvoiceAlreadyCancelled);

            if ((invoice.State != InvoiceState.Accepted))
                throw new LNDWalletException(LNDWalletErrorCode.InvoiceNotAccepted);

            invoice.State = InvoiceState.Settled;
            invoice.Preimage = preimage;
            walletContext.Value.SaveObject(invoice);
            eventSource.FireOnInvoiceStateChanged(paymentHash, InvoiceState.Settled);
            var selfPayQuery = (from pay in walletContext.Value.Payments
                                where pay.PaymentHash == paymentHash && pay.IsSelfManaged
                                select pay);
            if (selfPayQuery.ExecuteUpdate(
                i => i
                .SetProperty(a => a.Status, a => PaymentStatus.Succeeded)
                ) > 0)
                eventSource.FireOnPaymentStatusChanged(paymentHash, PaymentStatus.Succeeded);
        }
        else
        {
            //this happens the invoice is not self managed so the update returned 0 rows changed
            LND.SettleInvoice(conf, preimage);
        }
    }

    public void CancelInvoice(string paymentHash)
    {
        var invoice = (from inv in walletContext.Value.Invoices
                       where inv.PaymentHash == paymentHash && inv.IsSelfManaged
                       select inv).FirstOrDefault();

        if (invoice != null)
        {
            if (invoice.PublicKey != PublicKey)
                throw new LNDWalletException(LNDWalletErrorCode.UnknownInvoice);

            if (invoice.State == InvoiceState.Settled)
                throw new LNDWalletException(LNDWalletErrorCode.InvoiceAlreadySettled);

            invoice.State = InvoiceState.Cancelled;
            walletContext.Value.SaveObject(invoice);
            eventSource.FireOnInvoiceStateChanged(paymentHash, InvoiceState.Cancelled);
            var selfPayQuery = (from pay in walletContext.Value.Payments
                                where pay.PaymentHash == paymentHash && pay.IsSelfManaged
                                select pay);
            if (selfPayQuery.ExecuteUpdate(
                i => i
                .SetProperty(a => a.Status, a => PaymentStatus.Failed)
                ) > 0)
                eventSource.FireOnPaymentStatusChanged(paymentHash, PaymentStatus.Failed);
        }
        else
        {
            //this happens the invoice is not self managed so the update returned 0 rows changed
            LND.CancelInvoice(conf, paymentHash.AsBytes());
        }
    }

    public PaymentStatus GetPaymentStatus(string paymentHash)
    {
            var pm = (from pay in walletContext.Value.Payments where pay.PaymentHash == paymentHash select pay).FirstOrDefault();
            if (pm != null)
                return pm.Status;
            else
                throw new LNDWalletException(LNDWalletErrorCode.UnknownPayment);
    }

    public InvoiceState GetInvoiceState(string paymentHash)
    {
            var iv = (from inv in walletContext.Value.Invoices where inv.PaymentHash == paymentHash select inv).FirstOrDefault();
            if (iv != null)
                return iv.State;
            else
                throw new LNDWalletException(LNDWalletErrorCode.UnknownInvoice);
    }

    public long GetAccountBallance()
    {
            var channelfunds = GetExecutedPayedInFundingAmount(6);
            var alreadypayedout = GetPayedOutAmount();

            var earnedFromSettledInvoices = (from inv in walletContext.Value.Invoices
                                            where inv.PublicKey == PublicKey && inv.State == InvoiceState.Settled
                                            select (long)inv.Satoshis-(long)inv.TxFee).Sum();

            var sendOrLocedPayments = (from pay in walletContext.Value.Payments
                                            where pay.PublicKey == PublicKey && (pay.Status == PaymentStatus.Succeeded || pay.Status == PaymentStatus.InFlight)
                                            select (long)pay.Satoshis + ((long)pay.PaymentFee + (long)pay.TxFee)).Sum();

            return channelfunds - alreadypayedout + earnedFromSettledInvoices - sendOrLocedPayments;
    }

    public AccountBallanceDetails GetAccountBallanceDetails()
    {
        throw new NotImplementedException();

        /*        var (allChannelFunds, allChannelFundsTxFee, confirmedChannelFunds, confirmedChannelFundsTxFee) = GetExecutedPayedInFundingAmount(6);
                var (payedOutAmount, payedOutAmountTxFee) = GetPayedOutAmount();
                var (pendingPayOutAmount, pendingPayOutAmountTxFee) = GetPendingPayedOutAmount();
                var (allPayOutFunds, allPayOutFundsTxFee, confirmedPayOutFunds, confirmedPayOutFundsTxFee) = GetExecutedPayedOutAmount(6);

                var earnedFromSettledInvoices = (from inv in walletContext.Value.Invoices
                                                 where inv.PublicKey == PublicKey && inv.State == InvoiceState.Settled
                                                 select (long)inv.Satoshis).Sum();

                var earnedFromSettledInvoicesTxFee = (from inv in walletContext.Value.Invoices
                                                      where inv.PublicKey == PublicKey && inv.State == InvoiceState.Settled
                                                      select (long)inv.TxFee).Sum();

                var earnedFromAcceptedOrSettledInvoices = (from inv in walletContext.Value.Invoices
                                                           where inv.PublicKey == PublicKey && (inv.State == InvoiceState.Accepted || inv.State == InvoiceState.Settled)
                                                           select (long)inv.Satoshis).Sum();

                var earnedFromAcceptedOrSettledInvoicesTxFee = (from inv in walletContext.Value.Invoices
                                                                where inv.PublicKey == PublicKey && (inv.State == InvoiceState.Accepted || inv.State == InvoiceState.Settled)
                                                                select (long)inv.TxFee).Sum();

                var succesfulPayments = (from pay in walletContext.Value.Payments
                                         where pay.PublicKey == PublicKey && (pay.Status == PaymentStatus.Succeeded)
                                         select (long)pay.Satoshis).Sum();

                var succesfulPaymentsFee = (from pay in walletContext.Value.Payments
                                            where pay.PublicKey == PublicKey && (pay.Status == PaymentStatus.Succeeded)
                                            select (long)pay.PaymentFee).Sum();

                var succesfulPaymentsTxFee = (from pay in walletContext.Value.Payments
                                              where pay.PublicKey == PublicKey && (pay.Status == PaymentStatus.Succeeded)
                                              select (long)pay.TxFee).Sum();

                var succesfulOrFlyingPayments = (from pay in walletContext.Value.Payments
                                                 where pay.PublicKey == PublicKey && (pay.Status == PaymentStatus.Succeeded || pay.Status == PaymentStatus.InFlight)
                                                 select (long)pay.Satoshis).Sum();

                var succesfulOrFlyingPaymentsFee = (from pay in walletContext.Value.Payments
                                                    where pay.PublicKey == PublicKey && (pay.Status == PaymentStatus.Succeeded || pay.Status == PaymentStatus.InFlight)
                                                    select (long)pay.PaymentFee).Sum();

                var succesfulOrFlyingPaymentsTxFee = (from pay in walletContext.Value.Payments
                                                      where pay.PublicKey == PublicKey && (pay.Status == PaymentStatus.Succeeded || pay.Status == PaymentStatus.InFlight)
                                                      select (long)pay.TxFee).Sum();

                return new AccountBallanceDetails
                {
                    AllChannelFunds = allChannelFunds,
                    AllChannelFundsTxFee = allChannelFundsTxFee,
                    ConfirmedChannelFunds = confirmedChannelFunds,
                    ConfirmedChannelFundsTxFee = confirmedChannelFundsTxFee,

                    PayedOutAmount = payedOutAmount,
                    PayedOutAmountTxFee = payedOutAmountTxFee,
                    PendingPayOutAmount = pendingPayOutAmount,
                    PendingPayOutAmountTxFee = pendingPayOutAmountTxFee,

                    AllPayOutFunds = allPayOutFunds,
                    AllPayOutFundsTxFee = allPayOutFundsTxFee,
                    ConfirmedPayOutFunds = confirmedPayOutFunds,
                    ConfirmedPayOutFundsTxFee = confirmedPayOutFundsTxFee,

                    EarnedFromSettledInvoices = earnedFromSettledInvoices,
                    EarnedFromSettledInvoicesTxFee = earnedFromSettledInvoicesTxFee,

                    EarnedFromAcceptedOrSettledInvoices = earnedFromAcceptedOrSettledInvoices,
                    EarnedFromAcceptedOrSettledInvoicesTxFee = earnedFromAcceptedOrSettledInvoicesTxFee,

                    SuccesfulPayments = succesfulPayments,
                    SuccesfulPaymentsFee = succesfulPaymentsFee,
                    SuccesfulPaymentsTxFee = succesfulPaymentsTxFee,
                    SuccesfulOrFlyingPayments = succesfulOrFlyingPayments,
                    SuccesfulOrFlyingPaymentsFee = succesfulOrFlyingPaymentsFee,
                    SuccesfulOrFlyingPaymentsTxFee = succesfulOrFlyingPaymentsTxFee,
                };*/
    }
}

[Serializable]
public class AccountBallanceDetails
{
    public long AllChannelFunds { get; set; }
    public long AllChannelFundsTxFee { get; set; }
    public long ConfirmedChannelFunds { get; set; }
    public long ConfirmedChannelFundsTxFee { get; set; }

    public long PayedOutAmount { get; set; }
    public long PayedOutAmountTxFee { get; set; }
    public long PendingPayOutAmount { get; set; }
    public long PendingPayOutAmountTxFee { get; set; }

    public long AllPayOutFunds { get; set; }
    public long AllPayOutFundsTxFee { get; set; }
    public long ConfirmedPayOutFunds { get; set; }
    public long ConfirmedPayOutFundsTxFee { get; set; }

    public long EarnedFromSettledInvoices { get; set; }
    public long EarnedFromSettledInvoicesTxFee { get; set; }

    public long EarnedFromAcceptedOrSettledInvoices { get; set; }
    public long EarnedFromAcceptedOrSettledInvoicesTxFee { get; set; }

    public long SuccesfulPayments { get; set; }
    public long SuccesfulPaymentsFee { get; set; }
    public long SuccesfulPaymentsTxFee { get; set; }
    public long SuccesfulOrFlyingPayments { get; set; }
    public long SuccesfulOrFlyingPaymentsFee { get; set; }
    public long SuccesfulOrFlyingPaymentsTxFee { get; set; }
}

public class LNDWalletManager : LNDEventSource
{
    private LND.NodeSettings conf;
    private ThreadLocal<WaletContext> walletContext;
    private string connectionString;
    private CancellationTokenSource subscribeInvoicesCancallationTokenSource;
    private Thread subscribeInvoicesThread;
    private CancellationTokenSource trackPaymentsCancallationTokenSource;
    private Thread trackPaymentsThread;

    public LNDWalletManager(string connectionString, LND.NodeSettings conf)
    {
        this.connectionString = connectionString;
        this.walletContext = new ThreadLocal<WaletContext>(() => new WaletContext(connectionString));
        this.conf = conf;
        walletContext.Value.Database.EnsureCreated();
    }

    public ListPeersResponse ListPeers()
    {
        return LND.ListPeers(conf);
    }

    public void Connect(string friend)
    {
        var pr = friend.Split('@');
        LND.Connect(conf, pr[1], pr[0]);
    }

    ConcurrentDictionary<string, bool> alreadySubscribed = new();

    void SubscribeSingleInvoice(string paymentHash)
    {
        alreadySubscribed.GetOrAdd(paymentHash, (paymentHash) =>
        {
            Task.Run(async () =>
            {
                try
                {
                    {
                        var inv = LND.LookupInvoiceV2(conf, paymentHash.AsBytes());
                        if ((inv.State == Lnrpc.Invoice.Types.InvoiceState.Settled) || (inv.State == Lnrpc.Invoice.Types.InvoiceState.Canceled))
                        {
                            var linv = (from i in walletContext.Value.Invoices
                                        where i.PaymentHash == inv.RHash.ToArray().AsHex()
                                        && i.State != (InvoiceState)inv.State
                                        select i).FirstOrDefault();
                            if (linv != null)
                            {
                                linv.State = (InvoiceState)((int)inv.State);
                                walletContext.Value.SaveObject(linv);
                                this.FireOnInvoiceStateChanged(linv.PaymentHash, linv.State);
                            }
                            return;
                        }
                    }
                    try
                    {
                        var stream = LND.SubscribeSingleInvoice(conf, paymentHash.AsBytes(), cancellationToken: subscribeInvoicesCancallationTokenSource.Token);
                        while (await stream.ResponseStream.MoveNext(subscribeInvoicesCancallationTokenSource.Token))
                        {
                            var inv = stream.ResponseStream.Current;
                            if ((inv.State == Lnrpc.Invoice.Types.InvoiceState.Accepted) || (inv.State == Lnrpc.Invoice.Types.InvoiceState.Canceled))
                            {
                                var linv = (from i in walletContext.Value.Invoices
                                            where i.PaymentHash == inv.RHash.ToArray().AsHex()
                                            && i.State != (InvoiceState)inv.State
                                            select i).FirstOrDefault();
                                if (linv != null)
                                {
                                    linv.State = (InvoiceState)((int)inv.State);
                                    walletContext.Value.SaveObject(linv);
                                    this.FireOnInvoiceStateChanged(linv.PaymentHash, linv.State);
                                }
                            }
                            else if ((inv.State == Lnrpc.Invoice.Types.InvoiceState.Settled) || (inv.State == Lnrpc.Invoice.Types.InvoiceState.Canceled))
                                return;
                        }
                    }
                    catch (RpcException e) when (e.Status.StatusCode == StatusCode.Cancelled)
                    {
                        //                        Trace.TraceInformation("Streaming was cancelled from the client!");
                    }
                }
                finally
                {
                    alreadySubscribed.TryRemove(paymentHash, out _);
                }
            });
            return true;
        });
    }

    public void Start()
    {
        var allInvs = new Dictionary<string, Lnrpc.Invoice>(
            (from inv in LND.ListInvoices(conf).Invoices
             select KeyValuePair.Create(inv.RHash.ToArray().AsHex(), inv)));
        foreach (var inv in walletContext.Value.Invoices)
        {
            if (allInvs.ContainsKey(inv.PaymentHash))
            {
                var myinv = allInvs[inv.PaymentHash];
                if (((int)inv.State) != ((int)myinv.State))
                {
                    walletContext.Value.Invoices
                        .Where(i => i.PaymentHash == inv.PaymentHash)
                        .ExecuteUpdate(i => i
                        .SetProperty(a => a.State, (InvoiceState)myinv.State));
                }
                if (inv.IsHodlInvoice && inv.State == InvoiceState.Accepted)
                {
                    SubscribeSingleInvoice(inv.PaymentHash);
                }
            }
        }

        var allPayments = new Dictionary<string, Lnrpc.Payment>(
            (from pm in LND.ListPayments(conf).Payments
             select KeyValuePair.Create(pm.PaymentHash, pm)));
        foreach (var pm in walletContext.Value.Payments)
        {
            if (allPayments.ContainsKey(pm.PaymentHash))
            {
                var mypm = allPayments[pm.PaymentHash];
                if (((int)pm.Status) != ((int)mypm.Status))
                {
                    walletContext.Value.Payments
                        .Where(i => i.PaymentHash == pm.PaymentHash)
                        .ExecuteUpdate(i => i
                        .SetProperty(a => a.Status, (PaymentStatus)mypm.Status));
                }
            }
        }

        subscribeInvoicesCancallationTokenSource = new CancellationTokenSource();
        subscribeInvoicesThread = new Thread(async () =>
        {
            try
            {
                var stream = LND.SubscribeInvoices(conf, cancellationToken: subscribeInvoicesCancallationTokenSource.Token);
                while (await stream.ResponseStream.MoveNext(subscribeInvoicesCancallationTokenSource.Token))
                {
                    var inv = stream.ResponseStream.Current;
                    (from i in walletContext.Value.Invoices
                     where i.PaymentHash == inv.RHash.ToArray().AsHex()
                     && i.State != (InvoiceState)inv.State
                     select i)
                        .ExecuteUpdate(
                        i => i
                            .SetProperty(a => a.State, a => (InvoiceState)((int)inv.State))
                            .SetProperty(a => a.Preimage, a => inv.State == Lnrpc.Invoice.Types.InvoiceState.Settled ? inv.RPreimage.ToArray() : a.Preimage));
                    this.FireOnInvoiceStateChanged(inv.RHash.ToArray().AsHex(), (InvoiceState)inv.State);
                    if (inv.State == Lnrpc.Invoice.Types.InvoiceState.Accepted)
                    {
                        SubscribeSingleInvoice(inv.RHash.ToArray().AsHex());
                    }
                }
            }
            catch (RpcException e) when (e.Status.StatusCode == StatusCode.Cancelled)
            {
                Trace.TraceInformation("Streaming was cancelled from the client!");
            }

        });

        trackPaymentsCancallationTokenSource = new CancellationTokenSource();
        trackPaymentsThread = new Thread(async () =>
        {
            try
            {
                var stream = LND.TrackPayments(conf, cancellationToken: trackPaymentsCancallationTokenSource.Token);
                while (await stream.ResponseStream.MoveNext(trackPaymentsCancallationTokenSource.Token))
                {
                    var pm = stream.ResponseStream.Current;
                    (from i in walletContext.Value.Payments
                     where i.PaymentHash == pm.PaymentHash
                     && i.Status != (PaymentStatus)pm.Status
                     select i)
                     .ExecuteUpdate(i => i
                         .SetProperty(a => a.Status, a => (PaymentStatus)((int)pm.Status))
                         .SetProperty(a => a.PaymentFee, a => pm.Status == Lnrpc.Payment.Types.PaymentStatus.Succeeded ? (long)pm.FeeSat : a.PaymentFee));
                    this.FireOnPaymentStatusChanged(pm.PaymentHash, (PaymentStatus)pm.Status);
                }
            }
            catch (RpcException e) when (e.Status.StatusCode == StatusCode.Cancelled)
            {
                Trace.TraceInformation("Streaming was cancelled from the client!");
            }

        });

        subscribeInvoicesThread.Start();
        trackPaymentsThread.Start();

    }

    public void Stop()
    {
        subscribeInvoicesCancallationTokenSource.Cancel();
        trackPaymentsCancallationTokenSource.Cancel();
        subscribeInvoicesThread.Join();
        trackPaymentsThread.Join();
    }

    public LNDAccountManager ValidateAuthTokenAndGetAccount(string authTokenBase64)
    {
        var timedToken = CryptoToolkit.Crypto.VerifySignedTimedToken(authTokenBase64, 120.0);
        if (timedToken == null)
            throw new LNDWalletException(LNDWalletErrorCode.InvalidToken);

        var tk = (from token in walletContext.Value.Tokens where token.pubkey == timedToken.Value.PublicKey && token.id == timedToken.Value.Guid select token).FirstOrDefault();
        if (tk == null)
            throw new LNDWalletException(LNDWalletErrorCode.InvalidToken);

        return GetAccount(tk.pubkey.AsECXOnlyPubKey());
    }

    public LNDAccountManager GetAccount(ECXOnlyPubKey pubkey)
    {
        return new LNDAccountManager(conf, this.connectionString, pubkey, this);
    }

    public Guid GetTokenGuid(string pubkey)
    {
        var t = (from token in walletContext.Value.Tokens where pubkey == token.pubkey select token).FirstOrDefault();
        if (t == null)
        {
            t = new Token() { id = Guid.NewGuid(), pubkey = pubkey };
            walletContext.Value.AddObject(t);
        }
        return t.id;
    }

    public string SendCoins(string address, long satoshis)
    {
        return LND.SendCoins(conf, address, "", satoshis);
    }


    public void MarkPayoutAsSending(Guid id)
    {
        if ((from po in walletContext.Value.Payouts where po.PayoutId == id && po.State == PayoutState.Open select po)
        .ExecuteUpdate(po => po
        .SetProperty(a => a.State, a => PayoutState.Sending)) == 0)
            throw new LNDWalletException(LNDWalletErrorCode.PayoutAlreadySent);
    }

    public void MarkPayoutAsSent(Guid id, string tx)
    {
        if ((from po in walletContext.Value.Payouts where po.PayoutId == id && po.State == PayoutState.Open select po)
        .ExecuteUpdate(po => po
        .SetProperty(a => a.State, PayoutState.Sent)
        .SetProperty(a => a.Tx, a => tx)) == 0)
            throw new LNDWalletException(LNDWalletErrorCode.PayoutAlreadySent);
    }

    public long GetChannelFundingBalance(int minconf)
    {
        return (from utxo in LND.ListUnspent(conf, minconf).Utxos select utxo.AmountSat).Sum();
    }

    public long GetConfirmedWalletBalance()
    {
        return LND.WalletBallance(conf).ConfirmedBalance;
    }

    public long GetRequiredReserve(uint additionalChannelsNum)
    {
        return LND.RequiredReserve(conf, additionalChannelsNum).RequiredReserve;
    }

    public long GetRequestedReserveAmount()
    {
        return (from r in this.walletContext.Value.Reserves select r.Satoshis).Sum();
    }

    public List<Reserve> GetRequestedReserves()
    {
        return (from r in this.walletContext.Value.Reserves select r).ToList();
    }

    public List<Payout> GetPendingPayouts(List<Guid> payoutIds)
    {
        return (from p in this.walletContext.Value.Payouts where payoutIds.Contains(p.PayoutId) select p).ToList();
    }

    public AsyncServerStreamingCall<OpenStatusUpdate> OpenChannel(string nodePubKey, long fundingSatoshis)
    {
        return LND.OpenChannel(conf, nodePubKey, fundingSatoshis);
    }

    public BatchOpenChannelResponse BatchOpenChannel(List<(string, long)> amountsPerNode)
    {
        return LND.BatchOpenChannel(conf, amountsPerNode);
    }

    public string OpenChannelSync(string nodePubKey, long fundingSatoshis)
    {
        var channelpoint = LND.OpenChannelSync(conf, nodePubKey, fundingSatoshis);
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
        return LND.ListChannels(conf, openOnly);
    }

    public AsyncServerStreamingCall<CloseStatusUpdate> CloseChannel(string chanpoint)
    {
        return LND.CloseChannel(conf, chanpoint);
    }

}
