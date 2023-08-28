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

public class Settler : CertificationAuthority
{
    private TimeSpan invoicePaymentTimeout;
    private long priceAmountForSettlement;
    private swaggerClient lndWalletClient;
    private Guid walletTokenGuid;
    private ThreadLocal<SettlerContext> settlerContext;

    public Settler(Uri serviceUri, ECPrivKey settlerPrivateKey, long priceAmountForSettlement, TimeSpan invoicePaymentTimeout) : base(serviceUri, settlerPrivateKey)
    {
        this.priceAmountForSettlement = priceAmountForSettlement;
        this.invoicePaymentTimeout = invoicePaymentTimeout;
    }

    public void Init(swaggerClient lndWalletClient, string connectionString, bool deleteDb = false)
    {
        this.lndWalletClient = lndWalletClient;
        this.walletTokenGuid = lndWalletClient.GetTokenAsync(this.CaXOnlyPublicKey.AsHex()).Result;
        settlerContext = new ThreadLocal<SettlerContext>(() => new SettlerContext(connectionString));
        if (deleteDb)
            settlerContext.Value.Database.EnsureDeleted();
        settlerContext.Value.Database.EnsureCreated();
    }

    protected string MakeAuthToken()
    {
        return Crypto.MakeSignedTimedToken(this._CaPrivateKey, DateTime.Now, this.walletTokenGuid);
    }

    public Guid GetTokenGuid(string pubkey)
    {
        var t = (from token in settlerContext.Value.Tokens where pubkey == token.PublicKey select token).FirstOrDefault();
        if (t == null)
        {
            t = new Token() { TokenId = Guid.NewGuid(), PublicKey = pubkey };
            settlerContext.Value.AddObject(t);
        }
        return t.TokenId;
    }


    public string ValidateAuthToken(string authTokenBase64)
    {
        var timedToken = CryptoToolkit.Crypto.VerifySignedTimedToken(authTokenBase64, 120.0);
        if (timedToken == null)
            throw new SettlerException(SettlerErrorCode.InvalidToken);

        var tk = (from token in settlerContext.Value.Tokens where token.PublicKey == timedToken.Value.PublicKey && token.TokenId == timedToken.Value.Guid select token).FirstOrDefault();
        if (tk == null)
            throw new SettlerException(SettlerErrorCode.InvalidToken);

        return tk.PublicKey;
    }


    public void GiveUserProperty(string pubkey, string name, byte[] value, DateTime validTill)
    {
        if (
            (from u in settlerContext.Value.UserProperties
             where u.Name == name && u.PublicKey == pubkey
             select u)
                .ExecuteUpdate(i => i
                .SetProperty(a => a.Value, a => value)
                .SetProperty(a => a.IsRevoked, a => false)
                .SetProperty(a => a.ValidTill, a => validTill))
             == 0)
        {
            settlerContext.Value.AddObject(new UserProperty()
            {
                PropertyId = Guid.NewGuid(),
                IsRevoked = false,
                Name = name,
                PublicKey = pubkey,
                ValidTill = validTill,
                Value = value
            });
        }
    }

    public void RevokeUserProperty(string pubkey, string name)
    {
        var up = (from u in settlerContext.Value.UserProperties where u.Name == name && u.PublicKey == pubkey && u.IsRevoked == false select u).FirstOrDefault();
        if (up != null)
        {
            up.IsRevoked = true;
            settlerContext.Value.SaveObject(up);
            var certids = (from cp in settlerContext.Value.CertificateProperties where cp.PropertyId == up.PropertyId select cp.CertificateId).ToArray();
            var certs = (from c in settlerContext.Value.UserCertificates where certids.Contains(c.CertificateId) select c).ToArray();
            foreach (var c in certs)
            {
                c.IsRevoked = true;
                settlerContext.Value.SaveObject(c);
            }
        }
    }


    public Certificate IssueCertificate(string pubkey, string[] properties)
    {
        var props = (from u in settlerContext.Value.UserProperties where u.PublicKey == pubkey && !u.IsRevoked && u.ValidTill >= DateTime.Now && properties.Contains(u.Name) select u).ToArray();
        var hasprops = new HashSet<string>(properties);
        if (!hasprops.SetEquals((from p in props select p.Name)))
            throw new SettlerException(SettlerErrorCode.PropertyNotGranted);
        var minDate = (from p in props select p.ValidTill).Min();
        var prp = new Dictionary<string, byte[]>((from p in props select KeyValuePair.Create<string, byte[]>(p.Name, p.Value)));
        var cert = base.IssueCertificate(pubkey.AsECXOnlyPubKey(), prp, minDate, DateTime.Now);
        var certProps = (from p in props select new CertificateProperty() { CertificateId = cert.Id, PropertyId = p.PropertyId }).ToArray();
        settlerContext.Value.AddObjectRange(certProps);
        settlerContext.Value.AddObject(new UserCertificate() { PublicKey = pubkey, CertificateId = cert.Id, IsRevoked = false, TheCertificate = Crypto.SerializeObject(cert) });
        return cert;
    }

