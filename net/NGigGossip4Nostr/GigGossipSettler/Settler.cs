using CryptoToolkit;
using GigGossipFrames;
using GigGossipSettler.Exceptions;
using GigGossipSettlerAPIClient;
using GigLNDWalletAPIClient;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using NBitcoin.Secp256k1;
using Newtonsoft.Json;
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


public class GigStatusEventArgs : EventArgs
{
    public required Guid SignedRequestPayloadId { get; set; }
    public required Guid ReplierCertificateId { get; set; }
    public required GigStatus Status { get; set; }
    public required string Value { get; set; }
}
public delegate void GigStatusEventHandler(object sender, GigStatusEventArgs e);

public class Settler : CertificationAuthority
{
    public event PreimageRevealEventHandler OnPreimageReveal;

    public void FireOnPreimageReveal(string paymentHash, string preimage)
    {
        if (OnPreimageReveal != null)
            OnPreimageReveal.Invoke(this, new PreimageRevealEventArgs()
            {
                PaymentHash = paymentHash,
                Preimage = preimage,
            }); ;
    }

    public event GigStatusEventHandler OnGigStatus;

    public void FireOnGigStatus(Guid signedRequestPayloadId, Guid replierCertificateId, GigStatus status, string value = "")
    {
        if (OnGigStatus != null)
            OnGigStatus.Invoke(this, new GigStatusEventArgs()
            {
                Status = status,
                SignedRequestPayloadId = signedRequestPayloadId,
                ReplierCertificateId = replierCertificateId,
                Value = value,
            });
    }

    private TimeSpan invoicePaymentTimeout;
    public TimeSpan disputeTimeout;
    private long priceAmountForSettlement;
    public IWalletAPI lndWalletClient;
    private InvoiceStateUpdatesMonitor _invoiceStateUpdatesMonitor;
    private Guid walletTokenGuid;
    public ThreadLocal<SettlerContext> settlerContext;
    private IScheduler scheduler;
    private ISettlerSelector settlerSelector;

    private CancellationTokenSource CancellationTokenSource = new();

    public IRetryPolicy RetryPolicy;

    public Settler(Uri serviceUri, ISettlerSelector settlerSelector, ECPrivKey settlerPrivateKey, long priceAmountForSettlement, TimeSpan invoicePaymentTimeout, TimeSpan disputeTimeout, IRetryPolicy retryPolicy) : base(serviceUri, settlerPrivateKey)
    {
        this.priceAmountForSettlement = priceAmountForSettlement;
        this.invoicePaymentTimeout = invoicePaymentTimeout;
        this.disputeTimeout = disputeTimeout;
        this.settlerSelector = settlerSelector;
        this.RetryPolicy = retryPolicy;
    }

