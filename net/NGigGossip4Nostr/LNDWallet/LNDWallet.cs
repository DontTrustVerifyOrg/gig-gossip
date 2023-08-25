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

public class LNDAccountManager
{
    private LND.NodeSettings conf;
    private ThreadLocal<WaletContext> walletContext;
    private string publicKey;

    internal LNDAccountManager(LND.NodeSettings conf, string connectionString, ECXOnlyPubKey pubKey)
    {
        this.conf = conf;
        this.publicKey = pubKey.AsHex();
        this.walletContext = new ThreadLocal<WaletContext>(() => new WaletContext(connectionString));
    }

    public string NewAddress(long txfee)
    {
            var newaddress = LND.NewAddress(conf);
            walletContext.Value.AddObject(new Address() { BitcoinAddress = newaddress, PublicKey = publicKey, TxFee = txfee });
            return newaddress;
    }

    public Guid RegisterPayout(long satoshis, string btcAddress, long txfee)
    {
        if ((GetAccountBallance() - (long)txfee)< (long)satoshis)
            throw new LNDWalletException(LNDWalletErrorCode.NotEnoughFunds);

        var myid = Guid.NewGuid();

            walletContext.Value.AddObject(new LNDWallet.Payout()
            {
                PayoutId = myid,
                BitcoinAddress = btcAddress,
                PublicKey = publicKey,
                TxFee = txfee,
                IsPending = true,
                Satoshis = satoshis
            });
        return myid;
    }

    public long GetExecutedPayedInFundingAmount(int minConf)
    {
            var myaddrs = new Dictionary<string, long>(
                from a in walletContext.Value.FundingAddresses
                where a.PublicKey == publicKey
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
                    where a.PublicKey == publicKey
                    select ((long)(a.Satoshis + a.TxFee))).Sum();
    }

    public List<LNDWallet.Payout> GetPendingPayouts()
    {
            return (from a in walletContext.Value.Payouts
                    where a.PublicKey == publicKey && a.IsPending
                    select a).ToList();
    }

    public long GetPendingPayedOutAmount()
    {
            return (from a in walletContext.Value.Payouts
                    where a.PublicKey == publicKey && a.IsPending
                    select ((long)(a.Satoshis + a.TxFee))).Sum();
    }

    public long GetExecutedPayedOutAmount(int minConf)
    {
        Dictionary<string, LNDWallet.Payout> mypayouts;
            mypayouts = new Dictionary<string, LNDWallet.Payout>(
                from a in walletContext.Value.Payouts
                where a.PublicKey == publicKey
                select new KeyValuePair<string, LNDWallet.Payout>(a.BitcoinAddress, a));

        var transactuinsResp = LND.GetTransactions(conf);
        long balance = 0;
        foreach (var transation in transactuinsResp.Transactions)
            if (transation.NumConfirmations >= minConf)
                foreach (var outp in transation.OutputDetails)
                    if (!outp.IsOurAddress)
                        if (mypayouts.ContainsKey(outp.Address))
                        {
                            balance += outp.Amount;
                            balance += (long)mypayouts[outp.Address].TxFee;
                        }
        return balance;
    }

