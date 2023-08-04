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

namespace LNDWallet;

public class User
{
    [Key]
    public string pubkey { get; set; }
}

public class Address
{
    [Key]
    public string address { get; set; }
    public string pubkey { get; set; }
    public ulong txfee { get; set; }
}

public class Invoice
{
    [Key]
    public string hash { get; set; }
    public string pubkey { get; set; }
    public ulong txfee { get; set; }
    public bool ishodl { get; set; }
}

public class Payment
{
    [Key]
    public string hash { get; set; }
    public string pubkey { get; set; }
    public ulong txfee { get; set; }
}

public class SelfTransaction
{
    [Key]
    public string hash { get; set; }
    public string issuer_pubkey { get; set; }
    public string payer_pubkey { get; set; }
    public ulong txfee { get; set; }
    public bool issettled { get; set; }
    public bool iscancelled { get; set; }
}

public class WaletContext : DbContext
{
    string connectionString;

    public WaletContext(string connectionString)
    {
        this.connectionString = connectionString;
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Address> FundingAddresses { get; set; }
    public DbSet<Address> PayoutAddresses { get; set; }
    public DbSet<Invoice> Invoices { get; set; }
    public DbSet<Payment> Payments { get; set; }
    public DbSet<SelfTransaction> SelfTransactions { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(connectionString);
    }

}

public class NotEnoughFundsInAccountException : Exception
{ }

public class NotEnoughFundsOnChainException : Exception
{ }


public class LNDAccountManager
{
    LND.NodesConfiguration conf;
    int idx;
    WaletContext walletContext;
    GetInfoResponse info;
    string account;

    internal LNDAccountManager(LND.NodesConfiguration conf, int idx, WaletContext walletContext, GetInfoResponse info, string account)
    {
        this.conf = conf;
        this.idx = idx;
        this.account = account;
        this.walletContext = walletContext;
        this.info = info;
    }

    public string NewAddress(ulong txfee)
    {
        var newaddress = LND.NewAddress(conf, idx);
        walletContext.FundingAddresses.Add(new Address() { address = newaddress, pubkey = account, txfee = txfee });
        walletContext.SaveChanges();
        return newaddress;
    }


    public string Payout(ulong satoshis, string btcAddress, ulong txfee)
    {
        if (LND.GetWalletBalance(conf, idx).ConfirmedBalance >= (long)satoshis)
            throw new NotEnoughFundsOnChainException();

        if ((long)(satoshis) > GetAccountBallance() - (long)txfee)
            throw new NotEnoughFundsInAccountException();

        walletContext.PayoutAddresses.Add(new Address() { address = btcAddress, pubkey = account, txfee = txfee });
        walletContext.SaveChanges();
        return LND.SendCoins(conf, idx, btcAddress, "", (long)satoshis);
    }

