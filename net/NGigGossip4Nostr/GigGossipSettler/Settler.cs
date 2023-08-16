using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Xml.Linq;
using CryptoToolkit;
using GigLNDWalletAPIClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using NBitcoin;
using NBitcoin.RPC;
using NBitcoin.Secp256k1;
using Newtonsoft.Json.Linq;
using NGigGossip4Nostr;

namespace GigGossipSettler;

public class UserProperty
{
    [Key]
    public Guid propid { get; set; }
    public string pubkey { get; set; }
    public string name { get; set; }
    public byte[] value { get; set; }
    public DateTime validtill { get; set; }
    public bool isrevoked { get; set; }
}

public class CertificateProperty
{
    [Key]
    public Guid certid { get; set; }
    public Guid propid { get; set; }
}

public class UserCertificate
{
    [Key]
    public Guid certid { get; set; }
    public string pubkey { get; set; }
    public byte[] certificate { get; set; }
    public bool isrevoked { get; set; }
}

public class Preimage
{
    [Key]
    public string hash { get; set; }
    public Guid tid { get; set; }
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
    public DbSet<UserProperty> UserProperties { get; set; }
    public DbSet<CertificateProperty> CertificateProperties { get; set; }
    public DbSet<UserCertificate> UserCertificates { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(connectionString);
    }

}


public class Settler : CertificationAuthority
{
    private TimeSpan invoicePaymentTimeout;
    private long priceAmountForSettlement;
    protected swaggerClient lndWalletClient;
    protected Guid _walletToken;
    private string connectionString;
    ThreadLocal<SettlerContext> settlerContext;

    public Settler(Uri serviceUri, ECPrivKey settlerPrivateKey, long priceAmountForSettlement, TimeSpan invoicePaymentTimeout) : base(serviceUri, settlerPrivateKey)
    {
        this.priceAmountForSettlement = priceAmountForSettlement;
        this.invoicePaymentTimeout = invoicePaymentTimeout;
    }

    public async Task Init(swaggerClient lndWalletClient, string connectionString, bool deleteDb = false)
    {
        this.lndWalletClient = lndWalletClient;
        this._walletToken = await lndWalletClient.GetTokenAsync(this.CaXOnlyPublicKey.AsHex());
        this.connectionString = connectionString;
        settlerContext = new ThreadLocal<SettlerContext>(() => new SettlerContext(connectionString));
        if (deleteDb)
            settlerContext.Value.Database.EnsureDeleted();
        settlerContext.Value.Database.EnsureCreated();
    }

    protected string walletToken()
    {
        return Crypto.MakeSignedTimedToken(this._CaPrivateKey, DateTime.Now, this._walletToken);
    }

    public Guid GetToken(string pubkey)
    {
        var t = (from token in settlerContext.Value.Tokens where pubkey == token.pubkey select token).FirstOrDefault();
        if (t == null)
        {
            t = new Token() { id = Guid.NewGuid(), pubkey = pubkey };
            settlerContext.Value.Tokens.Add(t);
            settlerContext.Value.SaveChanges();
        }
        return t.id;
    }

    public string ValidateToken(string authTokenBase64)
    {
        var timedToken = CryptoToolkit.Crypto.VerifySignedTimedToken(authTokenBase64, 120.0);
        if (timedToken == null)
            throw new InvalidOperationException();

        var tk = (from token in settlerContext.Value.Tokens where token.pubkey == timedToken.Value.PublicKey && token.id == timedToken.Value.Guid select token).FirstOrDefault();
        if (tk == null)
            throw new InvalidOperationException();

        return tk.pubkey;
    }


    public void GiveUserProperty(string pubkey, string name, byte[] value, DateTime validTill)
    {
        var up = (from u in settlerContext.Value.UserProperties where u.name == name && u.pubkey == pubkey select u).FirstOrDefault();
        if (up == null)
        {
            settlerContext.Value.UserProperties.Add(new UserProperty()
            {
                propid = Guid.NewGuid(),
                isrevoked = false,
                name = name,
                pubkey = pubkey,
                validtill = validTill,
                value = value
            });
        }
        else
        {
            up.value = value;
            up.isrevoked = false;
            up.validtill = validTill;
            settlerContext.Value.UserProperties.Update(up);
        }
        settlerContext.Value.SaveChanges();
    }

