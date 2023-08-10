using System.ComponentModel.DataAnnotations;
using CryptoToolkit;
using GigLNDWalletAPIClient;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.RPC;
using NBitcoin.Secp256k1;
using NGigGossip4Nostr;

namespace GigGossipSettler;

public class Preimage
{
    [Key]
    public Guid tid { get; set; }
    public string hash { get; set; }
    public string pubkey { get; set; }
    public string preimage { get; set; }
    public bool revealed { get; set; }
}

public enum GigStatus
{
    Open = 0,
    Accepted = 1,
    Cancelled = 2,
    Disuputed = 3,
    Completed = 4,
}

public class Gig
{
    [Key]
    public Guid tid { get; set; }
    public string senderpubkey { get; set; }
    public string replierpubkey { get; set; }
    public string symmetrickey { get; set; }
    public string paymenthash { get; set; }
    public string networkhash { get; set; }
    public GigStatus status { get; set; }
    public DateTime disputedeadline { get; set; }
}

public class Token
{
    [Key]
    public Guid id { get; set; }
    public string pubkey { get; set; }
}

public class SettlerContext : DbContext
{
    string connectionString;

    public SettlerContext(string connectionString)
    {
        this.connectionString = connectionString;
    }

    public DbSet<Token> Tokens { get; set; }
    public DbSet<Preimage> Preimages { get; set; }
    public DbSet<Gig> Gigs { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(connectionString);
    }

}


public class Settler : CertificationAuthority
{
    private readonly int priceAmountForSettlement;
    protected swaggerClient lndWalletClient;
    protected Guid _walletToken;
    SettlerContext settlerContext;

    public Settler(string caName, ECPrivKey settlerPrivateKey, int priceAmountForSettlement) : base(caName,settlerPrivateKey)
    {
        this.priceAmountForSettlement = priceAmountForSettlement;
    }

    public async Task Init(swaggerClient lndWalletClient, string connectionString, bool deleteDb = false)
    {
        this.lndWalletClient = lndWalletClient;
        this._walletToken = await lndWalletClient.GetTokenAsync(this.CaName);
        this.settlerContext = new SettlerContext(connectionString);
        if (deleteDb)
            settlerContext.Database.EnsureDeleted();
        settlerContext.Database.EnsureCreated();
    }

    protected string walletToken()
    {
        return Crypto.MakeSignedTimedToken(this._CaPrivateKey, DateTime.Now, this._walletToken);
    }

    public Guid GetToken(string pubkey)
    {
        lock (settlerContext)
        {
            var t = (from token in settlerContext.Tokens where pubkey == token.pubkey select token).FirstOrDefault();
            if (t == null)
            {
                t = new Token() { id = Guid.NewGuid(), pubkey = pubkey };
                settlerContext.Tokens.Add(t);
                settlerContext.SaveChanges();
            }
            return t.id;
        }
    }

    public Settler ValidateToken(ECXOnlyPubKey pubkey, string authTokenBase64)
    {
        var guid = CryptoToolkit.Crypto.VerifySignedTimedToken( pubkey, authTokenBase64, 120.0);
        if (guid == null)
            throw new InvalidOperationException();

        var tk = (from token in settlerContext.Tokens where token.pubkey == pubkey.AsHex() && token.id == guid select token).FirstOrDefault();
        if (tk == null)
            throw new InvalidOperationException();

        return this;
    }


    public string GenerateReplyPaymentPreimage(string pubkey, Guid tid)
    {
        var preimage = Crypto.GenerateRandomPreimage();
        var paymentHash = Crypto.ComputePaymentHash(preimage).AsHex();

        lock (settlerContext)
        {
            settlerContext.Preimages.Add(new Preimage() { hash = paymentHash, preimage = preimage.AsHex(), tid = tid, pubkey = pubkey, revealed = false });
            settlerContext.SaveChanges();
        }
        return paymentHash;
    }

    public string GenerateRelatedPreimage(string pubkey, string paymentHash)
    {
        var preimage = Crypto.GenerateRandomPreimage();
        var newPaymentHash = Crypto.ComputePaymentHash(preimage).AsHex();

        lock (settlerContext)
        {
            var pix = (from pi in settlerContext.Preimages where pi.hash == paymentHash select pi).FirstOrDefault();
            if (pix != null)
            {
                settlerContext.Preimages.Add(new Preimage() { hash = newPaymentHash, preimage = preimage.AsHex(), tid = pix.tid, pubkey = pubkey, revealed = false });
                settlerContext.SaveChanges();
            }
        }
        return newPaymentHash;
    }

    public string RevealPreimage(string pubkey, string paymentHash)
    {
        lock (settlerContext)
        {
            var preimage = (from pi in settlerContext.Preimages where pi.pubkey == pubkey && pi.hash == paymentHash && pi.revealed select pi).FirstOrDefault();
            if (preimage == null)
                return "";
            else
                return preimage.preimage;
        }
    }

    public string RevealSymmetricKey(string senderpubkey, Guid tid)
    {
        lock (settlerContext)
        {
            var symkey = (from g in settlerContext.Gigs where g.senderpubkey == senderpubkey && g.tid == tid && g.status == GigStatus.Accepted select g).FirstOrDefault();
            if (symkey == null)
                return "";
            else
                return symkey.symmetrickey;
        }
    }