    public async Task InitAsync(IWalletAPI lndWalletClient, DBProvider provider, string connectionString, string ownerPublicKey)
    {
        this.lndWalletClient = lndWalletClient;

#if DEBUG
        await Task.Delay(5000);
#endif
        this.walletTokenGuid = WalletAPIResult.Get<Guid>(await lndWalletClient.GetTokenAsync(this.CaXOnlyPublicKey.AsHex(), CancellationTokenSource.Token));

        settlerContext = new ThreadLocal<SettlerContext>(() => new SettlerContext(provider, connectionString));
        settlerContext.Value.Database.EnsureCreated();
        EnsureOwnerAccessRights(ownerPublicKey);

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

    private void EnsureOwnerAccessRights(string pubkey)
    {
        using var TX = settlerContext.Value.Database.BeginTransaction();

        var ar = (from a in settlerContext.Value.UserAccessRights where a.AccessRights == AccessRights.Owner select a).FirstOrDefault();
        if (ar != null)
        {
            if (ar.PublicKey != pubkey)
            {
                ar.PublicKey = pubkey;
                settlerContext.Value
                    .UPDATE(ar)
                    .SAVE();
            }
        }
        else
        {
            settlerContext.Value
                .INSERT(new UserAccessRights() { PublicKey = pubkey, AccessRights = AccessRights.Owner })
                .SAVE();
        }

        TX.Commit();
    }

    public void GrantAccessRights(string pubkey, AccessRights accessRights)
    {
        using var TX = settlerContext.Value.Database.BeginTransaction();

        var ar = (from a in settlerContext.Value.UserAccessRights where a.PublicKey == pubkey select a).FirstOrDefault();
        if (ar == null)
        {
            ar = new UserAccessRights() { PublicKey = pubkey, AccessRights = accessRights };
            settlerContext.Value
                .INSERT(ar)
                .SAVE();
        }
        else
        {
            ar.AccessRights |= accessRights;
            settlerContext.Value
                .UPDATE(ar)
                .SAVE();
        }

        TX.Commit();
    }

    public void RevokeAccessRights(string pubkey, AccessRights accessRights)
    {
        var query = (from a in settlerContext.Value.UserAccessRights where a.PublicKey == pubkey select a);
        query.ExecuteUpdate((x) => x.SetProperty(a => a.AccessRights, a => a.AccessRights & ~accessRights));
    }

    public AccessRights GetAccessRights(string pubkey)
    {
        var ar = (from a in settlerContext.Value.UserAccessRights where a.PublicKey == pubkey select a).FirstOrDefault();
        return (ar == null) ? AccessRights.Anonymous : ar.AccessRights;
    }

    public bool HasAccessRights(string pubkey, AccessRights accessRights)
    {
        if (accessRights == AccessRights.Anonymous)
            return true;
        var ar = (from a in settlerContext.Value.UserAccessRights where a.PublicKey == pubkey select a).FirstOrDefault();
        if (ar == null)
            return false;
        return (ar.AccessRights & accessRights) == accessRights;
    }

    public Guid GetTokenGuid(string pubkey)
    {
        var t = (from token in settlerContext.Value.Tokens where pubkey == token.PublicKey select token).FirstOrDefault();
        if (t == null)
        {
            t = new Token() { TokenId = Guid.NewGuid(), PublicKey = pubkey };
            settlerContext.Value
                .INSERT(t)
                .SAVE();
        }
        return t.TokenId;
    }

    string GenerateRandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        var randomString = new string(Enumerable.Repeat(chars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray());
        return randomString;
    }

    public string IssueNewAccessCode(int length, bool singleUse, long validTillMin, string Memo)
    {
        for (int i = 0; i < 100; i++)
        {
            var code = GenerateRandomString(length);
            {
                using var TX = settlerContext.Value.Database.BeginTransaction();
                if (settlerContext.Value.AccessCodes.Where(a => a.Code == code).Count() == 0)
                {
                    settlerContext.Value
                        .INSERT(new AccessCode()
                        {
                            Code = code,
                            SingleUse = singleUse,
                            ValidTill = DateTime.UtcNow.AddMinutes(validTillMin),
                            Memo = Memo,
                            UseCount = 0,
                            IsRevoked = false,
                        })
                        .SAVE();

                    TX.Commit();
                    return code;
                }
            }
        };
        throw new Exception("Failed to generate unique access code");
    }

    public bool ValidateAccessCode(string code)
    {
        using var TX = settlerContext.Value.Database.BeginTransaction();

        var ac = (from a in settlerContext.Value.AccessCodes where a.Code == code && !a.IsRevoked && a.ValidTill >= DateTime.UtcNow select a).FirstOrDefault();
        if (ac == null)
            return false;
        if (ac.SingleUse)
            ac.IsRevoked = true;
        ac.UseCount++;
        settlerContext.Value
            .UPDATE(ac)
            .SAVE();
        TX.Commit();
        return true;
    }

    public void RevokeAccessCode(string code)
    {
        var query = (from a in settlerContext.Value.AccessCodes where a.Code == code select a);
        query.ExecuteUpdate((x) => x.SetProperty(a => a.IsRevoked, true));
    }

    public string GetMemoFromAccessCode(string code)
    {
        var ac = (from a in settlerContext.Value.AccessCodes where a.Code == code && !a.IsRevoked && a.ValidTill >= DateTime.UtcNow select a).FirstOrDefault();
        if (ac == null)
            return "";
        return ac.Memo;
    }

    private string ValidateAuthToken(string authTokenBase64)
    {
        var timedToken = CryptoToolkit.Crypto.VerifySignedTimedToken(authTokenBase64, 120.0);
        if (timedToken == null)
            throw new InvalidAuthTokenException();

        var tk = (from token in settlerContext.Value.Tokens where token.PublicKey == timedToken.PublicKey && token.TokenId == timedToken.Token.AsGuid() select token).FirstOrDefault();
        if (tk == null)
            throw new InvalidAuthTokenException();

        return tk.PublicKey;
    }

    public string ValidateAuthToken(string authTokenBase64, AccessRights accessRights)
    {
        var pubkey = ValidateAuthToken(authTokenBase64);
        if (!HasAccessRights(pubkey, accessRights))
            throw new AccessDeniedException();
        return pubkey;
    }

    public void SaveUserTraceProperty(string pubkey, string name, byte[] value)
    {
        using var TX = settlerContext.Value.Database.BeginTransaction();
        var query = (from u in settlerContext.Value.UserTraceProperties
                     where u.Name == name && u.PublicKey == pubkey
                     select u);
        if (query.ExecuteUpdate(i => i
                .SetProperty(a => a.Value, a => value))
             == 0)
        {
            settlerContext.Value
                .INSERT(new UserTraceProperty
                {
                    PublicKey = pubkey,
                    Name = name,
                    Value = value,
                })
                .SAVE();
        }
        TX.Commit();
    }

    public void GiveUserProperty(string pubkey, string name, byte[] value, byte[] secret, DateTime validTill)
    {
        using var TX = settlerContext.Value.Database.BeginTransaction();

        var query = (from u in settlerContext.Value.UserProperties
                     where u.Name == name && u.PublicKey == pubkey
                     select u);
        if (query.ExecuteUpdate(i => i
                .SetProperty(a => a.Value, a => value)
                .SetProperty(a => a.Secret, a => secret)
                .SetProperty(a => a.IsRevoked, a => false)
                .SetProperty(a => a.ValidTill, a => validTill))
             == 0)
        {
            settlerContext.Value
                .INSERT(new UserProperty()
                {
                    PropertyId = Guid.NewGuid(),
                    IsRevoked = false,
                    Name = name,
                    PublicKey = pubkey,
                    ValidTill = validTill,
                    Value = value,
                    Secret = secret,
                })
                .SAVE();
        }

        TX.Commit();
    }

    public UserProperty? GetUserProperty(string pubkey, string name)
    {
        return (from u in settlerContext.Value.UserProperties where u.Name == name && u.PublicKey == pubkey && u.IsRevoked == false select u).FirstOrDefault();
    }

    public void RevokeUserProperty(string pubkey, string name)
    {
        var query = (from u in settlerContext.Value.UserProperties where u.Name == name && u.PublicKey == pubkey && u.IsRevoked == false select u);
        query.ExecuteUpdate((x) => x.SetProperty(a => a.IsRevoked, true));
    }

    private Certificate IssueCertificate<T>(string kind, Guid id, string pubkey, string[] properties, T data) where T : Google.Protobuf.IMessage<T>
    {
        using var TX = settlerContext.Value.Database.BeginTransaction();

        var props = (from u in settlerContext.Value.UserProperties where u.PublicKey == pubkey && !u.IsRevoked && u.ValidTill >= DateTime.UtcNow && properties.Contains(u.Name) select u).ToArray();
        var tracs = (from u in settlerContext.Value.UserTraceProperties where u.PublicKey == pubkey && properties.Contains(u.Name) select u).ToArray();
        var prp = (from p in props select KeyValuePair.Create(p.Name, p.Value)).ToList();
        var trp = (from p in tracs select KeyValuePair.Create(p.Name, p.Value)).ToList();
        prp.AddRange(trp);

        if (!new HashSet<string>(properties).IsSubsetOf(new HashSet<string>(from p in prp select p.Key)))
            throw new PropertyNotGrantedException();
        var minDate = (from p in props select p.ValidTill).Min();
        var cert = base.IssueCertificate<T>(kind, id, prp.ToDictionary(), minDate, DateTime.UtcNow, data);
        foreach (var p in props)
            settlerContext.Value.INSERT(
                new CertificateProperty() { Kind = kind, CertificateId = cert.Id.AsGuid(), PropertyId = p.PropertyId });

        settlerContext.Value
            .INSERT(new UserCertificate() { Kind = kind, PublicKey = pubkey, CertificateId = cert.Id.AsGuid(), IsRevoked = false })
            .SAVE();

        TX.Commit();
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

        settlerContext.Value
            .INSERT(new InvoicePreimage() { PaymentHash = paymentHash, Preimage = preimage.AsHex(), SignedRequestPayloadId = tid, ReplierPublicKey = replierpubkey, PublicKey = pubkey, IsRevealed = false })
            .SAVE();
        return paymentHash;
    }

    public string GenerateRelatedPreimage(string pubkey, string paymentHash)
    {
        using var TX = settlerContext.Value.Database.BeginTransaction();

        var pix = (from pi in settlerContext.Value.Preimages where pi.PaymentHash == paymentHash select pi).FirstOrDefault();
        if (pix == null)
            throw new NotFoundException();

        var preimage = Crypto.GenerateRandomPreimage();
        var newPaymentHash = Crypto.ComputePaymentHash(preimage).AsHex();

        settlerContext.Value
            .INSERT(new InvoicePreimage() { PaymentHash = newPaymentHash, Preimage = preimage.AsHex(), SignedRequestPayloadId = pix.SignedRequestPayloadId, ReplierPublicKey = pix.ReplierPublicKey, PublicKey = pubkey, IsRevealed = false })
            .SAVE();

        TX.Commit();
        return newPaymentHash;
    }

    public bool ValidateRelatedPaymentHashes(string paymentHash1, string paymentHash2)
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

    public string RevealSymmetricKey(string pubkey, Guid signedRequestPayloadId, Guid repliercertificateId)
    {
        var gig = (from g in settlerContext.Value.Gigs
                   where g.SignedRequestPayloadId == signedRequestPayloadId && g.ReplierCertificateId == repliercertificateId
                   select g).FirstOrDefault();

        if (gig == null)
            return "";
        else
            return gig.SymmetricKey;
    }

    public string GetGigStatus(Guid signedRequestPayloadId, Guid repliercertificateid)
    {
        var gig = (from g in settlerContext.Value.Gigs where g.ReplierCertificateId == repliercertificateid && g.SignedRequestPayloadId == signedRequestPayloadId select g).FirstOrDefault();
        if (gig == null)
            return "";
        else
            return gig.Status.ToString() + "|" + gig.SymmetricKey;
    }

    public async Task<SettlementTrust> GenerateSettlementTrustAsync(string replierpubkey, string[] replierproperties, byte[] message, string replyInvoice, Certificate signedRequestPayload)
    {
        using var TX = settlerContext.Value.Database.BeginTransaction();

        var decodedInv = WalletAPIResult.Get<PaymentRequestRecord>(await lndWalletClient.DecodeInvoiceAsync(MakeAuthToken(), replyInvoice, CancellationTokenSource.Token));
        var invPaymentHash = decodedInv.PaymentHash;
        if ((from pi in settlerContext.Value.Preimages where pi.SignedRequestPayloadId == signedRequestPayload.Id.AsGuid() && pi.PaymentHash == invPaymentHash select pi).FirstOrDefault() == null)
            throw new UnknownPreimageException();

        byte[] key = Crypto.GenerateSymmetricKey();
        byte[] encryptedReplyMessage = Crypto.SymmetricBytesEncrypt(key, message);

        var replyPayload = this.IssueCertificate<ReplyPayloadValue>(
            "Reply",
            Guid.NewGuid(),
            replierpubkey,
            replierproperties,
            new ReplyPayloadValue
            {
                SignedRequestPayload = signedRequestPayload,
                EncryptedReplyMessage = encryptedReplyMessage.AsByteString(),
                ReplyInvoice = replyInvoice,
                Timestamp = DateTime.UtcNow.AsUnixTimestamp(),
            }
        );

        var networkInvoicePaymentHash = GenerateReplyPaymentPreimage(this.CaXOnlyPublicKey.AsHex(), signedRequestPayload.Id.AsGuid(), replierpubkey);
        var networkInvoice = WalletAPIResult.Get<InvoiceRecord>(await lndWalletClient.AddHodlInvoiceAsync(
             MakeAuthToken(), priceAmountForSettlement, networkInvoicePaymentHash, "", (long)invoicePaymentTimeout.TotalSeconds, CancellationTokenSource.Token));



        settlerContext.Value
            .INSERT(new Gig()
            {
                ReplierCertificateId = replyPayload.Id.AsGuid(),
                SignedRequestPayloadId = signedRequestPayload.Id.AsGuid(),
                SymmetricKey = key.AsHex(),
                Status = GigStatus.Open,
                SubStatus = GigSubStatus.None,
                NetworkPaymentHash = networkInvoice.PaymentHash,
                PaymentHash = decodedInv.PaymentHash,
                DisputeDeadline = DateTime.MaxValue
            })
            .SAVE();

        var encryptedReplyPayload = Convert.FromBase64String(SettlerAPIResult.Get<string>(await settlerSelector.GetSettlerClient(new Uri(signedRequestPayload.CertificationAuthorityUri))
            .EncryptObjectForCertificateIdAsync(signedRequestPayload.Id.AsGuid(),
                                                new FileParameter(new MemoryStream(Crypto.BinarySerializeObject(replyPayload))), CancellationTokenSource.Token)));


        byte[] hashOfEncryptedReplyPayload = Crypto.ComputeSha256(new List<byte[]> { encryptedReplyPayload });

        SettlementPromise signedSettlementPromise = new SettlementPromise()
        {
            MySecurityCenterUri = this.ServiceUri.AbsoluteUri,
            TheirSecurityCenterUri = signedRequestPayload.CertificationAuthorityUri,
            HashOfEncryptedReplyPayload = hashOfEncryptedReplyPayload.AsByteString(),
            ReplyPaymentAmountSat = (ulong)decodedInv.Satoshis,
            NetworkPaymentHash = networkInvoicePaymentHash.AsBytes().AsByteString(),
        };
        signedSettlementPromise.Sign(_CaPrivateKey);

        await _invoiceStateUpdatesMonitor.MonitorInvoicesAsync(networkInvoicePaymentHash, decodedInv.PaymentHash);

        TX.Commit();

        return new SettlementTrust()
        {
            SettlementPromise = signedSettlementPromise,
            NetworkInvoice = networkInvoice.PaymentRequest,
            EncryptedReplyPayload = encryptedReplyPayload.AsByteString(),
            ReplierCertificateId = replyPayload.Id,
        };
    }

    public string GetPubkeyFromCertificateId(Guid certificateId)
    {
        var pubkey = (from cert in settlerContext.Value.UserCertificates where cert.CertificateId == certificateId && !cert.IsRevoked select cert.PublicKey).FirstOrDefault();
        if (pubkey == null)
            throw new NotFoundException();
        return pubkey;
    }

    public byte[] EncryptObjectForPubkey(string pubkey, byte[] bytes)
    {
        return Crypto.EncryptObject(Crypto.BinaryDeserializeObject<Certificate>(bytes)!, pubkey.AsECXOnlyPubKey(), this._CaPrivateKey);
    }

    public BroadcastTopicResponse GenerateRequestPayload(string senderspubkey, string[] sendersproperties, byte[] topic)
    {
        using var TX = settlerContext.Value.Database.BeginTransaction();

        var guid = Guid.NewGuid();

        var cert1 = this.IssueCertificate(
            "Request",
            guid,
            senderspubkey,
            sendersproperties,
            new RequestPayloadValue
            {
                Topic = topic.AsByteString(),
                Timestamp = DateTime.UtcNow.AsUnixTimestamp(),
            }
        );
        var cert2 = this.IssueCertificate(
            "Cancel",
            guid,
            senderspubkey,
            sendersproperties,
            new CancelRequestPayloadValue
            {
                Timestamp = DateTime.UtcNow.AsUnixTimestamp(),
            }
        );

        TX.Commit();
        return new BroadcastTopicResponse { SignedRequestPayload = cert1, SignedCancelRequestPayload = cert2 };
    }

    public async Task ManageDisputeAsync(Guid tid, Guid repliercertificateId, bool open)
    {
        using var TX = settlerContext.Value.Database.BeginTransaction();

        var gig = (from g in settlerContext.Value.Gigs where g.SignedRequestPayloadId == tid && g.ReplierCertificateId == repliercertificateId && g.Status == GigStatus.Accepted select g).FirstOrDefault();
        if (gig != null)
        {
            if (open)
                await DescheduleGigAsync(gig);
            gig.Status = open ? GigStatus.Disuputed : GigStatus.Accepted;
            settlerContext.Value
                .UPDATE(gig)
                .SAVE();
            if (!open)
                await ScheduleGigAsync(gig);
        }

        TX.Commit();
    }

    public async Task CancelGigAsync(Guid tid, Guid repliercertificateId)
    {
        using var TX = settlerContext.Value.Database.BeginTransaction();

        var gig = (from g in settlerContext.Value.Gigs
                   where g.SignedRequestPayloadId == tid && g.ReplierCertificateId == repliercertificateId && (g.Status == GigStatus.Open || g.Status != GigStatus.Accepted)
                   select g).FirstOrDefault();

        if (gig == null)
            throw new NotFoundException();

        if (gig.Status == GigStatus.Accepted)
        {
            await DescheduleGigAsync(gig);
            var status = WalletAPIResult.Status(await lndWalletClient.CancelInvoiceAsync(MakeAuthToken(), gig.NetworkPaymentHash, CancellationTokenSource.Token));
            if (status != GigLNDWalletAPIErrorCode.Ok)
                throw new InvoiceProblemException(status);
        }

        if (gig.Status != GigStatus.Cancelled)
        {
            gig.Status = GigStatus.Cancelled;
            gig.SubStatus = GigSubStatus.None;
            settlerContext.Value
                .UPDATE(gig)
                .SAVE();
            FireOnGigStatus(tid, repliercertificateId, GigStatus.Cancelled);
        }

        TX.Commit();
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
        using var TX = settlerContext.Value.Database.BeginTransaction();

        var replierpubkey = (from cert in this.settlerContext.Value.UserCertificates where cert.CertificateId == gig.ReplierCertificateId && !cert.IsRevoked select cert.PublicKey).FirstOrDefault();
        if (replierpubkey == null)
            throw new UnknownPreimageException();

        var preims = (from pi in this.settlerContext.Value.Preimages where pi.ReplierPublicKey == replierpubkey && pi.SignedRequestPayloadId == gig.SignedRequestPayloadId select pi).ToList();

        var settletPi = (from pi in preims where pi.PublicKey == this.CaXOnlyPublicKey.AsHex() select pi).FirstOrDefault();
        if (settletPi == null)
            throw new UnknownPreimageException();

        foreach (var pi in preims)
        {
            pi.IsRevealed = true;
            this.settlerContext.Value
                .UPDATE(pi);
        }

        gig.Status = GigStatus.Completed;
        gig.SubStatus = GigSubStatus.None;
        this.settlerContext.Value
            .UPDATE(gig)
            .SAVE();

        var status = WalletAPIResult.Status(await this.lndWalletClient.SettleInvoiceAsync(this.MakeAuthToken(), settletPi.Preimage, CancellationTokenSource.Token)); // settle settlers network invoice
        if (status != GigLNDWalletAPIErrorCode.Ok)
            throw new InvoiceProblemException(status);

        foreach (var pi in preims)
            this.FireOnPreimageReveal(pi.PaymentHash, pi.Preimage);

        FireOnGigStatus(gig.SignedRequestPayloadId, gig.ReplierCertificateId, GigStatus.Completed, gig.SymmetricKey);

        TX.Commit();
    }

    public async Task ScheduleGigAsync(Gig gig)
    {
        IJobDetail job = JobBuilder.Create<GigAcceptedJob>().
                                    UsingJobData("GigId", gig.SignedRequestPayloadId).
                                    WithIdentity(gig.SignedRequestPayloadId.ToString()).
                                    Build();
        ITrigger trigger = TriggerBuilder.Create().StartAt(gig.DisputeDeadline).Build();
        await scheduler.ScheduleJob(job, trigger);
    }

    public async Task DescheduleGigAsync(Gig gig)
    {
        await scheduler.Interrupt(new JobKey(gig.SignedRequestPayloadId.ToString()));
        await scheduler.DeleteJob(new JobKey(gig.SignedRequestPayloadId.ToString()));
    }

    public void DeletePersonalUserData(string pubkey)
    {
        using var TX = settlerContext.Value.Database.BeginTransaction();

        var certids = (from c in (from u in settlerContext.Value.UserCertificates where u.PublicKey == pubkey select u) select c.CertificateId).ToHashSet();
        if (certids.Count > 0)
            (from u in settlerContext.Value.CertificateProperties where certids.Contains(u.CertificateId) select u).ExecuteDelete();

        (from u in settlerContext.Value.UserCertificates where u.PublicKey == pubkey select u).ExecuteDelete();
        (from u in settlerContext.Value.UserProperties where u.PublicKey == pubkey select u).ExecuteDelete();
        (from u in settlerContext.Value.UserTraceProperties where u.PublicKey == pubkey select u).ExecuteDelete();


        TX.Commit();
    }
}