    public Invoicesrpc.AddHoldInvoiceResp AddHodlInvoice(long satoshis, string memo, byte[] hash, long txfee, long expiry)
    {
        var inv = LND.AddHodlInvoice(conf, satoshis, memo, hash, expiry);
            walletContext.Value.AddObject(new Invoice() {
                PaymentHash = hash.AsHex(),
                PublicKey = publicKey,
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
            PublicKey = publicKey,
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

    public void SendPayment(string paymentRequest, int timeout, long txfee, long feelimit)
    {
        var decinv = LND.DecodeInvoice(conf, paymentRequest);
            if ((long)decinv.NumSatoshis > GetAccountBallance() - (long)txfee)
                throw new LNDWalletException(LNDWalletErrorCode.NotEnoughFunds);

        var selfInvQuery = (from inv in walletContext.Value.Invoices
                            where inv.PaymentHash == decinv.PaymentHash
                            select inv);
        var selfInv = selfInvQuery.FirstOrDefault();
        if (selfInv != null) // selfpayment
        {
            selfInvQuery.ExecuteUpdate(
                    i=>i
                    .SetProperty(a=>a.State, a=>a.IsHodlInvoice? InvoiceState.Accepted : InvoiceState.Settled)
                    .SetProperty(a=>a.IsSelfManaged,a=>true));

            walletContext.Value.AddObject(new Payment()
            {
                PaymentHash = decinv.PaymentHash,
                PublicKey = publicKey,
                Satoshis = selfInv.Satoshis,
                TxFee = txfee,
                IsSelfManaged = true,
                PaymentFee = 0,
                Status = selfInv.IsHodlInvoice ? PaymentStatus.InFlight : PaymentStatus.Succeeded,
            }) ;
        }
        else
        {
            LND.SendPaymentV2(conf, paymentRequest, timeout, (long)feelimit);
            walletContext.Value.AddObject(new Payment()
            {
                PaymentHash = decinv.PaymentHash,
                PublicKey = publicKey,
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
        var hash = Crypto.ComputePaymentHash(preimage);
        var paymentHash = hash.AsHex();
        var selfTransQuery = (from inv in walletContext.Value.Invoices
                              where inv.PaymentHash == paymentHash && inv.IsSelfManaged
                              select inv);

        if (selfTransQuery.ExecuteUpdate(
            i => i
            .SetProperty(a => a.State, a => InvoiceState.Settled)
            .SetProperty(a => a.Preimage, a => preimage)
            ) == 0)
        {
            //this happens the invoice is not self managed so the update returned 0 rows changed
            LND.SettleInvoice(conf, preimage);
        }
    }

    public void CancelInvoice(string paymentHash)
    {
        var hash = paymentHash.AsBytes();
        var selfTransQuery = (from inv in walletContext.Value.Invoices
                              where inv.PaymentHash == paymentHash && inv.IsSelfManaged
                              select inv);

        if (selfTransQuery.ExecuteUpdate(
            i => i
            .SetProperty(a => a.State, a => InvoiceState.Cancelled)
            ) == 0)
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
                                            where inv.PublicKey == publicKey && inv.State == InvoiceState.Settled
                                            select (long)inv.Satoshis-(long)inv.TxFee).Sum();

            var sendOrLocedPayments = (from pay in walletContext.Value.Payments
                                            where pay.PublicKey == publicKey
                                            select (long)pay.Satoshis + ((long)pay.PaymentFee + (long)pay.TxFee)).Sum();

            return channelfunds - alreadypayedout + earnedFromSettledInvoices - sendOrLocedPayments;
    }

}

public class LNDWalletManager
{
    private LND.NodeSettings conf;
    private ThreadLocal<WaletContext> walletContext;
    private string connectionString;
    private CancellationTokenSource subscribeInvoicesCancallationTokenSource;
    private Thread subscribeInvoicesThread;
    private CancellationTokenSource trackPaymentsCancallationTokenSource;
    private Thread trackPaymentsThread;

    public LNDWalletManager(string connectionString, LND.NodeSettings conf, bool deleteDb = false)
    {
        this.connectionString = connectionString;
        this.walletContext = new ThreadLocal<WaletContext>(() => new WaletContext(connectionString));
        this.conf = conf;
        if (deleteDb)
            walletContext.Value.Database.EnsureDeleted();
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
                        .SetProperty(a => a.State, a => (InvoiceState)inv.State)
                        .SetProperty(a => a.Preimage, a => inv.State == Lnrpc.Invoice.Types.InvoiceState.Settled ? inv.RPreimage.ToArray() : a.Preimage));
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
                     .SetProperty(a => a.Status, a => (PaymentStatus)pm.Status)
                     .SetProperty(a => a.PaymentFee, a => pm.Status == Lnrpc.Payment.Types.PaymentStatus.Succeeded ? (long)pm.FeeSat : a.PaymentFee));
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
        return new LNDAccountManager(conf, this.connectionString, pubkey);
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

    public List<LNDWallet.Payout> GetAllPendingPayouts()
    {
        return (from a in walletContext.Value.Payouts
                where a.IsPending
                select a).ToList();
    }

    public void MarkPayoutAsCompleted(Guid id, string tx)
    {
        if ((from po in walletContext.Value.Payouts where po.PayoutId == id && po.IsPending select po)
        .ExecuteUpdate(po => po
        .SetProperty(a => a.IsPending, a => false)
        .SetProperty(a => a.Tx, a => tx)) == 0)
            throw new LNDWalletException(LNDWalletErrorCode.PayoutAlreadyCompleted);
    }

    public long GetChannelFundingBalance(int minconf)
    {
        return (from utxo in LND.ListUnspent(conf, minconf).Utxos select utxo.AmountSat).Sum();
    }

    public AsyncServerStreamingCall<OpenStatusUpdate> OpenChannel(string nodePubKey, long fundingSatoshis)
    {
        return LND.OpenChannel(conf, nodePubKey, fundingSatoshis);
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
