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

public class Address
{
    [Key]
    public string address { get; set; }
    public string pubkey { get; set; }
    public long txfee { get; set; }
}

public class Invoice
{
    [Key]
    public string hash { get; set; }
    public string pubkey { get; set; }
    public string paymentreq { get; set; }
    public long satoshis { get; set; }
    public InvoiceState state { get; set; }
    public long txfee { get; set; }
    public bool ishodl { get; set; }
    public bool isselfmanaged { get; set; }
    public byte[]? preimage { get; set; }
}

public class Payment
{
    [Key]
    public string hash { get; set; }
    public string pubkey { get; set; }
    public long satoshis { get; set; }
    public PaymentStatus status { get; set; }
    public long paymentfee { get; set; }
    public long txfee { get; set; }
    public bool isselfmanaged { get; set; }
}

public class Payout
{
    [Key]
    public Guid id { get; set; }
    public string pubkey { get; set; }
    public string address { get; set; }
    public bool ispending { get; set; }
    public long satoshis { get; set; }
    public long txfee { get; set; }
    public string tx { get; set; }
}

public class Token
{
    [Key]
    public Guid id { get; set; }
    public string pubkey { get; set; }
}

public class WaletContext : DbContext
{
    string connectionString;

    public WaletContext(string connectionString)
    {
        this.connectionString = connectionString;
    }

    public DbSet<Address> FundingAddresses { get; set; }
    public DbSet<Payout> Payouts { get; set; }
    public DbSet<Invoice> Invoices { get; set; }
    public DbSet<Payment> Payments { get; set; }
    public DbSet<Token> Tokens { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(connectionString);
    }

}

public class NotEnoughFundsInAccountException : Exception
{ }

public class NotEnoughFundsOnChainException : Exception
{ }

public class PayoutAlreadyCompletedException : Exception
{ }

public record AuthToken
{
    public Guid Id { get; set; }
    public DateTime DateTime { get; set; }
}

public class LNDAccountManager
{
    LND.NodesConfiguration conf;
    int idx;
    WaletContext walletContext;
    GetInfoResponse info;
    string account;
    ECXOnlyPubKey pubKey;

    internal LNDAccountManager(LND.NodesConfiguration conf, int idx, WaletContext walletContext, GetInfoResponse info, ECXOnlyPubKey pubKey)
    {
        this.pubKey = pubKey;
        this.conf = conf;
        this.idx = idx;
        this.account = pubKey.AsHex();
        this.walletContext = walletContext;
        this.info = info;
    }

    public LNDAccountManager ValidateToken(string authTokenBase64)
    {
        var guid = CryptoToolkit.Crypto.VerifySignedTimedToken(pubKey, authTokenBase64, 120.0);
        if (guid == null)
            throw new InvalidOperationException();

        var tk = (from token in walletContext.Tokens where token.pubkey == account && token.id == guid select token).FirstOrDefault();
        if (tk == null)
            throw new InvalidOperationException();

        return this;
    }

    public string NewAddress(long txfee)
    {
        lock (walletContext)
        {
            var newaddress = LND.NewAddress(conf, idx);
            walletContext.FundingAddresses.Add(new Address() { address = newaddress, pubkey = account, txfee = txfee });
            walletContext.SaveChanges();
            return newaddress;
        }
    }

    public Guid RegisterPayout(long satoshis, string btcAddress, long txfee)
    {
        if ((GetAccountBallance() - (long)txfee)< (long)satoshis)
            throw new NotEnoughFundsInAccountException();

        var myid = Guid.NewGuid();

        lock (walletContext)
        {
            walletContext.Payouts.Add(new LNDWallet.Payout()
            {
                id = myid,
                address = btcAddress,
                pubkey = account,
                txfee = txfee,
                ispending = true,
                satoshis = satoshis
            });
            walletContext.SaveChanges();
        }
        return myid;
    }

