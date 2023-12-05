using CryptoToolkit;
using GigGossipFrames;
using GigGossipSettler.Exceptions;
using GigGossipSettlerAPIClient;
using GigLNDWalletAPIClient;
using Microsoft.EntityFrameworkCore;
using NBitcoin.Secp256k1;
using NGigGossip4Nostr;
using Quartz;
using Quartz.Impl;
using System.Diagnostics;

namespace GigGossipSettler;

public class PreimageRevealEventArgs : EventArgs
{
    public required string PaymentHash { get; set; }
    public required string Preimage { get; set; }
}
public delegate void PreimageRevealEventHandler(object sender, PreimageRevealEventArgs e);


public class SymmetricKeyRevealEventArgs : EventArgs
{
    public required Guid GigId { get; set; }
    public required Guid ReplierCertificateId { get; set; }
    public required string SymmetricKey { get; set; }
}
public delegate void SymmetricKeyRevealEventHandler(object sender, SymmetricKeyRevealEventArgs e);

public class Settler : CertificationAuthority
{
    public event SymmetricKeyRevealEventHandler OnSymmetricKeyReveal;

    public void FireOnSymmetricKeyReveal(Guid gidId, Guid replierCertificateId, string symmetricKey)
    {
        if (OnSymmetricKeyReveal != null)
            OnSymmetricKeyReveal.Invoke(this, new SymmetricKeyRevealEventArgs()
            {
                SymmetricKey = symmetricKey,
                GigId = gidId,
                ReplierCertificateId = replierCertificateId,
            });;
    }

    public event PreimageRevealEventHandler OnPreimageReveal;

    public void FireOnPreimageReveal(string paymentHash , string preimage)
    {
        if (OnPreimageReveal != null)
            OnPreimageReveal.Invoke(this, new PreimageRevealEventArgs()
            {
                PaymentHash = paymentHash,
                Preimage = preimage,
            }); ;
    }

    private TimeSpan invoicePaymentTimeout;
    public TimeSpan disputeTimeout;
    private long priceAmountForSettlement;
    public GigLNDWalletAPIClient.swaggerClient lndWalletClient;
    private InvoiceStateUpdatesMonitor _invoiceStateUpdatesMonitor;
    private Guid walletTokenGuid;
    public ThreadLocal<SettlerContext> settlerContext;
    private IScheduler scheduler;
    private ISettlerSelector settlerSelector;

    public Settler(Uri serviceUri, ISettlerSelector settlerSelector, ECPrivKey settlerPrivateKey, long priceAmountForSettlement, TimeSpan invoicePaymentTimeout, TimeSpan disputeTimeout) : base(serviceUri, settlerPrivateKey)
    {
        this.priceAmountForSettlement = priceAmountForSettlement;
        this.invoicePaymentTimeout = invoicePaymentTimeout;
        this.disputeTimeout = disputeTimeout;
        this.settlerSelector = settlerSelector;
    }

    public HttpMessageHandler HttpMessageHandler;

    public async Task InitAsync(GigLNDWalletAPIClient.swaggerClient lndWalletClient, string connectionString, HttpMessageHandler? httpMessageHandler = null, bool deleteDb = false)
    {
        this.lndWalletClient = lndWalletClient;

#if DEBUG
        await Task.Delay(5000);
#endif
        this.walletTokenGuid = WalletAPIResult.Get<Guid>(await lndWalletClient.GetTokenAsync(this.CaXOnlyPublicKey.AsHex()));

        settlerContext = new ThreadLocal<SettlerContext>(() => new SettlerContext(connectionString));
        if (deleteDb)
            settlerContext.Value.Database.EnsureDeleted();
        settlerContext.Value.Database.EnsureCreated();

        this.HttpMessageHandler = httpMessageHandler;

        _invoiceStateUpdatesMonitor = new InvoiceStateUpdatesMonitor(this);

        scheduler = await new StdSchedulerFactory().GetScheduler();
        await scheduler.Start();
        scheduler.Context.Put("me", this);
    }

    public async Task StartAsync()
    {
        await _invoiceStateUpdatesMonitor.StartAsync();
    }

    public void Stop()
    {
        _invoiceStateUpdatesMonitor.Stop();
    }