    public Guid[] ListCertificates(string pubkey)
    {
        return (from cert in settlerContext.Value.UserCertificates where cert.PublicKey == pubkey && !cert.IsRevoked select cert.CertificateId).ToArray();
    }

    public Certificate GetCertificate(string pubkey, Guid certid)
    {
        var crt = (from c in settlerContext.Value.UserCertificates where c.PublicKey == pubkey && c.CertificateId == certid && !c.IsRevoked select c.TheCertificate).FirstOrDefault();
        if (crt == null)
            throw new SettlerException(SettlerErrorCode.UnknownCertificate);
        return Crypto.DeserializeObject<Certificate>(crt);
    }

    public bool IsCertificateRevoked(Guid certid)
    {
        return (from c in settlerContext.Value.UserCertificates where c.CertificateId == certid && c.IsRevoked select c).FirstOrDefault() != null;
    }

    public string GenerateReplyPaymentPreimage(string pubkey, Guid tid, string replierPubKey)
    {
        var preimage = Crypto.GenerateRandomPreimage();
        var paymentHash = Crypto.ComputePaymentHash(preimage).AsHex();

        settlerContext.Value.AddObject(new InvoicePreimage() { PaymentHash = paymentHash, Preimage = preimage.AsHex(), GigId = tid, ReplierPublicKey= replierPubKey, PublicKey = pubkey, IsRevealed = false });
        return paymentHash;
    }

    public string GenerateRelatedPreimage(string pubkey, string paymentHash)
    {
        var preimage = Crypto.GenerateRandomPreimage();
        var newPaymentHash = Crypto.ComputePaymentHash(preimage).AsHex();

        var pix = (from pi in settlerContext.Value.Preimages where pi.PaymentHash == paymentHash select pi).FirstOrDefault();
        if (pix != null)
        {
            settlerContext.Value.AddObject(new InvoicePreimage() { PaymentHash = newPaymentHash, Preimage = preimage.AsHex(), GigId = pix.GigId, ReplierPublicKey=pix.ReplierPublicKey, PublicKey = pubkey, IsRevealed = false });
        }
        return newPaymentHash;
    }

    public string RevealPreimage(string pubkey, string paymentHash)
    {
        var preimage = (from pi in settlerContext.Value.Preimages where pi.PublicKey == pubkey && pi.PaymentHash == paymentHash && pi.IsRevealed select pi).FirstOrDefault();
        if (preimage == null)
            return "";
        else
            return preimage.Preimage;
    }

    public string RevealSymmetricKey(string senderpubkey, Guid tid, string replierpubkey)
    {
        var symkey = (from g in settlerContext.Value.Gigs where g.SenderPublicKey == senderpubkey && g.ReplierPublicKey == replierpubkey && g.GigId == tid && g.Status == GigStatus.Accepted select g).FirstOrDefault();
        if (symkey == null)
            return "";
        else
            return symkey.SymmetricKey;
    }