    public long GetExecutedPayedInFundingAmount(int minConf)
    {
        lock (walletContext)
        {
            var myaddrs = new Dictionary<string, long>(
                from a in walletContext.FundingAddresses
                where a.pubkey == account
                select new KeyValuePair<string, long>(a.address, a.txfee));

            var transactuinsResp = LND.GetTransactions(conf, idx);
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
    }

    public long GetPayedOutAmount()
    {
        lock (walletContext)
        {
            return (from a in walletContext.Payouts
                    where a.pubkey == account
                    select ((long)(a.satoshis + a.txfee))).Sum();
        }
    }

    public List<LNDWallet.Payout> GetPendingPayouts()
    {
        lock (walletContext)
        {
            return (from a in walletContext.Payouts
                    where a.pubkey == account && a.ispending
                    select a).ToList();
        }
    }

    public long GetPendingPayedOutAmount()
    {
        lock (walletContext)
        {
            return (from a in walletContext.Payouts
                    where a.pubkey == account && a.ispending
                    select ((long)(a.satoshis + a.txfee))).Sum();
        }
    }

    public long GetExecutedPayedOutAmount(int minConf)
    {
        Dictionary<string, LNDWallet.Payout> mypayouts;
        lock (walletContext)
        {
            mypayouts = new Dictionary<string, LNDWallet.Payout>(
                from a in walletContext.Payouts
                where a.pubkey == account
                select new KeyValuePair<string, LNDWallet.Payout>(a.address, a));
        }

        var transactuinsResp = LND.GetTransactions(conf, idx);
        long balance = 0;
        foreach (var transation in transactuinsResp.Transactions)
            if (transation.NumConfirmations >= minConf)
                foreach (var outp in transation.OutputDetails)
                    if (!outp.IsOurAddress)
                        if (mypayouts.ContainsKey(outp.Address))
                        {
                            balance += outp.Amount;
                            balance += (long)mypayouts[outp.Address].txfee;
                        }
        return balance;
    }

    public Invoicesrpc.AddHoldInvoiceResp AddHodlInvoice(long satoshis, string memo, byte[] hash, long txfee, long expiry = 86400)
    {
        var inv = LND.AddHodlInvoice(conf, idx, satoshis, memo, hash, expiry);
        lock (walletContext)
        {
            walletContext.Invoices.Add(new Invoice() {
                hash = hash.AsHex(),
                pubkey = account,
                paymentreq = inv.PaymentRequest,
                satoshis = (long)satoshis,
                state = InvoiceState.Open,
                txfee = txfee,
                ishodl = true,
                isselfmanaged = false,
            }) ;
            walletContext.SaveChanges();
        }
        return inv;
    }

    public Lnrpc.AddInvoiceResponse AddInvoice(long satoshis, string memo, long txfee)
    {
        var inv = LND.AddInvoice(conf, idx, satoshis, memo);
        lock (walletContext)
        {
            walletContext.Invoices.Add(new Invoice()
            {
                hash = inv.RHash.ToArray().AsHex(),
                pubkey = account,
                paymentreq = inv.PaymentRequest,
                satoshis = (long)satoshis,
                state = InvoiceState.Open,
                txfee = txfee,
                ishodl = false,
                isselfmanaged = false,
            });
            walletContext.SaveChanges();
        }
        return inv;
    }

    public PayReq DecodeInvoice(string paymentRequest)
    {
        return LND.DecodeInvoice(conf, idx, paymentRequest);
    }

    public void SendPayment(string paymentRequest, int timeout, long txfee, long feelimit)
    {
        var decinv = LND.DecodeInvoice(conf, idx, paymentRequest);
        lock (walletContext)
        {
            if ((long)decinv.NumSatoshis > GetAccountBallance() - (long)txfee)
                throw new NotEnoughFundsInAccountException();

            var selfInv = (from inv in walletContext.Invoices where inv.hash == decinv.PaymentHash select inv).FirstOrDefault();
            if (selfInv != null) // selfpayment
            {
                selfInv.isselfmanaged = true;
                selfInv.state = selfInv.ishodl ? InvoiceState.Accepted : InvoiceState.Settled;
                walletContext.Invoices.Update(selfInv);
                walletContext.Payments.Add(new Payment()
                {
                    hash = decinv.PaymentHash,
                    pubkey = account,
                    satoshis = selfInv.satoshis,
                    txfee = txfee,
                    isselfmanaged = true,
                    paymentfee = 0,
                    status = selfInv.ishodl ? PaymentStatus.InFlight : PaymentStatus.Succeeded,
                }) ;
                walletContext.SaveChanges();
            }
            else
            {
                LND.SendPaymentV2(conf, idx, paymentRequest, timeout, (long)feelimit);
                walletContext.Payments.Add(new Payment()
                {
                    hash = decinv.PaymentHash,
                    pubkey = account,
                    satoshis = (long)decinv.NumSatoshis,
                    txfee = txfee,
                    isselfmanaged = false,
                    paymentfee = feelimit,
                    status = selfInv.ishodl ? PaymentStatus.InFlight : PaymentStatus.Succeeded,
                });
                walletContext.SaveChanges();
            }
        }
    }

    public void SettleInvoice(byte[] preimage)
    {
        var hash = CryptoToolkit.Crypto.ComputePaymentHash(preimage);
        var paymentHash = hash.AsHex();
        lock (walletContext)
        {
            var selftrans = (from st in walletContext.Invoices where st.hash == paymentHash && st.isselfmanaged select st).FirstOrDefault();
            if (selftrans != null)
            {
                selftrans.state = InvoiceState.Settled;
                selftrans.preimage = preimage;
                walletContext.Update(selftrans);
                walletContext.SaveChanges();
            }
            else
            {
                LND.SettleInvoice(conf, idx, preimage);
            }
        }
    }

    public void CancelInvoice(string paymentHash)
    {
        var hash = Convert.FromHexString(paymentHash);
        lock (walletContext)
        {
            var selftrans = (from st in walletContext.Invoices where st.hash == paymentHash && st.isselfmanaged select st).FirstOrDefault();
            if (selftrans != null)
            {
                selftrans.state = InvoiceState.Cancelled;
                walletContext.Update(selftrans);
                walletContext.SaveChanges();
            }
            else
            {
                LND.CancelInvoice(conf, idx, Convert.FromHexString(paymentHash));
            }
        }
    }

    public PaymentStatus GetPaymentStatus(string paymentHash)
    {
        lock(walletContext)
        {
            var pm = (from pay in walletContext.Payments where pay.hash == paymentHash select pay).FirstOrDefault();
            if (pm != null)
                return pm.status;
            else
                throw new InvalidOperationException();
        }
    }

    public InvoiceState GetInvoiceState(string paymentHash)
    {
        lock (walletContext)
        {
            var iv = (from inv in walletContext.Invoices where inv.hash == paymentHash select inv).FirstOrDefault();
            if (iv != null)
                return iv.state;
            else
                throw new InvalidOperationException();
        }
    }

    public long GetAccountBallance()
    {
        lock (walletContext)
        {
            var channelfunds = GetExecutedPayedInFundingAmount(6);
            var alreadypayedout = GetPayedOutAmount();

            var earnedFromSettledInvoices = (from inv in walletContext.Invoices
                                            where inv.pubkey == account && inv.state == InvoiceState.Settled
                                            select (long)inv.satoshis-(long)inv.txfee).Sum();

            var sendOrLocedPayments = (from pay in walletContext.Payments
                                            where pay.pubkey == account
                                            select (long)pay.satoshis + ((long)pay.paymentfee + (long)pay.txfee)).Sum();

            return channelfunds - alreadypayedout + earnedFromSettledInvoices - sendOrLocedPayments;
        }
    }

}

public class LNDWalletManager
{
    LND.NodesConfiguration conf;
    int idx;
    WaletContext walletContext;
    GetInfoResponse info;