    public string MakeAuthToken()
    {
        return Crypto.MakeSignedTimedToken(this._CaPrivateKey, DateTime.UtcNow, this.walletTokenGuid);
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
            throw new InvalidAuthTokenException();

        var tk = (from token in settlerContext.Value.Tokens where token.PublicKey == timedToken.Value.PublicKey && token.TokenId == timedToken.Value.Guid select token).FirstOrDefault();
        if (tk == null)
            throw new InvalidAuthTokenException();

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

    private Certificate<T> IssueCertificate<T>(string kind, Guid id, string pubkey, string[] properties, T data)
    {
        var props = (from u in settlerContext.Value.UserProperties where u.PublicKey == pubkey && !u.IsRevoked && u.ValidTill >= DateTime.UtcNow && properties.Contains(u.Name) select u).ToArray();
        var hasprops = new HashSet<string>(properties);
        if (!hasprops.SetEquals((from p in props select p.Name)))
            throw new PropertyNotGrantedException();
        var minDate = (from p in props select p.ValidTill).Min();
        var prp = (from p in props select p.Name).ToArray();
        var cert = base.IssueCertificate<T>(kind, id, prp, minDate, DateTime.UtcNow, data);
        var certProps = (from p in props select new CertificateProperty() { Kind = kind, CertificateId = cert.Id , PropertyId = p.PropertyId }).ToArray();
        settlerContext.Value.AddObjectRange(certProps);
        settlerContext.Value.AddObject(new UserCertificate() { Kind = kind, PublicKey = pubkey, CertificateId = cert.Id, IsRevoked = false });
        return cert;
    }

    public Guid[] ListCertificates(string pubkey)
    {
        return (from cert in settlerContext.Value.UserCertificates where cert.PublicKey == pubkey && !cert.IsRevoked select cert.CertificateId).ToArray();
    }

    public bool IsCertificateRevoked(Guid certid)
    {
        return (from c in settlerContext.Value.UserCertificates where c.CertificateId == certid && c.IsRevoked select c).FirstOrDefault() != null;
    }

    public string GenerateReplyPaymentPreimage(string pubkey, Guid tid, string replierpubkey)
    {
        var preimage = Crypto.GenerateRandomPreimage();
        var paymentHash = Crypto.ComputePaymentHash(preimage).AsHex();

        settlerContext.Value.AddObject(new InvoicePreimage() { PaymentHash = paymentHash, Preimage = preimage.AsHex(), SignedRequestPayloadId = tid, ReplierPublicKey= replierpubkey, PublicKey = pubkey, IsRevealed = false });
        return paymentHash;
    }

    public string GenerateRelatedPreimage(string pubkey, string paymentHash)
    {
        var preimage = Crypto.GenerateRandomPreimage();
        var newPaymentHash = Crypto.ComputePaymentHash(preimage).AsHex();

        var pix = (from pi in settlerContext.Value.Preimages where pi.PaymentHash == paymentHash select pi).FirstOrDefault();
        if (pix != null)
        {
            settlerContext.Value.AddObject(new InvoicePreimage() { PaymentHash = newPaymentHash, Preimage = preimage.AsHex(), SignedRequestPayloadId = pix.SignedRequestPayloadId, ReplierPublicKey=pix.ReplierPublicKey, PublicKey = pubkey, IsRevealed = false });
        }
        return newPaymentHash;
    }

    public bool ValidateRelatedPaymentHashes(string pubkey, string paymentHash1, string paymentHash2)
    {
        var pix1 = (from pi in settlerContext.Value.Preimages where pi.PaymentHash == paymentHash1 select pi).FirstOrDefault();
        if (pix1 == null)
            return false;
        var pix2 = (from pi in settlerContext.Value.Preimages where pi.PaymentHash == paymentHash2 select pi).FirstOrDefault();
        if (pix2 == null)
            return false;
        return pix1.SignedRequestPayloadId == pix2.SignedRequestPayloadId;
    }

    public string RevealPreimage(string pubkey, string paymentHash)
    {
        var preimage = (from pi in settlerContext.Value.Preimages where pi.PublicKey == pubkey && pi.PaymentHash == paymentHash && pi.IsRevealed select pi).FirstOrDefault();
        if (preimage == null)
            return "";
        else
            return preimage.Preimage;
    }

    public string RevealSymmetricKey(Guid sendercertificateid, Guid tid, Guid repliercertificateid)
    {
        var symkey = (from g in settlerContext.Value.Gigs where g.SenderCertificateId == sendercertificateid && g.ReplierCertificateId == repliercertificateid && g.SignedRequestPayloadId == tid && g.Status == GigStatus.Accepted select g).FirstOrDefault();
        if (symkey == null)
            return "";
        else
            return symkey.SymmetricKey;
    }

    public async Task<SettlementTrust> GenerateSettlementTrustAsync(string replierpubkey, string[] replierproperties, byte[] message, string replyInvoice, Certificate<RequestPayloadValue> signedRequestPayload)
    {
        var decodedInv = WalletAPIResult.Get<PayReq>(await lndWalletClient.DecodeInvoiceAsync(MakeAuthToken(), replyInvoice));
        var invPaymentHash = decodedInv.PaymentHash;
        if ((from pi in settlerContext.Value.Preimages where pi.SignedRequestPayloadId == signedRequestPayload.Id && pi.PaymentHash == invPaymentHash select pi).FirstOrDefault() == null)
            throw new UnknownPreimageException();

        byte[] key = Crypto.GenerateSymmetricKey();
        byte[] encryptedReplyMessage = Crypto.SymmetricEncrypt(key, message);

        var replyPayload = this.IssueCertificate<ReplyPayloadValue>(
            "Reply",
            Guid.NewGuid(),
            replierpubkey,
            replierproperties,
            new ReplyPayloadValue
            {
                SignedRequestPayload = signedRequestPayload,
                EncryptedReplyMessage = encryptedReplyMessage,
                ReplyInvoice = replyInvoice,
                Timestamp = DateTime.UtcNow,
            }
        );

        var networkInvoicePaymentHash = GenerateReplyPaymentPreimage(this.CaXOnlyPublicKey.AsHex(), signedRequestPayload.Id, replierpubkey);
        var networkInvoice = WalletAPIResult.Get<InvoiceRet>(await lndWalletClient.AddHodlInvoiceAsync(
             MakeAuthToken(), priceAmountForSettlement, networkInvoicePaymentHash, "", (long)invoicePaymentTimeout.TotalSeconds));



        settlerContext.Value.AddObject(new Gig()
        {
            SenderCertificateId = signedRequestPayload.Id,
            ReplierCertificateId = replyPayload.Id,
            SignedRequestPayloadId = signedRequestPayload.Id,
            SymmetricKey = key.AsHex(),
            Status = GigStatus.Open,
            SubStatus = GigSubStatus.None,
            NetworkPaymentHash = networkInvoice.PaymentHash,
            PaymentHash = decodedInv.PaymentHash,
            DisputeDeadline = DateTime.MaxValue
        });

        var encryptedReplyPayload = Convert.FromBase64String(SettlerAPIResult.Get<string>(await settlerSelector.GetSettlerClient(signedRequestPayload.ServiceUri)
            .EncryptObjectForCertificateIdAsync(signedRequestPayload.Id.ToString(),
                                                Convert.ToBase64String(Crypto.SerializeObject(replyPayload)))));


        byte[] hashOfEncryptedReplyPayload = Crypto.ComputeSha256(new List<byte[]> { encryptedReplyPayload});

        SettlementPromise signedSettlementPromise = new SettlementPromise()
        {
            ServiceUri = this.ServiceUri,
            RequestersServiceUri = signedRequestPayload.ServiceUri,
            HashOfEncryptedReplyPayload = hashOfEncryptedReplyPayload,
            ReplyPaymentAmount = decodedInv.NumSatoshis,
            NetworkPaymentHash = networkInvoicePaymentHash.AsBytes(),
        };
        signedSettlementPromise.Sign(_CaPrivateKey);

        await _invoiceStateUpdatesMonitor.MonitorInvoicesAsync(networkInvoicePaymentHash, decodedInv.PaymentHash);

        return new SettlementTrust()
        {
            SettlementPromise = signedSettlementPromise,
            NetworkInvoice = networkInvoice.PaymentRequest,
            EncryptedReplyPayload = encryptedReplyPayload
        };
    }

    public byte[] EncryptObjectForCertificateId(byte[] bytes, Guid certificateId)
    {
        var pubkey = (from cert in settlerContext.Value.UserCertificates where cert.CertificateId == certificateId && !cert.IsRevoked select cert.PublicKey).FirstOrDefault();
        if (pubkey == null)
            return null;
        return Crypto.EncryptObject(Crypto.DeserializeObject<Certificate<ReplyPayloadValue>>(bytes), pubkey.AsECXOnlyPubKey(), this._CaPrivateKey);
    }

    public BroadcastTopicResponse GenerateRequestPayload(string senderspubkey, string[] sendersproperties, byte[] topic)
    {
        var guid = Guid.NewGuid();

        var cert1 = this.IssueCertificate<RequestPayloadValue>(
            "Request",
            guid,
            senderspubkey,
            sendersproperties,
            new RequestPayloadValue
            {
                Topic = topic,
                Timestamp = DateTime.UtcNow,
            }
        );
        var cert2 = this.IssueCertificate<CancelRequestPayloadValue>(
            "Cancel",
            guid,
            senderspubkey,
            sendersproperties,
            new CancelRequestPayloadValue
            {
                Timestamp = DateTime.UtcNow,
            }
        );
        return new BroadcastTopicResponse { SignedRequestPayload = cert1, SignedCancelRequestPayload = cert2 };
    }

    public async Task ManageDisputeAsync(Guid tid, Guid repliercertificateId, bool open)
    {
        var gig = (from g in settlerContext.Value.Gigs where g.SignedRequestPayloadId == tid && g.ReplierCertificateId == repliercertificateId && g.Status == GigStatus.Accepted select g).FirstOrDefault();
        if (gig != null)
        {
            if (open)
                await CancelGigAsync(gig);
            gig.Status = open ? GigStatus.Disuputed : GigStatus.Accepted;
            settlerContext.Value.SaveObject(gig);
            if (!open)
                await ScheduleGigAsync(gig);
        }
    }


    class GigAcceptedJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            var me = (Settler)context.Scheduler.Context.Get("me");
            var gigid = context.JobDetail.JobDataMap.GetGuid("GigId");
            var gig = (from g in me.settlerContext.Value.Gigs
                       where g.SignedRequestPayloadId == gigid
                       select g).FirstOrDefault();
            if (gig != null)
                await me.SettleGigAsync(gig);
        }
    }

    public async Task SettleGigAsync(Gig gig)
    {
        var replierpubkey = (from cert in this.settlerContext.Value.UserCertificates where cert.CertificateId==gig.ReplierCertificateId && !cert.IsRevoked select cert.PublicKey).FirstOrDefault();
        if (replierpubkey == null)
            throw new UnknownPreimageException();
        var preims = (from pi in this.settlerContext.Value.Preimages where pi.ReplierPublicKey == replierpubkey && pi.SignedRequestPayloadId == gig.SignedRequestPayloadId select pi).ToList();
        foreach (var pi in preims)
            pi.IsRevealed = true;
        this.settlerContext.Value.SaveObjectRange(preims);
        foreach (var pi in preims)
            this.FireOnPreimageReveal(pi.PaymentHash, pi.Preimage);

        gig.Status = GigStatus.Completed;
        gig.SubStatus = GigSubStatus.None;
        this.settlerContext.Value.SaveObject(gig);
        var settletPi = (from pi in preims where pi.PublicKey == this.CaXOnlyPublicKey.AsHex() select pi).FirstOrDefault();
        if (settletPi == null)
            throw new UnknownPreimageException();
        WalletAPIResult.Check(await this.lndWalletClient.SettleInvoiceAsync(this.MakeAuthToken(), settletPi.Preimage)); // settle settlers network invoice
    }

    public async Task ScheduleGigAsync(Gig gig)
    {
        IJobDetail job = JobBuilder.Create<GigAcceptedJob>().UsingJobData("GigId", gig.SignedRequestPayloadId).WithIdentity(gig.SignedRequestPayloadId.ToString()).Build();
        ITrigger trigger = TriggerBuilder.Create().StartAt(gig.DisputeDeadline).Build();
        await scheduler.ScheduleJob(job, trigger);
    }

    public async Task CancelGigAsync(Gig gig)
    {
        await scheduler.Interrupt(new JobKey(gig.SignedRequestPayloadId.ToString()));
        await scheduler.DeleteJob(new JobKey(gig.SignedRequestPayloadId.ToString()));
    }

}