    public async Task<SettlementTrust> GenerateSettlementTrust(string replierpubkey, byte[] message, string replyInvoice, RequestPayload signedRequestPayload, Certificate replierCertificate)
    {
        var decodedInv = await lndWalletClient.DecodeInvoiceAsync(this.CaName, walletToken(), replyInvoice);
        var invPaymentHash = decodedInv.PaymentHash;
        lock (settlerContext)
        {
            if ((from pi in settlerContext.Preimages where pi.tid == signedRequestPayload.PayloadId && pi.hash == invPaymentHash select pi).FirstOrDefault() == null)
                throw new InvalidOperationException();
        }

        byte[] key = Crypto.GenerateSymmetricKey();
        byte[] encryptedReplyMessage = Crypto.SymmetricEncrypt(key, message);

        var networkInvoicePaymentHash = GenerateReplyPaymentPreimage(this.CaXOnlyPublicKey.AsHex(), signedRequestPayload.PayloadId);
        var networkInvoice = await lndWalletClient.AddHodlInvoiceAsync(this.CaName, walletToken(), priceAmountForSettlement, networkInvoicePaymentHash, "");

        lock (settlerContext)
        {
            settlerContext.Gigs.Add(new Gig() {
                senderpubkey = signedRequestPayload.SenderCertificate.PublicKey,
                replierpubkey = replierpubkey,
                tid = signedRequestPayload.PayloadId,
                symmetrickey = key.AsHex(),
                status = GigStatus.Open,
                networkhash = networkInvoice.PaymentHash,
                paymenthash = decodedInv.PaymentHash,
                disputedeadline = DateTime.MaxValue
            }) ;
            settlerContext.SaveChanges();
        }

        ReplyPayload replyPayload = new ReplyPayload()
        {
            ReplierCertificate = replierCertificate,
            SignedRequestPayload = signedRequestPayload,
            EncryptedReplyMessage = encryptedReplyMessage,
            ReplyInvoice = replyInvoice
        };

        byte[] encryptedReplyPayload = Crypto.EncryptObject(replyPayload, signedRequestPayload.SenderCertificate.GetECXOnlyPubKey(), this._CaPrivateKey);
        byte[] hashOfEncryptedReplyPayload = Crypto.ComputeSha256(new List<byte[]> { encryptedReplyPayload });

        SettlementPromise signedSettlementPromise = new SettlementPromise()
        {
            SettlerCaName = this.CaName,
            HashOfEncryptedReplyPayload = hashOfEncryptedReplyPayload,
            ReplyPaymentAmount = decodedInv.NumSatoshis,
        };
        signedSettlementPromise.Sign(_CaPrivateKey);

        return new SettlementTrust()
        {
            SettlementPromise = signedSettlementPromise,
            NetworkInvoice = networkInvoice.PaymentRequest,
            EncryptedReplyPayload = encryptedReplyPayload
        };
    }

    public void ManageDispute(Guid tid, bool open)
    {
        lock (settlerContext)
        {
            var gig = (from g in settlerContext.Gigs where g.tid == tid && g.status == GigStatus.Accepted select g).FirstOrDefault();
            if (gig != null)
            {
                gig.status = open ? GigStatus.Disuputed : GigStatus.Accepted;
                settlerContext.Update(gig);
                settlerContext.SaveChanges();
            }
        }
    }

    Thread invoiceTrackerThread;
    private long _invoiceTrackerThreadStop = 0;

    public async Task Start()
    {
        _invoiceTrackerThreadStop = 0;
        invoiceTrackerThread = new Thread(async () =>
        {
            while (Interlocked.Read(ref _invoiceTrackerThreadStop) == 0)
            {
                List<Gig> gigs;
                lock (settlerContext)
                {
                    gigs = (from g in settlerContext.Gigs where (g.status == GigStatus.Open || g.status == GigStatus.Accepted) && DateTime.Now > g.disputedeadline select g).ToList();
                }

                foreach (var gig in gigs)
                {
                    if (gig.status == GigStatus.Open)
                    {
                        var network_state = await lndWalletClient.GetInvoiceStateAsync(this.CaName, walletToken(), gig.networkhash);
                        var payment_state = await lndWalletClient.GetInvoiceStateAsync(this.CaName, walletToken(), gig.networkhash);
                        if (network_state == "Accepted" && payment_state == "Accepted")
                        {
                            lock (settlerContext)
                            {
                                gig.status = GigStatus.Accepted;
                                gig.disputedeadline = DateTime.Now + TimeSpan.FromSeconds(10);
                                settlerContext.Gigs.Update(gig);
                                settlerContext.SaveChanges();
                            }
                        }
                        else if (network_state == "Cancelled" || payment_state == "Cancelled")
                        {
                            lock (settlerContext)
                            {
                                gig.status = GigStatus.Cancelled;
                                settlerContext.Gigs.Update(gig);
                                settlerContext.SaveChanges();
                            }
                        }
                    }
                    else if (gig.status == GigStatus.Accepted)
                    {
                        if (DateTime.Now > gig.disputedeadline) // shuld be alwas true due to where in linq
                        {
                            lock (settlerContext)
                            {
                                var preims = (from pi in settlerContext.Preimages where pi.tid == gig.tid select pi).ToList();
                                foreach (var pi in preims)
                                    pi.revealed = true;
                                settlerContext.UpdateRange(preims);
                                gig.status = GigStatus.Completed;
                                settlerContext.Update(gig);
                                settlerContext.SaveChanges();
                            }
                        }
                    }
                }
            }
        });


        invoiceTrackerThread.Start();

    }

    public void Stop()
    {
        Interlocked.Add(ref _invoiceTrackerThreadStop, 1);
        invoiceTrackerThread.Join();
    }
}