    public LNDWalletManager(string connectionString, LND.NodesConfiguration conf, int idx, GetInfoResponse nodeInfo, bool deleteDb = false)
    {
        this.walletContext = new WaletContext(connectionString);
        this.conf = conf;
        this.idx = idx;
        if (deleteDb)
            walletContext.Database.EnsureDeleted();
        walletContext.Database.EnsureCreated();
        this.info = nodeInfo;
    }

    CancellationTokenSource subscribeInvoicesCancallationTokenSource ;
    Thread subscribeInvoicesThread;

    CancellationTokenSource trackPaymentsCancallationTokenSource;
    Thread trackPaymentsThread;

    public void Start()
    {
        lock(walletContext)
        {
            var allInvs = new Dictionary<string, Lnrpc.Invoice>(
                (from inv in LND.ListInvoices(conf, idx).Invoices
                 select KeyValuePair.Create(inv.RHash.ToArray().AsHex(), inv)));
            foreach (var inv in walletContext.Invoices)
            {
                if (allInvs.ContainsKey(inv.hash))
                {
                    var myinv = allInvs[inv.hash];
                    if (((int)inv.state) != ((int)myinv.State))
                    {
                        inv.state = (InvoiceState)myinv.State;
                        walletContext.Invoices.Update(inv);
                    }
                }
            }

            var allPayments = new Dictionary<string, Lnrpc.Payment>(
                (from pm in LND.ListPayments(conf, idx).Payments
                 select KeyValuePair.Create(pm.PaymentHash, pm)));
            foreach (var pm in walletContext.Payments)
            {
                if (allPayments.ContainsKey(pm.hash))
                {
                    var mypm = allPayments[pm.hash];
                    if (((int)pm.status) != ((int)mypm.Status))
                    {
                        pm.status = (PaymentStatus)mypm.Status;
                        walletContext.Payments.Update(pm);
                    }
                }
            }

            walletContext.SaveChanges();
        }

        subscribeInvoicesCancallationTokenSource = new CancellationTokenSource();
        subscribeInvoicesThread = new Thread(async () =>
        {
            try
            {
                var stream = LND.SubscribeInvoices(conf, idx, cancellationToken:subscribeInvoicesCancallationTokenSource.Token);
                while (await stream.ResponseStream.MoveNext(subscribeInvoicesCancallationTokenSource.Token))
                {
                    var inv = stream.ResponseStream.Current;
                    lock (walletContext)
                    {
                        var walInv = (from inv2 in walletContext.Invoices where inv2.hash == inv.RHash.ToArray().AsHex() select inv2).FirstOrDefault();
                        if (walInv != null)
                        {
                            if (((int)walInv.state) != ((int)inv.State))
                            {
                                walInv.state = (InvoiceState)inv.State;
                                if (inv.State == Lnrpc.Invoice.Types.InvoiceState.Settled)
                                    walInv.preimage = inv.RPreimage.ToArray();
                                walletContext.Invoices.Update(walInv);
                                walletContext.SaveChanges();
                            }
                        }
                    }
                }
            }
            catch (RpcException e) when (e.Status.StatusCode == StatusCode.Cancelled)
            {
                Console.WriteLine("Streaming was cancelled from the client!");
            }

        });

        trackPaymentsCancallationTokenSource = new CancellationTokenSource();
        trackPaymentsThread = new Thread(async () =>
        {
            try
            {
                var stream = LND.TrackPayments(conf, idx, cancellationToken: trackPaymentsCancallationTokenSource.Token);
                while (await stream.ResponseStream.MoveNext(trackPaymentsCancallationTokenSource.Token))
                {
                    var pm = stream.ResponseStream.Current;
                    lock (walletContext)
                    {
                        var walPm = (from pm2 in walletContext.Payments where pm2.hash == pm.PaymentHash select pm2).FirstOrDefault();
                        if (walPm != null)
                        {
                            if (((int)walPm.status) != ((int)pm.Status))
                            {
                                walPm.status = (PaymentStatus)pm.Status;
                                if(pm.Status == Lnrpc.Payment.Types.PaymentStatus.Succeeded)
                                    walPm.paymentfee = (long)pm.FeeSat;
                                walletContext.Payments.Update(walPm);
                                walletContext.SaveChanges();
                            }
                        }
                    }
                }
            }
            catch (RpcException e) when (e.Status.StatusCode == StatusCode.Cancelled)
            {
                Console.WriteLine("Streaming was cancelled from the client!");
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

    public LNDAccountManager GetAccount(ECXOnlyPubKey pubkey)
    {
        lock (walletContext)
        {
            return new LNDAccountManager(conf, idx, walletContext, info, pubkey);
        }
    }

    public Guid GetToken(string pubkey)
    {
        lock(walletContext)
        {
            var t=(from token in walletContext.Tokens where pubkey == token.pubkey select token).FirstOrDefault();
            if(t == null)
            {
                t = new Token() { id = Guid.NewGuid(), pubkey = pubkey };
                walletContext.Tokens.Add(t);
                walletContext.SaveChanges();
            }
            return t.id;
        }
    }

    public string SendCoins(string address, long satoshis)
    {
        return LND.SendCoins(conf, idx, address, "", satoshis);
    }

    public List<LNDWallet.Payout> GetAllPendingPayouts()
    {
        lock (walletContext)
        {
            return (from a in walletContext.Payouts
                    where a.ispending
                    select a).ToList();
        }
    }

    public void MarkPayoutAsCompleted(Guid id, string tx)
    {
        lock (walletContext)
        {
            var payout = (from po in walletContext.Payouts where po.id == id && po.ispending select po).FirstOrDefault();
            if (payout != null)
            {
                payout.ispending = false;
                payout.tx = tx;
                walletContext.Update(payout);
                walletContext.SaveChanges();
            }
            else
                throw new PayoutAlreadyCompletedException();
        }
    }

    public long GetChannelFundingBalance(int minconf)
    {
        return (from utxo in LND.ListUnspent(conf, idx,minconf).Utxos select utxo.AmountSat).Sum() ;
    }

    public AsyncServerStreamingCall<OpenStatusUpdate> OpenChannel(string nodePubKey, long fundingSatoshis)
    {
        return LND.OpenChannel(conf, idx, nodePubKey, fundingSatoshis);
    }

    public string OpenChannelSync(string nodePubKey, long fundingSatoshis)
    {
        var channelpoint = LND.OpenChannelSync(conf, idx, nodePubKey, fundingSatoshis);
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
        return LND.ListChannels(conf, idx, openOnly);
    }

    public AsyncServerStreamingCall<CloseStatusUpdate> CloseChannel(string chanpoint)
    {
        return LND.CloseChannel(conf, idx, chanpoint);
    }

}