    public async Task<SettlementTrust> GenerateSettlementTrustAsync(string replierpubkey, byte[] message, string replyInvoice, RequestPayload signedRequestPayload, Certificate replierCertificate)
    {
        var decodedInv = await lndWalletClient.DecodeInvoiceAsync(MakeAuthToken(), replyInvoice);
        var invPaymentHash = decodedInv.PaymentHash;
        if ((from pi in settlerContext.Value.Preimages where pi.GigId == signedRequestPayload.PayloadId && pi.PaymentHash == invPaymentHash select pi).FirstOrDefault() == null)
            throw new SettlerException(SettlerErrorCode.UnknownPreimage);

        byte[] key = Crypto.GenerateSymmetricKey();
        byte[] encryptedReplyMessage = Crypto.SymmetricEncrypt(key, message);

        var networkInvoicePaymentHash = GenerateReplyPaymentPreimage(this.CaXOnlyPublicKey.AsHex(), signedRequestPayload.PayloadId, replierpubkey);
        var networkInvoice = await lndWalletClient.AddHodlInvoiceAsync(
             MakeAuthToken(), priceAmountForSettlement, networkInvoicePaymentHash, "", (long)invoicePaymentTimeout.TotalSeconds);

        settlerContext.Value.AddObject(new Gig()
        {
            SenderPublicKey = signedRequestPayload.SenderCertificate.PublicKey,
            ReplierPublicKey = replierpubkey,
            GigId = signedRequestPayload.PayloadId,
            SymmetricKey = key.AsHex(),
            Status = GigStatus.Open,
            NetworkPaymentHash = networkInvoice.PaymentHash,
            PaymentHash = decodedInv.PaymentHash,
            DisputeDeadline = DateTime.MaxValue
        });

        ReplyPayload replyPayload = new ReplyPayload()
        {
            ReplierCertificate = replierCertificate,
            SignedRequestPayload = signedRequestPayload,
            EncryptedReplyMessage = encryptedReplyMessage,
            ReplyInvoice = replyInvoice
        };

        byte[] encryptedReplyPayload = Crypto.EncryptObject(replyPayload, signedRequestPayload.SenderCertificate.PublicKey.AsECXOnlyPubKey(), this._CaPrivateKey);
        byte[] hashOfEncryptedReplyPayload = Crypto.ComputeSha256(new List<byte[]> { encryptedReplyPayload });

        SettlementPromise signedSettlementPromise = new SettlementPromise()
        {
            ServiceUri = this.ServiceUri,
            HashOfEncryptedReplyPayload = hashOfEncryptedReplyPayload,
            ReplyPaymentAmount = decodedInv.NumSatoshis,
            NetworkPaymentHash = networkInvoicePaymentHash.AsBytes(),
        };
        signedSettlementPromise.Sign(_CaPrivateKey);

        return new SettlementTrust()
        {
            SettlementPromise = signedSettlementPromise,
            NetworkInvoice = networkInvoice.PaymentRequest,
            EncryptedReplyPayload = encryptedReplyPayload
        };
    }

    public void ManageDispute(Guid tid, string replierpubkey, bool open)
    {
        var gig = (from g in settlerContext.Value.Gigs where g.GigId == tid && g.ReplierPublicKey==replierpubkey && g.Status == GigStatus.Accepted select g).FirstOrDefault();
        if (gig != null)
        {
            gig.Status = open ? GigStatus.Disuputed : GigStatus.Accepted;
            settlerContext.Value.SaveObject(gig);
        }
    }

    Thread invoiceTrackerThread;
    private long _invoiceTrackerThreadStop;

    public void Start()
    {
        _invoiceTrackerThreadStop = 0;
        invoiceTrackerThread = new Thread( async () =>
        {
            while (Interlocked.Read(ref _invoiceTrackerThreadStop) == 0)
            {
                List<Gig> gigs = (from g in settlerContext.Value.Gigs where (g.Status == GigStatus.Open || g.Status == GigStatus.Accepted) select g).ToList();

                foreach (var gig in gigs)
                {
                    if (gig.Status == GigStatus.Open)
                    {
                        var network_state = await lndWalletClient.GetInvoiceStateAsync(MakeAuthToken(), gig.NetworkPaymentHash);
                        var payment_state = await lndWalletClient.GetInvoiceStateAsync(MakeAuthToken(), gig.PaymentHash);
                        if (network_state == "Accepted" && payment_state == "Accepted")
                        {
                            gig.Status = GigStatus.Accepted;
                            gig.DisputeDeadline = DateTime.Now + TimeSpan.FromSeconds(10);
                            settlerContext.Value.SaveObject(gig);
                        }
                        else if (network_state == "Cancelled" || payment_state == "Cancelled")
                        {
                            gig.Status = GigStatus.Cancelled;
                            settlerContext.Value.SaveObject(gig);
                        }
                    }
                    else if (gig.Status == GigStatus.Accepted)
                    {
                        if (DateTime.Now > gig.DisputeDeadline) 
                        {
                            var preims = (from pi in settlerContext.Value.Preimages where pi.ReplierPublicKey==gig.ReplierPublicKey && pi.GigId == gig.GigId select pi).ToList();
                            foreach (var pi in preims)
                                pi.IsRevealed = true;
                            settlerContext.Value.SaveObjectRange(preims);
                            gig.Status = GigStatus.Completed;
                            settlerContext.Value.SaveObject(gig);
                            var settletPi = (from pi in preims where pi.PublicKey == this.CaXOnlyPublicKey.AsHex() select pi).FirstOrDefault();
                            if (settletPi == null)
                                throw new SettlerException(SettlerErrorCode.UnknownPreimage);
                            lndWalletClient.SettleInvoiceAsync(MakeAuthToken(), settletPi.Preimage).Wait(); // settle settlers network invoice
                        }
                    }
                }
                Thread.Sleep(1000);
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