    public void RevokeUserProperty(string pubkey, string name)
    {
        var up = (from u in settlerContext.Value.UserProperties where u.name == name && u.pubkey == pubkey && u.isrevoked == false select u).FirstOrDefault();
        if (up != null)
        {
            up.isrevoked = true;
            settlerContext.Value.UserProperties.Update(up);
            var certids = (from cp in settlerContext.Value.CertificateProperties where cp.propid == up.propid select cp.certid).ToArray();
            var certs = (from c in settlerContext.Value.UserCertificates where certids.Contains(c.certid) select c).ToArray();
            foreach (var c in certs)
            {
                c.isrevoked = true;
                settlerContext.Value.UserCertificates.Update(c);
            }
            settlerContext.Value.SaveChanges();
        }
    }

    public Certificate IssueCertificate(string pubkey, string[] properties)
    {
        var props = (from u in settlerContext.Value.UserProperties where u.pubkey == pubkey && !u.isrevoked && u.validtill >= DateTime.Now && properties.Contains(u.name) select u).ToArray();
        var hasprops = new HashSet<string>(properties);
        if (!hasprops.SetEquals((from p in props select p.name)))
            throw new InvalidOperationException();
        var minDate = (from p in props select p.validtill).Min();
        var prp = new Dictionary<string, byte[]>((from p in props select KeyValuePair.Create<string, byte[]>(p.name, p.value)));
        var cert = base.IssueCertificate(Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(pubkey)), prp, minDate, DateTime.Now);
        var certProps = (from p in props select new CertificateProperty() { certid = cert.Id, propid = p.propid }).ToArray();
        settlerContext.Value.CertificateProperties.AddRange(certProps);
        settlerContext.Value.UserCertificates.Add(new UserCertificate() { pubkey = pubkey, certid = cert.Id, isrevoked = false, certificate = Crypto.SerializeObject(cert) });
        settlerContext.Value.SaveChanges();
        return cert;
    }

    public Guid[] ListCertificates(string pubkey)
    {
        return (from cert in settlerContext.Value.UserCertificates where cert.pubkey == pubkey && !cert.isrevoked select cert.certid).ToArray();
    }

    public Certificate GetCertificate(string pubkey, Guid certid)
    {
        var crt = (from c in settlerContext.Value.UserCertificates where c.pubkey == pubkey && c.certid == certid && !c.isrevoked select c.certificate).FirstOrDefault();
        if (crt == null)
            throw new InvalidOperationException();
        return Crypto.DeserializeObject<Certificate>(crt);
    }

    public bool IsCertificateRevoked(Guid certid)
    {
        return (from c in settlerContext.Value.UserCertificates where c.certid == certid && c.isrevoked select c).FirstOrDefault() != null;
    }

    public string GenerateReplyPaymentPreimage(string pubkey, Guid tid)
    {
        var preimage = Crypto.GenerateRandomPreimage();
        var paymentHash = Crypto.ComputePaymentHash(preimage).AsHex();

        settlerContext.Value.Preimages.Add(new Preimage() { hash = paymentHash, preimage = preimage.AsHex(), tid = tid, pubkey = pubkey, revealed = false });
        settlerContext.Value.SaveChanges();
        return paymentHash;
    }

    public string GenerateRelatedPreimage(string pubkey, string paymentHash)
    {
        var preimage = Crypto.GenerateRandomPreimage();
        var newPaymentHash = Crypto.ComputePaymentHash(preimage).AsHex();

        var pix = (from pi in settlerContext.Value.Preimages where pi.hash == paymentHash select pi).FirstOrDefault();
        if (pix != null)
        {
            settlerContext.Value.Preimages.Add(new Preimage() { hash = newPaymentHash, preimage = preimage.AsHex(), tid = pix.tid, pubkey = pubkey, revealed = false });
            settlerContext.Value.SaveChanges();
        }
        return newPaymentHash;
    }

    public string RevealPreimage(string pubkey, string paymentHash)
    {
        var preimage = (from pi in settlerContext.Value.Preimages where pi.pubkey == pubkey && pi.hash == paymentHash && pi.revealed select pi).FirstOrDefault();
        if (preimage == null)
            return "";
        else
            return preimage.preimage;
    }

    public string RevealSymmetricKey(string senderpubkey, Guid tid)
    {
        var symkey = (from g in settlerContext.Value.Gigs where g.senderpubkey == senderpubkey && g.tid == tid && g.status == GigStatus.Accepted select g).FirstOrDefault();
        if (symkey == null)
            return "";
        else
            return symkey.symmetrickey;
    }

    public async Task<SettlementTrust> GenerateSettlementTrust(string replierpubkey, byte[] message, string replyInvoice, RequestPayload signedRequestPayload, Certificate replierCertificate)
    {
        var decodedInv = await lndWalletClient.DecodeInvoiceAsync(walletToken(), replyInvoice);
        var invPaymentHash = decodedInv.PaymentHash;
        if ((from pi in settlerContext.Value.Preimages where pi.tid == signedRequestPayload.PayloadId && pi.hash == invPaymentHash select pi).FirstOrDefault() == null)
            throw new InvalidOperationException();

        byte[] key = Crypto.GenerateSymmetricKey();
        byte[] encryptedReplyMessage = Crypto.SymmetricEncrypt(key, message);

        var networkInvoicePaymentHash = GenerateReplyPaymentPreimage(this.CaXOnlyPublicKey.AsHex(), signedRequestPayload.PayloadId);
        var networkInvoice = await lndWalletClient.AddHodlInvoiceAsync(
             walletToken(), priceAmountForSettlement, networkInvoicePaymentHash, "", (long)invoicePaymentTimeout.TotalSeconds);

        settlerContext.Value.Gigs.Add(new Gig()
        {
            senderpubkey = signedRequestPayload.SenderCertificate.PublicKey,
            replierpubkey = replierpubkey,
            tid = signedRequestPayload.PayloadId,
            symmetrickey = key.AsHex(),
            status = GigStatus.Open,
            networkhash = networkInvoice.PaymentHash,
            paymenthash = decodedInv.PaymentHash,
            disputedeadline = DateTime.MaxValue
        });
        settlerContext.Value.SaveChanges();

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
            ServiceUri = this.ServiceUri,
            HashOfEncryptedReplyPayload = hashOfEncryptedReplyPayload,
            ReplyPaymentAmount = decodedInv.NumSatoshis,
            NetworkPaymentHash = Convert.FromHexString(networkInvoicePaymentHash),
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
        var gig = (from g in settlerContext.Value.Gigs where g.tid == tid && g.status == GigStatus.Accepted select g).FirstOrDefault();
        if (gig != null)
        {
            gig.status = open ? GigStatus.Disuputed : GigStatus.Accepted;
            settlerContext.Value.Update(gig);
            settlerContext.Value.SaveChanges();
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
                List<Gig> gigs = (from g in settlerContext.Value.Gigs where (g.status == GigStatus.Open || g.status == GigStatus.Accepted) && DateTime.Now > g.disputedeadline select g).ToList();

                foreach (var gig in gigs)
                {
                    if (gig.status == GigStatus.Open)
                    {
                        var network_state = await lndWalletClient.GetInvoiceStateAsync(walletToken(), gig.networkhash);
                        var payment_state = await lndWalletClient.GetInvoiceStateAsync(walletToken(), gig.networkhash);
                        if (network_state == "Accepted" && payment_state == "Accepted")
                        {
                            gig.status = GigStatus.Accepted;
                            gig.disputedeadline = DateTime.Now + TimeSpan.FromSeconds(10);
                            settlerContext.Value.Gigs.Update(gig);
                            settlerContext.Value.SaveChanges();
                        }
                        else if (network_state == "Cancelled" || payment_state == "Cancelled")
                        {
                            gig.status = GigStatus.Cancelled;
                            settlerContext.Value.Gigs.Update(gig);
                            settlerContext.Value.SaveChanges();
                        }
                    }
                    else if (gig.status == GigStatus.Accepted)
                    {
                        if (DateTime.Now > gig.disputedeadline) // shuld be alwas true due to where in linq
                        {
                            var preims = (from pi in settlerContext.Value.Preimages where pi.tid == gig.tid select pi).ToList();
                            foreach (var pi in preims)
                                pi.revealed = true;
                            settlerContext.Value.UpdateRange(preims);
                            gig.status = GigStatus.Completed;
                            settlerContext.Value.Update(gig);
                            settlerContext.Value.SaveChanges();
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

