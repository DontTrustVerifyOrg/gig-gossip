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
    LND.NodeSettings conf;
    ThreadLocal<WaletContext> walletContext;
    GetInfoResponse info;
    string account;
    ECXOnlyPubKey pubKey;
    string connectionString;

    internal LNDAccountManager(LND.NodeSettings conf, string connectionString, GetInfoResponse info, ECXOnlyPubKey pubKey)
    {
        this.connectionString = connectionString;
        this.pubKey = pubKey;
        this.conf = conf;
        this.account = pubKey.AsHex();
        this.walletContext = new ThreadLocal<WaletContext>(() => new WaletContext(connectionString));
        this.info = info;
    }

    public string NewAddress(long txfee)
    {
            var newaddress = LND.NewAddress(conf);
            walletContext.Value.FundingAddresses.Add(new Address() { address = newaddress, pubkey = account, txfee = txfee });
            walletContext.Value.SaveChanges();
            return newaddress;
    }

    public Guid RegisterPayout(long satoshis, string btcAddress, long txfee)
    {
        if ((GetAccountBallance() - (long)txfee)< (long)satoshis)
            throw new NotEnoughFundsInAccountException();

        var myid = Guid.NewGuid();

            walletContext.Value.Payouts.Add(new LNDWallet.Payout()
            {
                id = myid,
                address = btcAddress,
                pubkey = account,
                txfee = txfee,
                ispending = true,
                satoshis = satoshis
            });
            walletContext.Value.SaveChanges();
        return myid;
    }

    public long GetExecutedPayedInFundingAmount(int minConf)
    {
            var myaddrs = new Dictionary<string, long>(
                from a in walletContext.Value.FundingAddresses
                where a.pubkey == account
                select new KeyValuePair<string, long>(a.address, a.txfee));

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
                    where a.pubkey == account
                    select ((long)(a.satoshis + a.txfee))).Sum();
    }

    public List<LNDWallet.Payout> GetPendingPayouts()
    {
            return (from a in walletContext.Value.Payouts
                    where a.pubkey == account && a.ispending
                    select a).ToList();
    }

    public long GetPendingPayedOutAmount()
    {
            return (from a in walletContext.Value.Payouts
                    where a.pubkey == account && a.ispending
                    select ((long)(a.satoshis + a.txfee))).Sum();
    }

    public long GetExecutedPayedOutAmount(int minConf)
    {
        Dictionary<string, LNDWallet.Payout> mypayouts;
            mypayouts = new Dictionary<string, LNDWallet.Payout>(
                from a in walletContext.Value.Payouts
                where a.pubkey == account
                select new KeyValuePair<string, LNDWallet.Payout>(a.address, a));

        var transactuinsResp = LND.GetTransactions(conf);
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

    public Invoicesrpc.AddHoldInvoiceResp AddHodlInvoice(long satoshis, string memo, byte[] hash, long txfee, long expiry)
    {
        var inv = LND.AddHodlInvoice(conf, satoshis, memo, hash, expiry);
            walletContext.Value.Invoices.Add(new Invoice() {
                hash = hash.AsHex(),
                pubkey = account,
                paymentreq = inv.PaymentRequest,
                satoshis = (long)satoshis,
                state = InvoiceState.Open,
                txfee = txfee,
                ishodl = true,
                isselfmanaged = false,
            }) ;
            walletContext.Value.SaveChanges();
        return inv;
    }

    public Lnrpc.AddInvoiceResponse AddInvoice(long satoshis, string memo, long txfee, long expiry)
    {
        var inv = LND.AddInvoice(conf, satoshis, memo, expiry);
            walletContext.Value.Invoices.Add(new Invoice()
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
            walletContext.Value.SaveChanges();
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
                throw new NotEnoughFundsInAccountException();

            var selfInv = (from inv in walletContext.Value.Invoices where inv.hash == decinv.PaymentHash select inv).FirstOrDefault();
            if (selfInv != null) // selfpayment
            {
                selfInv.isselfmanaged = true;
                selfInv.state = selfInv.ishodl ? InvoiceState.Accepted : InvoiceState.Settled;
                walletContext.Value.Invoices.Update(selfInv);
                walletContext.Value.Payments.Add(new Payment()
                {
                    hash = decinv.PaymentHash,
                    pubkey = account,
                    satoshis = selfInv.satoshis,
                    txfee = txfee,
                    isselfmanaged = true,
                    paymentfee = 0,
                    status = selfInv.ishodl ? PaymentStatus.InFlight : PaymentStatus.Succeeded,
                }) ;
                walletContext.Value.SaveChanges();
            }
            else
            {
                LND.SendPaymentV2(conf, paymentRequest, timeout, (long)feelimit);
                walletContext.Value.Payments.Add(new Payment()
                {
                    hash = decinv.PaymentHash,
                    pubkey = account,
                    satoshis = (long)decinv.NumSatoshis,
                    txfee = txfee,
                    isselfmanaged = false,
                    paymentfee = feelimit,
                    status = selfInv.ishodl ? PaymentStatus.InFlight : PaymentStatus.Succeeded,
                });
                walletContext.Value.SaveChanges();
            }
    }

    public void SettleInvoice(byte[] preimage)
    {
        var hash = CryptoToolkit.Crypto.ComputePaymentHash(preimage);
        var paymentHash = hash.AsHex();
            var selftrans = (from st in walletContext.Value.Invoices where st.hash == paymentHash && st.isselfmanaged select st).FirstOrDefault();
            if (selftrans != null)
            {
                selftrans.state = InvoiceState.Settled;
                selftrans.preimage = preimage;
                walletContext.Value.Update(selftrans);
                walletContext.Value.SaveChanges();
            }
            else
            {
                LND.SettleInvoice(conf, preimage);
            }
    }

    public void CancelInvoice(string paymentHash)
    {
        var hash = Convert.FromHexString(paymentHash);
            var selftrans = (from st in walletContext.Value.Invoices where st.hash == paymentHash && st.isselfmanaged select st).FirstOrDefault();
            if (selftrans != null)
            {
                selftrans.state = InvoiceState.Cancelled;
                walletContext.Value.Update(selftrans);
                walletContext.Value.SaveChanges();
            }
            else
            {
                LND.CancelInvoice(conf, Convert.FromHexString(paymentHash));
            }
    }

    public PaymentStatus GetPaymentStatus(string paymentHash)
    {
            var pm = (from pay in walletContext.Value.Payments where pay.hash == paymentHash select pay).FirstOrDefault();
            if (pm != null)
                return pm.status;
            else
                throw new InvalidOperationException();
    }

    public InvoiceState GetInvoiceState(string paymentHash)
    {
            var iv = (from inv in walletContext.Value.Invoices where inv.hash == paymentHash select inv).FirstOrDefault();
            if (iv != null)
                return iv.state;
            else
                throw new InvalidOperationException();
    }

    public long GetAccountBallance()
    {
            var channelfunds = GetExecutedPayedInFundingAmount(6);
            var alreadypayedout = GetPayedOutAmount();

            var earnedFromSettledInvoices = (from inv in walletContext.Value.Invoices
                                            where inv.pubkey == account && inv.state == InvoiceState.Settled
                                            select (long)inv.satoshis-(long)inv.txfee).Sum();

            var sendOrLocedPayments = (from pay in walletContext.Value.Payments
                                            where pay.pubkey == account
                                            select (long)pay.satoshis + ((long)pay.paymentfee + (long)pay.txfee)).Sum();

            return channelfunds - alreadypayedout + earnedFromSettledInvoices - sendOrLocedPayments;
    }

}

public class LNDWalletManager
{
    LND.NodeSettings conf;
    ThreadLocal<WaletContext> walletContext;
    GetInfoResponse info;
    string connectionString;

    public LNDWalletManager(string connectionString, LND.NodeSettings conf, GetInfoResponse nodeInfo, bool deleteDb = false)
    {
        this.connectionString = connectionString;
        this.walletContext = new ThreadLocal<WaletContext>(() => new WaletContext(connectionString));
        this.conf = conf;
        if (deleteDb)
            walletContext.Value.Database.EnsureDeleted();
        walletContext.Value.Database.EnsureCreated();
        this.info = nodeInfo;
    }

    CancellationTokenSource subscribeInvoicesCancallationTokenSource;
    Thread subscribeInvoicesThread;

    CancellationTokenSource trackPaymentsCancallationTokenSource;
    Thread trackPaymentsThread;

    public void Start()
    {
        var allInvs = new Dictionary<string, Lnrpc.Invoice>(
            (from inv in LND.ListInvoices(conf).Invoices
             select KeyValuePair.Create(inv.RHash.ToArray().AsHex(), inv)));
        foreach (var inv in walletContext.Value.Invoices)
        {
            if (allInvs.ContainsKey(inv.hash))
            {
                var myinv = allInvs[inv.hash];
                if (((int)inv.state) != ((int)myinv.State))
                {
                    inv.state = (InvoiceState)myinv.State;
                    walletContext.Value.Invoices.Update(inv);
                }
            }
        }

        var allPayments = new Dictionary<string, Lnrpc.Payment>(
            (from pm in LND.ListPayments(conf).Payments
             select KeyValuePair.Create(pm.PaymentHash, pm)));
        foreach (var pm in walletContext.Value.Payments)
        {
            if (allPayments.ContainsKey(pm.hash))
            {
                var mypm = allPayments[pm.hash];
                if (((int)pm.status) != ((int)mypm.Status))
                {
                    pm.status = (PaymentStatus)mypm.Status;
                    walletContext.Value.Payments.Update(pm);
                }
            }
        }

        walletContext.Value.SaveChanges();

        subscribeInvoicesCancallationTokenSource = new CancellationTokenSource();
        subscribeInvoicesThread = new Thread(async () =>
        {
            try
            {
                var stream = LND.SubscribeInvoices(conf, cancellationToken: subscribeInvoicesCancallationTokenSource.Token);
                while (await stream.ResponseStream.MoveNext(subscribeInvoicesCancallationTokenSource.Token))
                {
                    var inv = stream.ResponseStream.Current;
                    var walInv = (from inv2 in walletContext.Value.Invoices where inv2.hash == inv.RHash.ToArray().AsHex() select inv2).FirstOrDefault();
                    if (walInv != null)
                    {
                        if (((int)walInv.state) != ((int)inv.State))
                        {
                            walInv.state = (InvoiceState)inv.State;
                            if (inv.State == Lnrpc.Invoice.Types.InvoiceState.Settled)
                                walInv.preimage = inv.RPreimage.ToArray();
                            walletContext.Value.Invoices.Update(walInv);
                            walletContext.Value.SaveChanges();
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
                var stream = LND.TrackPayments(conf, cancellationToken: trackPaymentsCancallationTokenSource.Token);
                while (await stream.ResponseStream.MoveNext(trackPaymentsCancallationTokenSource.Token))
                {
                    var pm = stream.ResponseStream.Current;
                    var walPm = (from pm2 in walletContext.Value.Payments where pm2.hash == pm.PaymentHash select pm2).FirstOrDefault();
                    if (walPm != null)
                    {
                        if (((int)walPm.status) != ((int)pm.Status))
                        {
                            walPm.status = (PaymentStatus)pm.Status;
                            if (pm.Status == Lnrpc.Payment.Types.PaymentStatus.Succeeded)
                                walPm.paymentfee = (long)pm.FeeSat;
                            walletContext.Value.Payments.Update(walPm);
                            walletContext.Value.SaveChanges();
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

    public LNDAccountManager ValidateTokenGetAccount(string authTokenBase64)
    {
        var timedToken = CryptoToolkit.Crypto.VerifySignedTimedToken(authTokenBase64, 120.0);
        if (timedToken == null)
            throw new InvalidOperationException();

        var tk = (from token in walletContext.Value.Tokens where token.pubkey == timedToken.Value.PublicKey && token.id == timedToken.Value.Guid select token).FirstOrDefault();
        if (tk == null)
            throw new InvalidOperationException();

        return GetAccount(Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(tk.pubkey)));
    }

    public LNDAccountManager GetAccount(ECXOnlyPubKey pubkey)
    {
        return new LNDAccountManager(conf, this.connectionString, info, pubkey);
    }

    public Guid GetToken(string pubkey)
    {
        var t = (from token in walletContext.Value.Tokens where pubkey == token.pubkey select token).FirstOrDefault();
        if (t == null)
        {
            t = new Token() { id = Guid.NewGuid(), pubkey = pubkey };
            walletContext.Value.Tokens.Add(t);
            walletContext.Value.SaveChanges();
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
                where a.ispending
                select a).ToList();
    }

    public void MarkPayoutAsCompleted(Guid id, string tx)
    {
        var payout = (from po in walletContext.Value.Payouts where po.id == id && po.ispending select po).FirstOrDefault();
        if (payout != null)
        {
            payout.ispending = false;
            payout.tx = tx;
            walletContext.Value.Update(payout);
            walletContext.Value.SaveChanges();
        }
        else
            throw new PayoutAlreadyCompletedException();
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
