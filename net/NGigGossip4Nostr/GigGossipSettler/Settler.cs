using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Xml.Linq;
using CryptoToolkit;
using GigLNDWalletAPIClient;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using NBitcoin;
using NBitcoin.RPC;
using NBitcoin.Secp256k1;
using Newtonsoft.Json.Linq;
using NGigGossip4Nostr;
using Quartz;
using Quartz.Impl;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

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
    public required string ReplierPublicKey { get; set; }
    public required string SymmetricKey { get; set; }
}
public delegate void SymmetricKeyRevealEventHandler(object sender, SymmetricKeyRevealEventArgs e);

public class Settler : CertificationAuthority
{
    public event SymmetricKeyRevealEventHandler OnSymmetricKeyReveal;

    public void FireOnSymmetricKeyReveal(Guid gidId, string replierPublicKey, string symmetricKey)
    {
        if (OnSymmetricKeyReveal != null)
            OnSymmetricKeyReveal.Invoke(this, new SymmetricKeyRevealEventArgs()
            {
                SymmetricKey = symmetricKey,
                GigId = gidId,
                ReplierPublicKey = replierPublicKey,
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
    private TimeSpan disputeTimeout;
    private long priceAmountForSettlement;
    private swaggerClient lndWalletClient;
    private Guid walletTokenGuid;
    private ThreadLocal<SettlerContext> settlerContext;
    private IScheduler scheduler;
    private InvoiceStateUpdatesClient invoiceStateUpdatesClient;

    public Settler(Uri serviceUri, ECPrivKey settlerPrivateKey, long priceAmountForSettlement, TimeSpan invoicePaymentTimeout, TimeSpan disputeTimeout) : base(serviceUri, settlerPrivateKey)
    {
        this.priceAmountForSettlement = priceAmountForSettlement;
        this.invoicePaymentTimeout = invoicePaymentTimeout;
        this.disputeTimeout = disputeTimeout;
    }

    public async Task InitAsync(swaggerClient lndWalletClient, string connectionString, bool deleteDb = false)
    {
        this.lndWalletClient = lndWalletClient;
        this.walletTokenGuid = await lndWalletClient.GetTokenAsync(this.CaXOnlyPublicKey.AsHex());
        settlerContext = new ThreadLocal<SettlerContext>(() => new SettlerContext(connectionString));
        if (deleteDb)
            settlerContext.Value.Database.EnsureDeleted();
        settlerContext.Value.Database.EnsureCreated();

        scheduler = await new StdSchedulerFactory().GetScheduler();
        await scheduler.Start();
        scheduler.Context.Put("me", this);
    }

    public string MakeAuthToken()
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
        var prp = (from p in props select p.Name).ToArray();
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

    public bool ValidateRelatedPaymentHashes(string pubkey, string paymentHash1, string paymentHash2)
    {
        var pix1 = (from pi in settlerContext.Value.Preimages where pi.PaymentHash == paymentHash1 select pi).FirstOrDefault();
        if (pix1 == null)
            return false;
        var pix2 = (from pi in settlerContext.Value.Preimages where pi.PaymentHash == paymentHash2 select pi).FirstOrDefault();
        if (pix2 == null)
            return false;
        return pix1.GigId == pix2.GigId;
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
            SubStatus = GigSubStatus.None,
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

        var tok = MakeAuthToken();
        await invoiceStateUpdatesClient.MonitorAsync(tok, networkInvoicePaymentHash);
        await invoiceStateUpdatesClient.MonitorAsync(tok, decodedInv.PaymentHash);

        return new SettlementTrust()
        {
            SettlementPromise = signedSettlementPromise,
            NetworkInvoice = networkInvoice.PaymentRequest,
            EncryptedReplyPayload = encryptedReplyPayload
        };
    }

    public async Task ManageDisputeAsync(Guid tid, string replierpubkey, bool open)
    {
        var gig = (from g in settlerContext.Value.Gigs where g.GigId == tid && g.ReplierPublicKey == replierpubkey && g.Status == GigStatus.Accepted select g).FirstOrDefault();
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

    Thread invoiceTrackerThread;
    public CancellationTokenSource CancellationTokenSource = new CancellationTokenSource();

    class GigAcceptedJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            var me = (Settler)context.Scheduler.Context.Get("me");
            var gigid = context.JobDetail.JobDataMap.GetGuid("GigId");
            var gig = (from g in me.settlerContext.Value.Gigs
                       where g.GigId == gigid
                       select g).FirstOrDefault();
            if (gig != null)
                await me.SettleGigAsync(gig);
        }
    }

    public async Task SettleGigAsync(Gig gig)
    {
        var preims = (from pi in this.settlerContext.Value.Preimages where pi.ReplierPublicKey == gig.ReplierPublicKey && pi.GigId == gig.GigId select pi).ToList();
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
            throw new SettlerException(SettlerErrorCode.UnknownPreimage);
        await this.lndWalletClient.SettleInvoiceAsync(this.MakeAuthToken(), settletPi.Preimage); // settle settlers network invoice
    }

    public async Task ScheduleGigAsync(Gig gig)
    {
        IJobDetail job = JobBuilder.Create<GigAcceptedJob>().UsingJobData("GigId", gig.GigId).WithIdentity(gig.GigId.ToString()).Build();
        ITrigger trigger = TriggerBuilder.Create().StartAt(gig.DisputeDeadline).Build();
        await scheduler.ScheduleJob(job, trigger);
    }

    public async Task CancelGigAsync(Gig gig)
    {
        await scheduler.Interrupt(new JobKey(gig.GigId.ToString()));
        await scheduler.DeleteJob(new JobKey(gig.GigId.ToString()));
    }

    public async Task StartAsync()
    {
        invoiceStateUpdatesClient = new InvoiceStateUpdatesClient(this.lndWalletClient);
        await invoiceStateUpdatesClient.ConnectAsync(MakeAuthToken());

        invoiceTrackerThread = new Thread( async () =>
        {
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
                            gig.SubStatus = GigSubStatus.None;
                            gig.DisputeDeadline = DateTime.Now+ this.disputeTimeout;
                            settlerContext.Value.SaveObject(gig);
                            await ScheduleGigAsync(gig);
                        }
                        else if (network_state == "Accepted")
                        {
                            gig.SubStatus = GigSubStatus.AcceptedByNetwork;
                            settlerContext.Value.SaveObject(gig);
                        }
                        else if (payment_state == "Accepted")
                        {
                            gig.SubStatus = GigSubStatus.AcceptedByReply;
                            settlerContext.Value.SaveObject(gig);
                        }
                        else if (network_state == "Cancelled" || payment_state == "Cancelled")
                        {
                            gig.Status = GigStatus.Cancelled;
                            gig.SubStatus = GigSubStatus.None;
                            settlerContext.Value.SaveObject(gig);
                        }
                    }
                    else if (gig.Status == GigStatus.Accepted)
                    {
                        FireOnSymmetricKeyReveal(gig.GigId, gig.ReplierPublicKey, gig.SymmetricKey);
                        if (DateTime.Now >= gig.DisputeDeadline)
                        {
                            await SettleGigAsync(gig);
                        }
                        else
                        {
                            await ScheduleGigAsync(gig);
                        }
                    }
                }

                while (true)
                {
                    try
                    {
                        await foreach (var invstateupd in invoiceStateUpdatesClient.StreamAsync(MakeAuthToken(), CancellationTokenSource.Token))
                        {
                            var invp = invstateupd.Split('|');
                            var payhash = invp[0];
                            var state = invp[1];

                            if (state == "Accepted")
                            {
                                var gig = (from g in settlerContext.Value.Gigs
                                           where (g.NetworkPaymentHash == payhash) || (g.PaymentHash == payhash)
                                           select g).FirstOrDefault();
                                if (gig != null)
                                {
                                    if (gig.SubStatus == GigSubStatus.None && gig.NetworkPaymentHash == payhash && gig.Status == GigStatus.Open)
                                    {
                                        gig.SubStatus = GigSubStatus.AcceptedByNetwork;
                                        settlerContext.Value.SaveObject(gig);
                                    }
                                    else if (gig.SubStatus == GigSubStatus.None && gig.PaymentHash == payhash && gig.Status == GigStatus.Open)
                                    {
                                        gig.SubStatus = GigSubStatus.AcceptedByReply;
                                        settlerContext.Value.SaveObject(gig);
                                    }
                                    else if ((gig.NetworkPaymentHash == payhash && gig.SubStatus == GigSubStatus.AcceptedByReply)
                                    || (gig.PaymentHash == payhash && gig.SubStatus == GigSubStatus.AcceptedByNetwork))
                                    {
                                        gig.Status = GigStatus.Accepted;
                                        gig.SubStatus = GigSubStatus.None;
                                        gig.DisputeDeadline = DateTime.Now + this.disputeTimeout;
                                        settlerContext.Value.SaveObject(gig);
                                        FireOnSymmetricKeyReveal(gig.GigId, gig.ReplierPublicKey, gig.SymmetricKey);
                                        await ScheduleGigAsync(gig);
                                    }
                                }
                            }
                            else if (state == "Cancelled")
                            {
                                var gig = (from g in settlerContext.Value.Gigs
                                           where (g.NetworkPaymentHash == payhash) || (g.PaymentHash == payhash)
                                           select g).FirstOrDefault();
                                if (gig != null)
                                {
                                    if (gig.Status == GigStatus.Accepted)
                                    {
                                        await CancelGigAsync(gig);
                                    }
                                    if (gig.Status != GigStatus.Cancelled)
                                    {
                                        gig.Status = GigStatus.Cancelled;
                                        gig.SubStatus = GigSubStatus.None;
                                        settlerContext.Value.SaveObject(gig);
                                    }
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        //stream closed
                        return;
                    }
                    catch (TimeoutException)
                    {
                        Trace.TraceWarning("Timeout on " + lndWalletClient.BaseUrl + "/invoicestateupdates, reconnecting");
                        //reconnect
                    }
                }
            }
        });
        invoiceTrackerThread.Start();

    }

    public void Stop()
    {
        CancellationTokenSource.Cancel();
        invoiceTrackerThread.Join();
    }
}