    public long GetChannelFundingAmount(int minConf)
    {
        var myaddrs = new Dictionary<string, ulong>(
            from a in walletContext.FundingAddresses
            where a.pubkey == account
            select new KeyValuePair<string, ulong>(a.address, a.txfee));

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

    public long GetAlreadyPayedOutAmount(int minConf)
    {
        var myaddrs = new Dictionary<string, ulong>(
            from a in walletContext.PayoutAddresses
            where a.pubkey == account
            select new KeyValuePair<string, ulong>(a.address, a.txfee));

        var transactuinsResp = LND.GetTransactions(conf, idx);
        long balance = 0;
        foreach (var transation in transactuinsResp.Transactions)
            if (transation.NumConfirmations >= minConf)
                foreach (var outp in transation.OutputDetails)
                    if (!outp.IsOurAddress)
                        if (myaddrs.ContainsKey(outp.Address))
                        {
                            balance += outp.Amount;
                            balance += (long)myaddrs[outp.Address];
                        }
        return balance;
    }

    public Invoicesrpc.AddHoldInvoiceResp AddHodlInvoice(long satoshis, string memo, byte[] hash, ulong txfee, long expiry = 86400)
    {
        var inv = LND.AddHodlInvoice(conf, idx, satoshis, memo, hash, expiry);
        walletContext.Invoices.Add(new Invoice() { hash = hash.AsHex(), pubkey = account, txfee = txfee , ishodl=true});
        walletContext.SaveChanges();
        return inv;
    }

    public Lnrpc.AddInvoiceResponse AddInvoice(long satoshis, string memo, ulong txfee)
    {
        var inv = LND.AddInvoice(conf, idx, satoshis, memo);
        walletContext.Invoices.Add(new Invoice() { hash = inv.RHash.ToArray().AsHex(), pubkey = account, txfee = txfee, ishodl=false });
        walletContext.SaveChanges();
        return inv;
    }

    public class CallResult<T>
    {
        public T Value;
        public AsyncServerStreamingCall<T> StreamingCall;

        public async Task<bool> WaitForCondition(Func<T, bool> condition, CancellationToken cancellationToken)
        {
            if (StreamingCall != null)
            {
                while(await StreamingCall.ResponseStream.MoveNext(cancellationToken))
                {
                    if (condition(StreamingCall.ResponseStream.Current))
                        return true;
                    else
                        Thread.Sleep(1);
                }
                return false;
            }
            else
            {
                return condition(Value);
            }
        }
    }

    public async Task WaitForInvoiceCondition(string paymentHash, Func<Lnrpc.Invoice, bool> condition)
    {
        while (!await LookupSubscribeSingleInvoice(paymentHash).WaitForCondition(condition, CancellationToken.None))
            Thread.Sleep(1000);
    }

    public CallResult<Lnrpc.Payment> SendPayment(string paymentRequest, int timeout, ulong txfee)
    {
        var decinv = LND.DecodeInvoice(conf, idx, paymentRequest);
        if ((long)decinv.NumSatoshis > GetAccountBallance() - (long)txfee)
            throw new NotEnoughFundsInAccountException();

        var selfInv = (from inv in walletContext.Invoices where inv.hash == decinv.PaymentHash select inv).FirstOrDefault();
        if (selfInv != null) // selfpayment
        {
            walletContext.SelfTransactions.Add(new SelfTransaction() { hash = decinv.PaymentHash, issuer_pubkey = selfInv.pubkey, payer_pubkey = account, txfee = txfee, issettled = !selfInv.ishodl });
            walletContext.SaveChanges();
            return new CallResult<Lnrpc.Payment> {
                Value = new Lnrpc.Payment() {
                    Status = selfInv.ishodl ? Lnrpc.Payment.Types.PaymentStatus.InFlight : Lnrpc.Payment.Types.PaymentStatus.Succeeded,
                    PaymentHash = decinv.PaymentHash,
                    ValueSat = decinv.NumSatoshis,
                } };
        }
        else
        {
            walletContext.Payments.Add(new Payment() { hash = decinv.PaymentHash, pubkey = account, txfee = txfee });
            walletContext.SaveChanges();
            return new CallResult<Lnrpc.Payment> {
                StreamingCall = LND.SendPaymentV2(conf, idx, paymentRequest, timeout)
            };
        }
    }

    public CallResult<Lnrpc.Invoice> SettleInvoice(byte[] preimage)
    {
        var hash = LND.ComputePaymentHash(preimage);
        var paymentHash = hash.AsHex();
        var selftrans = (from st in walletContext.SelfTransactions where st.hash == paymentHash && !st.iscancelled select st).FirstOrDefault();
        if (selftrans != null)
        {
            selftrans.issettled = true;
            walletContext.Update(selftrans);
            walletContext.SaveChanges();
            var inv = LND.LookupInvoiceV2(conf, idx, hash);
            inv.State = Lnrpc.Invoice.Types.InvoiceState.Settled;
            return new CallResult<Lnrpc.Invoice>() { Value = inv };
        }
        else
        {
            LND.SettleInvoice(conf, idx, preimage);
            return new CallResult<Lnrpc.Invoice>() { StreamingCall = LND.SubscribeSingleInvoice(conf, idx, hash) };
        }
    }

    public CallResult<Lnrpc.Invoice> CancelInvoice(string paymentHash)
    {
        var hash = Convert.FromHexString(paymentHash);
        var selftrans = (from st in walletContext.SelfTransactions where st.hash == paymentHash select st).FirstOrDefault();
        if (selftrans != null)
        {
            selftrans.iscancelled = true;
            walletContext.Update(selftrans);
            walletContext.SaveChanges();
            LND.CancelInvoice(conf, idx, Convert.FromHexString(paymentHash));
            var inv = LND.LookupInvoiceV2(conf, idx, hash);
            inv.State = Lnrpc.Invoice.Types.InvoiceState.Canceled;
            return new CallResult<Lnrpc.Invoice>() { Value = inv };
        }
        else
        {
            LND.CancelInvoice(conf, idx, Convert.FromHexString(paymentHash));
            return new CallResult<Lnrpc.Invoice>() { StreamingCall = LND.SubscribeSingleInvoice(conf, idx, hash) };
        }
    }

    public CallResult<Lnrpc.Invoice> LookupSubscribeSingleInvoice(string paymentHash)
    {
        var hash = Convert.FromHexString(paymentHash);
        var inv = LND.LookupInvoiceV2(conf, idx, hash);
        if (inv.State == Lnrpc.Invoice.Types.InvoiceState.Open)
        {
            var selftrans = (from st in walletContext.SelfTransactions where st.hash == paymentHash select st).FirstOrDefault();
            if (selftrans != null)
            {
                if (selftrans.iscancelled)
                    inv.State = Lnrpc.Invoice.Types.InvoiceState.Canceled;
                else
                    inv.State = selftrans.issettled ? Lnrpc.Invoice.Types.InvoiceState.Settled : Lnrpc.Invoice.Types.InvoiceState.Accepted;
            }
            return new CallResult<Lnrpc.Invoice>() { Value = inv };
        }
        else
        {
            return new CallResult<Lnrpc.Invoice>() { StreamingCall = LND.SubscribeSingleInvoice(conf, idx, hash) };
        }
    }

    public long GetAccountBallance()
    {
        var channelfunds = GetChannelFundingAmount(6);
        var alreadypayedout = GetAlreadyPayedOutAmount(0);

        var myinvoiceHashes = new Dictionary<string, ulong>(
            from inv in walletContext.Invoices
            where inv.pubkey == account
            select new KeyValuePair<string, ulong>(inv.hash, inv.txfee));
        var mypaymentHashes = new Dictionary<string, ulong>(
            from pay in walletContext.Payments
            where pay.pubkey == account
            select new KeyValuePair<string, ulong>(pay.hash, pay.txfee));
        var myselfpaidInvoices = new Dictionary<string, ulong>(
            from tx in walletContext.SelfTransactions
            where tx.issuer_pubkey == account && tx.issettled
            select new KeyValuePair<string, ulong>(tx.hash, tx.txfee));
        var myselfpaimets = new Dictionary<string, ulong>(
            from tx in walletContext.SelfTransactions
            where tx.payer_pubkey == account && tx.issettled
            select new KeyValuePair<string, ulong>(tx.hash, tx.txfee));


        var allInvoices = LND.ListInvoices(conf, idx).Invoices;

        var paidinvoices = (from inv in allInvoices
                            where myinvoiceHashes.ContainsKey(inv.RHash.ToArray().AsHex())
                            && inv.State == Lnrpc.Invoice.Types.InvoiceState.Settled
                            select inv.AmtPaidSat - (long)myinvoiceHashes[inv.RHash.ToArray().AsHex()]).Sum();

        var paidselfinvoices = (from inv in allInvoices
                                where myselfpaidInvoices.ContainsKey(inv.RHash.ToArray().AsHex())
                                select inv.AmtPaidSat - (long)myselfpaidInvoices[inv.RHash.ToArray().AsHex()]).Sum();

        var selfpayments = (from inv in allInvoices
                            where myselfpaimets.ContainsKey(inv.RHash.ToArray().AsHex())
                            select inv.AmtPaidSat + (long)myselfpaimets[inv.RHash.ToArray().AsHex()]).Sum();

        var mypayments = (from pay in LND.ListPayments(conf, idx).Payments
                          where mypaymentHashes.ContainsKey(pay.PaymentHash)
                          && pay.Status == Lnrpc.Payment.Types.PaymentStatus.Succeeded
                          select pay.FeeSat + pay.ValueSat + (long)mypaymentHashes[pay.PaymentHash]).Sum();

        return channelfunds - alreadypayedout + paidinvoices - mypayments + paidselfinvoices - selfpayments;
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

    public LNDAccountManager CreateAccount(ECXOnlyPubKey pubkey)
    {
        walletContext.Users.Add(new User() { pubkey = pubkey.AsHex() });
        walletContext.SaveChanges();
        return new LNDAccountManager(conf, idx, walletContext, info, account: pubkey.AsHex());
    }

    public LNDAccountManager GetAccount(ECXOnlyPubKey pubkey)
    {
        var u = (from user in walletContext.Users where user.pubkey == pubkey.AsHex() select user).FirstOrDefault();
        if (u == null)
            return null;
        return new LNDAccountManager(conf, idx, walletContext, info, account: pubkey.AsHex());
    }

    public string OpenChannel(string nodePubKey, long fundingSatoshis)
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
