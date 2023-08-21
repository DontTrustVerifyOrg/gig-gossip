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
            walletContext.Value.FundingAddresses.Add(new Address() { BitcoinAddress = newaddress, PublicKey = publicKey, TxFee = txfee });
            walletContext.Value.SaveChanges();
            return newaddress;
    }

    public Guid RegisterPayout(long satoshis, string btcAddress, long txfee)
    {
        if ((GetAccountBallance() - (long)txfee)< (long)satoshis)
            throw new LNDWalletException(LNDWalletErrorCode.NotEnoughFunds);

        var myid = Guid.NewGuid();

            walletContext.Value.Payouts.Add(new LNDWallet.Payout()
            {
                PayoutId = myid,
                BitcoinAddress = btcAddress,
                PublicKey = publicKey,
                TxFee = txfee,
                IsPending = true,
                Satoshis = satoshis
            });
            walletContext.Value.SaveChanges();
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
            walletContext.Value.Invoices.Add(new Invoice() {
                PaymentHash = hash.AsHex(),
                PublicKey = publicKey,
                PaymentRequest = inv.PaymentRequest,
                Satoshis = (long)satoshis,
                State = InvoiceState.Open,
                TxFee = txfee,
                IsHodlInvoice = true,
                IsSelfManaged = false,
            }) ;
            walletContext.Value.SaveChanges();
        return inv;
    }

    public Lnrpc.AddInvoiceResponse AddInvoice(long satoshis, string memo, long txfee, long expiry)
    {
        var inv = LND.AddInvoice(conf, satoshis, memo, expiry);
            walletContext.Value.Invoices.Add(new Invoice()
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
                throw new LNDWalletException(LNDWalletErrorCode.NotEnoughFunds);

            var selfInv = (from inv in walletContext.Value.Invoices where inv.PaymentHash == decinv.PaymentHash select inv).FirstOrDefault();
            if (selfInv != null) // selfpayment
            {
                selfInv.IsSelfManaged = true;
                selfInv.State = selfInv.IsHodlInvoice ? InvoiceState.Accepted : InvoiceState.Settled;
                walletContext.Value.Invoices.Update(selfInv);
                walletContext.Value.Payments.Add(new Payment()
                {
                    PaymentHash = decinv.PaymentHash,
                    PublicKey = publicKey,
                    Satoshis = selfInv.Satoshis,
                    TxFee = txfee,
                    IsSelfManaged = true,
                    PaymentFee = 0,
                    Status = selfInv.IsHodlInvoice ? PaymentStatus.InFlight : PaymentStatus.Succeeded,
                }) ;
                walletContext.Value.SaveChanges();
            }
            else
            {
                LND.SendPaymentV2(conf, paymentRequest, timeout, (long)feelimit);
                walletContext.Value.Payments.Add(new Payment()
                {
                    PaymentHash = decinv.PaymentHash,
                    PublicKey = publicKey,
                    Satoshis = (long)decinv.NumSatoshis,
                    TxFee = txfee,
                    IsSelfManaged = false,
                    PaymentFee = feelimit,
                    Status = selfInv.IsHodlInvoice ? PaymentStatus.InFlight : PaymentStatus.Succeeded,
                });
                walletContext.Value.SaveChanges();
            }
    }

    public void SettleInvoice(byte[] preimage)
    {
        var hash = CryptoToolkit.Crypto.ComputePaymentHash(preimage);
        var paymentHash = hash.AsHex();
            var selftrans = (from st in walletContext.Value.Invoices where st.PaymentHash == paymentHash && st.IsSelfManaged select st).FirstOrDefault();
            if (selftrans != null)
            {
                selftrans.State = InvoiceState.Settled;
                selftrans.Preimage = preimage;
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
        var hash = paymentHash.AsBytes();
            var selftrans = (from st in walletContext.Value.Invoices where st.PaymentHash == paymentHash && st.IsSelfManaged select st).FirstOrDefault();
            if (selftrans != null)
            {
                selftrans.State = InvoiceState.Cancelled;
                walletContext.Value.Update(selftrans);
                walletContext.Value.SaveChanges();
            }
            else
            {
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
                    inv.State = (InvoiceState)myinv.State;
                    walletContext.Value.Invoices.Update(inv);
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
                    pm.Status = (PaymentStatus)mypm.Status;
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
                    var walInv = (from inv2 in walletContext.Value.Invoices where inv2.PaymentHash == inv.RHash.ToArray().AsHex() select inv2).FirstOrDefault();
                    if (walInv != null)
                    {
                        if (((int)walInv.State) != ((int)inv.State))
                        {
                            walInv.State = (InvoiceState)inv.State;
                            if (inv.State == Lnrpc.Invoice.Types.InvoiceState.Settled)
                                walInv.Preimage = inv.RPreimage.ToArray();
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
                    var walPm = (from pm2 in walletContext.Value.Payments where pm2.PaymentHash == pm.PaymentHash select pm2).FirstOrDefault();
                    if (walPm != null)
                    {
                        if (((int)walPm.Status) != ((int)pm.Status))
                        {
                            walPm.Status = (PaymentStatus)pm.Status;
                            if (pm.Status == Lnrpc.Payment.Types.PaymentStatus.Succeeded)
                                walPm.PaymentFee = (long)pm.FeeSat;
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
                where a.IsPending
                select a).ToList();
    }

    public void MarkPayoutAsCompleted(Guid id, string tx)
    {
        var payout = (from po in walletContext.Value.Payouts where po.PayoutId == id && po.IsPending select po).FirstOrDefault();
        if (payout != null)
        {
            payout.IsPending = false;
            payout.Tx = tx;
            walletContext.Value.Update(payout);
            walletContext.Value.SaveChanges();
        }
        else
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
