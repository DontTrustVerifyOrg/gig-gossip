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
        public string macaroon { get; set; }
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
        public DbSet<Channel> Channels { get; set; }
        public DbSet<Invoice> Invoices { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite(connectionString);
        }

    }

    public class LNDWalletManager
    {
        string connectionString;
        WaletContext walletContext;
        LND.NodesConfiguration conf;
        int idx;
        GetInfoResponse info;
        string account;

        public LNDWalletManager(string connectionString, LND.NodesConfiguration conf, int idx, string account=null, bool deleteDb = false)
        {
            this.connectionString = connectionString;
            this.walletContext = new WaletContext(connectionString);
            this.conf = conf;
            this.idx = idx;
            if (deleteDb)
                walletContext.Database.EnsureDeleted();
            walletContext.Database.EnsureCreated();
            this.info = LND.GetNodeInfo(conf, idx);
            this.account = account;
        }

        public LNDWalletManager Signup(LIT.NodesConfiguration litConf, int litIdx, ECXOnlyPubKey pubkey, ulong accountBalance)
        {
            var acc = LIT.CreateAccount(litConf, litIdx, accountBalance, pubkey.AsHex());
            var mac = acc.Macaroon.ToBase64();
            walletContext.Users.Add(new User() { pubkey = pubkey.AsHex(), macaroon = mac });
            walletContext.SaveChanges();
            var myconf = conf.ForMacaroon(new LND.MacaroonString(mac), this.idx);
            return new LNDWalletManager(this.connectionString, myconf, 1, account:pubkey.AsHex());
        }

        public LNDWalletManager Login(ECXOnlyPubKey pubkey)
        {
            var u = (from user in walletContext.Users where user.pubkey == pubkey.AsHex() select user).FirstOrDefault();
            if (u == null)
                return null;
            var myconf= conf.ForMacaroon(new LND.MacaroonString(u.macaroon), this.idx);
            return new LNDWalletManager(this.connectionString, myconf, 1, account: pubkey.AsHex());
        }

        public string NewAddress()
        {
            var newaddress = LND.NewAddress(conf, idx, this.account);
            walletContext.Addresses.Add(new Address() { address = newaddress, pubkey = account });
            walletContext.SaveChanges();
            return newaddress;
        }

        public long GetBalance(int minConf)
        {
            var listUnspentResp = LND.ListUnspent(conf, idx, minConf, account);
            long balance = 0;
            foreach (var unspent in listUnspentResp.Utxos)
                balance += unspent.AmountSat;
            return balance;
        }

        public string OpenChannel(string nodePubKey, long fundingSatoshis, string closeAddress=null)
        {
            var balance = GetBalance(6);
            if (balance < fundingSatoshis)
                throw new NotEnoughFundsException("You dont have enough satoshis in your wallet", null, Money.Satoshis(fundingSatoshis - balance));
            var channelpoint = LND.OpenChannelSync(conf, idx, nodePubKey, fundingSatoshis, closeAddress, memo: account, privat: true);
            string channelTx;
            if (channelpoint.HasFundingTxidBytes)
                channelTx = BitConverter.ToString(channelpoint.FundingTxidBytes.ToByteArray().Reverse().ToArray()).Replace("-", "").ToLower();
            else
                channelTx = channelpoint.FundingTxidStr;

            var chanpoint = channelTx + ":" + channelpoint.OutputIndex;
            walletContext.Channels.Add(new Channel() { channelpoint = chanpoint, pubkey = account });
            walletContext.SaveChanges();

            return chanpoint;
        }

        public IEnumerable<Lnrpc.Channel> ListChannels(bool openOnly)
        {
            var channels = LND.ListChannels(conf, idx, openOnly);
            var mycps = new HashSet<string>(from channel in walletContext.Channels where channel.pubkey == account select channel.channelpoint);
            return from channel in channels.Channels where mycps.Contains(channel.ChannelPoint) select channel;
        }

        public AsyncServerStreamingCall<CloseStatusUpdate> CloseChannel(string chanpoint, string closeAddress=null)
        {
            if ((from channel in walletContext.Channels where (channel.pubkey == account) && (channel.channelpoint == chanpoint) select channel).Count() == 0)
                throw new InvalidOperationException("Not your channel");
            return LND.CloseChannel(conf, idx, chanpoint,closeAddress);
        }

        public AddHoldInvoiceResp AddHodlInvoice( long satoshis, string memo, byte[] hash, long expiry = 86400)
        {
            var mychanids = from channel in ListChannels(true) select channel.ChanId;
            var ret = LND.AddHodlInvoice(conf, idx, satoshis, memo, hash, expiry, privat: true, info.IdentityPubkey, mychanids.ToList());
            walletContext.Invoices.Add(new Invoice() { pubkey = account, hash = hash.AsHex() });
            walletContext.SaveChanges();
            return ret;
        }

        public AsyncServerStreamingCall<Payment> SendPayment(string paymentRequest, int timeout)
        {
            var mychanids = from channel in ListChannels(true) select channel.ChanId;
            return LND.SendPaymentV2(conf, idx, paymentRequest, timeout, mychanids.ToList());
        }
    }
}

