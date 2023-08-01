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
using LITClient;

namespace LNDWallet
{
    public class User
    {
        [Key]
        public string pubkey { get; set; }
        public byte[] macaroon { get; set; }
    }

    public class Address
    {
        [Key]
        public string address { get; set; }
        public string pubkey { get; set; }
    }

    public class Channel
    {
        [Key]
        public string channelpoint { get; set; }
        public string pubkey { get; set; }
    }

    public class Invoice
    {
        [Key]
        public string hash { get; set; }
        public string pubkey { get; set; }
    }

    public class WaletContext : DbContext
    {
        string connectionString;

        public WaletContext(string connectionString)
        {
            this.connectionString = connectionString;
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Address> Addresses { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite(connectionString);
        }

    }

    public class LNDAccountManager
    {
        LND.NodesConfiguration conf;
        int idx;
        string account;
        public LNDAccountManager(LND.NodesConfiguration conf, int idx, string account )
        {
            this.conf = conf;
            this.idx = idx;
            this.account = account;
        }

        public AddHoldInvoiceResp AddHodlInvoice(long satoshis, string memo, byte[] hash, long expiry = 86400)
        {
            return LND.AddHodlInvoice(conf, idx, satoshis, memo, hash, expiry);
        }

        public AddInvoiceResponse AddInvoice(long satoshis, string memo)
        {
            return LND.AddInvoice(conf, idx, satoshis, memo);
        }

        public AsyncServerStreamingCall<Payment> SendPayment(string paymentRequest, int timeout)
        {
            return LND.SendPaymentV2(conf, idx, paymentRequest, timeout);
        }
    }

    public class LNDWalletManager
    {
        string connectionString;
        WaletContext walletContext;
        LND.NodesConfiguration conf;
        int idx;
        LIT.NodesConfiguration litConf;
        int litIdx;
        GetInfoResponse info;

        public LNDWalletManager(string connectionString, LND.NodesConfiguration conf, int idx, LIT.NodesConfiguration litConf, int litIdx, GetInfoResponse nodeInfo, bool deleteDb = false)
        {
            this.connectionString = connectionString;
            this.walletContext = new WaletContext(connectionString);
            this.conf = conf;
            this.idx = idx;
            this.litConf = litConf;
            this.litIdx = litIdx;
            if (deleteDb)
                walletContext.Database.EnsureDeleted();
            walletContext.Database.EnsureCreated();
            this.info = nodeInfo;
        }

        public LNDAccountManager CreateAccount(ECXOnlyPubKey pubkey, ulong initialAccountBalance)
        {
            var acc = LIT.CreateAccount(litConf, litIdx, initialAccountBalance, pubkey.AsHex());
            var mac = acc.Macaroon.ToArray();
            walletContext.Users.Add(new User() { pubkey = pubkey.AsHex(), macaroon = mac });
            walletContext.SaveChanges();
            var myconf = conf.ForMacaroon(new LND.MacaroonString(mac), this.idx);
            return new LNDAccountManager(myconf, 1, account:pubkey.AsHex());
        }

        public LNDAccountManager GetAccount(ECXOnlyPubKey pubkey)
        {
            var u = (from user in walletContext.Users where user.pubkey == pubkey.AsHex() select user).FirstOrDefault();
            if (u == null)
                return null;
            var myconf= conf.ForMacaroon(new LND.MacaroonString(u.macaroon), this.idx);
            return new LNDAccountManager( myconf, 1, account: pubkey.AsHex());
        }

        public string NewAddress(string account)
        {
            var newaddress = LND.NewAddress(conf, idx);
            walletContext.Addresses.Add(new Address() { address = newaddress, pubkey = account });
            walletContext.SaveChanges();
            return newaddress;
        }

        public long GetAccountOnChainBalance(string account, int minConf)
        {
            var myaddrs = new HashSet<string>(from a in walletContext.Addresses where a.pubkey == account select a.address);
            var transactuinsResp = LND.GetTransactions(conf, idx);
            long balance = 0;
            foreach (var transation in transactuinsResp.Transactions)
                if (transation.NumConfirmations >= minConf)
                    foreach (var outp in transation.OutputDetails)
                        if (outp.IsOurAddress)
                            if (myaddrs.Contains(outp.Address))
                                balance += outp.Amount;
            return balance;
        }

        public string OpenChannel(string nodePubKey, long fundingSatoshis, string closeAddress=null)
        {
            var channelpoint = LND.OpenChannelSync(conf, idx, nodePubKey, fundingSatoshis, closeAddress,  privat: true);
            string channelTx;
            if (channelpoint.HasFundingTxidBytes)
                channelTx = BitConverter.ToString(channelpoint.FundingTxidBytes.ToByteArray().Reverse().ToArray()).Replace("-", "").ToLower();
            else
                channelTx = channelpoint.FundingTxidStr;

            var chanpoint = channelTx + ":" + channelpoint.OutputIndex;

            return chanpoint;
        }


        public ListChannelsResponse ListChannels(bool openOnly)
        {
            return LND.ListChannels(conf, idx, openOnly);
        }

        public AsyncServerStreamingCall<CloseStatusUpdate> CloseChannel(string chanpoint, string closeAddress=null)
        {
            return LND.CloseChannel(conf, idx, chanpoint,closeAddress);
        }


    }
}

