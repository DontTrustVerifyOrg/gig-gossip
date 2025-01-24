
using Azure.Storage.Blobs;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using GigGossip;
using GigGossipSettler.Exceptions;
using GigGossipSettlerAPIClient;
using GigLNDWalletAPIClient;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using NBitcoin.Protocol;
using NBitcoin.Secp256k1;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Quartz;
using Quartz.Impl;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text;

namespace GigGossipSettler;


[Serializable]
public struct CaPricing
{
    public string CountryCode { get; set; }
    public string Currency { get; set; }
    public long ChargeMiliPercent { get; set; }
    public long ChargeFlatCents { get; set; }
}

[Serializable]
public class PreimageReveal
{
    public required string PaymentHash { get; set; }
    public required string Preimage { get; set; }
}

public class PreimageRevealEventArgs : EventArgs
{
    public required PreimageReveal PreimageReveal { get; set; }
}

public delegate void PreimageRevealEventHandler(object sender, PreimageRevealEventArgs e);


[Serializable]
public class GigStatusKey
{
    public required Guid JobRequestId { get; set; }
    public required Guid JobReplyId { get; set; }
    public required GigStatus Status { get; set; }
    public required string SymmetricKey { get; set; }
}

public class GigStatusEventArgs : EventArgs
{
    public required GigStatusKey GigStatusChanged { get; set; }
}

public delegate void GigStatusEventHandler(object sender, GigStatusEventArgs e);

public class Settler : CertificationAuthority
{
    public class LocaleStrings
    {
        public string NewRideRequestTitle { get; set; }
        public string NewRideRequestBody { get; set; }
    }

    public event PreimageRevealEventHandler OnPreimageReveal;

    public void FireOnPreimageReveal(string paymentHash, string preimage)
    {
        if (OnPreimageReveal != null)
        {
            Console.WriteLine("Firing OnPreimageReveal "+paymentHash);
            OnPreimageReveal.Invoke(this, new PreimageRevealEventArgs
            {
                PreimageReveal = new PreimageReveal
                {
                    PaymentHash = paymentHash,
                    Preimage = preimage,
                }
            });
        }
    }

    public event GigStatusEventHandler OnGigStatus;

    public void FireOnGigStatus(Guid signedRequestPayloadId, Guid replierCertificateId, GigStatus status, string value = "")
    {
        if (OnGigStatus != null)
        {
            Console.WriteLine("Firing OnGigStatus " + signedRequestPayloadId.ToString()+ " " + replierCertificateId.ToString()+" "+status.ToString()+" "+value);
            OnGigStatus.Invoke(this, new GigStatusEventArgs
            {
                GigStatusChanged = new GigStatusKey
                {
                    Status = status,
                    JobRequestId = signedRequestPayloadId,
                    JobReplyId = replierCertificateId,
                    SymmetricKey = value,
                }
            });
        }
    }

    private TimeSpan invoicePaymentTimeout;
    public TimeSpan disputeTimeout;
    public IWalletAPI lndWalletClient;
    private InvoiceStateUpdatesMonitor _invoiceStateUpdatesMonitor;
    private Guid walletTokenGuid;
    public ThreadLocal<SettlerContext> settlerContext;
    private IScheduler scheduler;
    private ISettlerSelector settlerSelector;
    private ScpSettings scpSettings;
    Dictionary<(string, string), CaPricing> pricings;

    private string adminPubkey;
    private CancellationTokenSource CancellationTokenSource = new();

    private string azureStorageConnectionString;
    private readonly string azureContainerName = "gohyper";

    public IRetryPolicy RetryPolicy;

    public Settler(Uri serviceUri, ISettlerSelector settlerSelector, ECPrivKey settlerPrivateKey, Dictionary<(string, string), CaPricing> pricings, TimeSpan invoicePaymentTimeout, TimeSpan disputeTimeout, IRetryPolicy retryPolicy, string firebaseAdminConfBase64, ScpSettings scpSettings) : base(serviceUri, settlerPrivateKey)
    {
        this.pricings = pricings;
        this.invoicePaymentTimeout = invoicePaymentTimeout;
        this.disputeTimeout = disputeTimeout;
        this.settlerSelector = settlerSelector;
        this.RetryPolicy = retryPolicy;
        this.scpSettings = scpSettings;
        FirebaseApp.Create(new AppOptions()
        {
            Credential = GoogleCredential.FromJson(Encoding.Default.GetString(Convert.FromBase64String(firebaseAdminConfBase64)))
        }); 
    }

    public async Task InitAsync(IWalletAPI lndWalletClient, DBProvider provider, string connectionString, string adminPubkey,string azureStorageConnectionString)
    {
        this.adminPubkey = adminPubkey;
        this.azureStorageConnectionString = azureStorageConnectionString;

        this.lndWalletClient = lndWalletClient;

#if DEBUG
        await Task.Delay(5000);
#endif
        this.walletTokenGuid = WalletAPIResult.Get<Guid>(await lndWalletClient.GetTokenAsync(this.CaXOnlyPublicKey.AsHex(), CancellationTokenSource.Token));


        settlerContext = new ThreadLocal<SettlerContext>(() => new SettlerContext(provider, connectionString));
        settlerContext.Value.Database.EnsureCreated();

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
        return AuthToken.Create(this._CaPrivateKey, DateTime.UtcNow, this.walletTokenGuid);
    }

    public bool HasAdminRights(string pubkey)
    {
        return pubkey == this.adminPubkey;
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
                using var TX = settlerContext.Value.BEGIN_TRANSACTION();
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
        using var TX = settlerContext.Value.BEGIN_TRANSACTION();

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
        var timedToken = AuthToken.Verify(authTokenBase64, 120.0);
        if (timedToken == null)
            throw new InvalidAuthTokenException();

        var tk = (from token in settlerContext.Value.Tokens where token.PublicKey == timedToken.Header.PublicKey.Value.ToArray().AsHex() && token.TokenId == timedToken.Header.TokenId.AsGuid() select token).FirstOrDefault();
        if (tk == null)
            throw new InvalidAuthTokenException();

        return tk.PublicKey;
    }

    public string ValidateAuthToken(string authTokenBase64, bool admin = false)
    {
        var pubkey = ValidateAuthToken(authTokenBase64);
        if (admin)
            if (!HasAdminRights(pubkey))
                throw new AccessDeniedException();
        return pubkey;
    }

    public void SaveUserTraceProperty(string pubkey, string name, byte[] value)
    {
        settlerContext.Value.UPDATE_OR_INSERT_AND_SAVE(
            new UserTraceProperty
            {
                PublicKey = pubkey,
                Name = name,
                Value = value,
            });
    }

    public void GiveUserProperty(string pubkey, string name, byte[] value, byte[] secret, DateTime validTill)
    {
        using var TX = settlerContext.Value.BEGIN_TRANSACTION();

        var up = (from u in settlerContext.Value.UserProperties
                  where u.Name == name && u.PublicKey == pubkey
                  select u).FirstOrDefault();
        if (up == null)
        {
            settlerContext.Value.INSERT(
            new UserProperty
            {
                PropertyId = Guid.NewGuid(),
                IsRevoked = false,
                Name = name,
                PublicKey = pubkey,
                ValidTill = validTill,
                Value = value,
                Secret = secret,
            }).SAVE();
        }
        else
        {
            up.ValidTill = validTill;
            up.Value = value;
            up.Secret = secret;
            up.IsRevoked = false;
            settlerContext.Value
                .UPDATE(up)
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

    private CertificateHeader CreateCertificateHeader(string kind, Guid id, string pubkey, string[] properties) 
    {
        var props = (from u in settlerContext.Value.UserProperties where u.PublicKey == pubkey && !u.IsRevoked && u.ValidTill >= DateTime.UtcNow && properties.Contains(u.Name) select u).ToArray();
        var tracs = (from u in settlerContext.Value.UserTraceProperties where u.PublicKey == pubkey && properties.Contains(u.Name) select u).ToArray();
        var prp = (from p in props select KeyValuePair.Create(p.Name, p.Value)).ToList();
        var trp = (from p in tracs select KeyValuePair.Create(p.Name, p.Value)).ToList();
        prp.AddRange(trp);

//        if (!new HashSet<string>(properties).IsSubsetOf(new HashSet<string>(from p in prp select p.Key)))
//            throw new PropertyNotGrantedException();
        var minDate = (from p in props select p.ValidTill).Min();
        var header = new CertificateHeader {
            AuthorityUri = this.ServiceUri.AsURI(),
            NotValidAfter = minDate.AsUnixTimestamp(),
            NotValidBefore = DateTime.UtcNow.AsUnixTimestamp(),
        };
        header.Properties.Add((from kv in prp
                               select new GigGossip.CertificateProperty
                               {
                                   Name = kv.Key,
                                   Value = kv.Value.AsByteString()
                               }).ToList());

        foreach (var p in props)
            settlerContext.Value.INSERT(
                new CertificateProperty() { Kind = kind, CertificateId = id, PropertyId = p.PropertyId })
                .SAVE();

        settlerContext.Value
            .INSERT(new UserCertificate() { Kind = kind, PublicKey = pubkey, CertificateId = id, IsRevoked = false })
            .SAVE();

        return header;
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
        using var TX = settlerContext.Value.BEGIN_TRANSACTION();

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

    public GigStatusKey GetGigStatus(Guid signedRequestPayloadId, Guid repliercertificateid)
    {
        var gig = (from g in settlerContext.Value.Gigs where g.ReplierCertificateId == repliercertificateid && g.SignedRequestPayloadId == signedRequestPayloadId select g).FirstOrDefault();
        if (gig == null)
            return new GigStatusKey { SymmetricKey = "", JobReplyId = signedRequestPayloadId, JobRequestId = repliercertificateid, Status = GigStatus.Cancelled };
        else
            return new GigStatusKey { SymmetricKey = gig.SymmetricKey, JobReplyId = signedRequestPayloadId, JobRequestId = repliercertificateid, Status = gig.Status };
    }

    public async Task<SettlementTrust> GenerateSettlementTrustAsync(string replierpubkey, string[] replierproperties, Reply reply, string replyInvoice, JobRequest signedRequestPayload)
    {
        var decodedInv = WalletAPIResult.Get<PaymentRequestRecord>(await lndWalletClient.DecodeInvoiceAsync(MakeAuthToken(), replyInvoice, CancellationTokenSource.Token));

        if(!this.pricings.ContainsKey((signedRequestPayload.Header.RideShare.Country.ToUpper(), decodedInv.Currency.ToUpper())))
            throw new SettlerException(Exceptions.SettlerErrorCode.NotSupportedCountryCurrencyPair);

        var priceing = this.pricings[(signedRequestPayload.Header.RideShare.Country.ToUpper(), decodedInv.Currency.ToUpper())];

        var invPaymentHash = decodedInv.PaymentHash;
        if ((from pi in settlerContext.Value.Preimages where pi.SignedRequestPayloadId == signedRequestPayload.Header.JobRequestId.AsGuid() && pi.PaymentHash == invPaymentHash select pi).FirstOrDefault() == null)
            throw new UnknownPreimageException();

        byte[] symmetrickey = Crypto.GenerateSymmetricKey();

        using var TX = settlerContext.Value.BEGIN_TRANSACTION();

        var guid = Guid.NewGuid();
        var certHeader = this.CreateCertificateHeader(
            "Reply",
            guid,
            replierpubkey,
            replierproperties);

        var jobReplyHeader = new JobReplyHeader
        {
            Header = certHeader,
            JobReplyId = guid.AsUUID(),
            EncryptedReply = reply.Encrypt(symmetrickey),
            JobPaymentRequest = new PaymentRequest { Value = replyInvoice },
            JobRequest = signedRequestPayload.Clone(),
            Timestamp = DateTime.UtcNow.AsUnixTimestamp(),
        };

        var replyPayload = new JobReply
        {
            Header = jobReplyHeader,
            Signature = this.Sign(jobReplyHeader),
        };

        var netinvamount = decodedInv.Currency == "BTC" ?
            (long)Math.Ceiling(((decimal)decodedInv.Amount + priceing.ChargeFlatCents) / ((decimal)(100000 - priceing.ChargeMiliPercent) / 100000)) - decodedInv.Amount
            : 0;

        var networkInvoicePaymentHash = GenerateReplyPaymentPreimage(this.CaXOnlyPublicKey.AsHex(), signedRequestPayload.Header.JobRequestId.AsGuid(), replierpubkey);
        var networkInvoice = WalletAPIResult.Get<InvoiceRecord>(await lndWalletClient.AddHodlInvoiceAsync(
             MakeAuthToken(), (long)netinvamount, networkInvoicePaymentHash, "", (long)invoicePaymentTimeout.TotalSeconds, CancellationTokenSource.Token));


        settlerContext.Value
            .INSERT(new Gig()
            {
                ReplierCertificateId = replyPayload.Header.JobReplyId.AsGuid(),
                SignedRequestPayloadId = signedRequestPayload.Header.JobRequestId.AsGuid(),
                SymmetricKey = symmetrickey.AsHex(),
                Status = GigStatus.Open,
                SubStatus = GigSubStatus.None,
                NetworkPaymentHash = networkInvoice.PaymentHash,
                PaymentHash = decodedInv.PaymentHash,
                DisputeDeadline = DateTime.MaxValue
            })
            .SAVE();

        var encryptedReplyPayload = Convert.FromBase64String(SettlerAPIResult.Get<string>(await settlerSelector.GetSettlerClient(signedRequestPayload.Header.Header.AuthorityUri.AsUri())
            .EncryptJobReplyForCertificateIdAsync(signedRequestPayload.Header.JobRequestId.AsGuid(),
                                                new FileParameter(new MemoryStream(Crypto.BinarySerializeObject(replyPayload))), CancellationTokenSource.Token)));

        byte[] hashOfEncryptedReplyPayload = Crypto.ComputeSha256(encryptedReplyPayload);

        SettlementPromiseHeader signedSettlementPromiseHeader = new SettlementPromiseHeader()
        {
            MySecurityCenterUri = this.ServiceUri.AsURI(),
            TheirSecurityCenterUri = signedRequestPayload.Header.Header.AuthorityUri.Clone(),
            HashOfEncryptedJobReply = new CryptographicHash { Value = hashOfEncryptedReplyPayload.AsByteString() },
            ReplyPaymentAmount = new Satoshis { Value = decodedInv.Currency=="BTC"? decodedInv.Amount:0 },
            NetworkPaymentHash = new PaymentHash { Value = networkInvoicePaymentHash.AsBytes().AsByteString() },
        };

        var signedSettlementPromise = new SettlementPromise
        {
            Header = signedSettlementPromiseHeader,
            Signature = signedSettlementPromiseHeader.Sign(_CaPrivateKey)
        };

        await _invoiceStateUpdatesMonitor.MonitorExternalInvoiceAsync(decodedInv.PaymentHash);

        TX.Commit();

        return new SettlementTrust()
        {
            SettlementPromise = signedSettlementPromise,
            NetworkPaymentRequest = new PaymentRequest { Value = networkInvoice.PaymentRequest },
            EncryptedJobReply = new EncryptedData { Value = encryptedReplyPayload.AsByteString() },
            JobReplyId = replyPayload.Header.JobReplyId,
        };
    }

    public string GetPubkeyFromCertificateId(Guid certificateId)
    {
        var pubkey = (from cert in settlerContext.Value.UserCertificates where cert.CertificateId == certificateId && !cert.IsRevoked select cert.PublicKey).FirstOrDefault();
        if (pubkey == null)
            throw new NotFoundException();
        return pubkey;
    }

    public EncryptedData EncryptJobReplyForPubkey(string pubkey, JobReply jobReply)
    {
        return jobReply.Encrypt(pubkey.AsECXOnlyPubKey(), this._CaPrivateKey);
    }

    public async Task<BroadcastRequest> GenerateRequestPayloadAsync(string senderspubkey, string[] sendersproperties, RideShareTopic topic)
    {
        using var TX = settlerContext.Value.BEGIN_TRANSACTION();

        var guid = Guid.NewGuid();
        var jobRequestHeader = new JobRequestHeader
        {
            Header = this.CreateCertificateHeader(
                    "Request",
                    guid,
                    senderspubkey,
                    sendersproperties),
            JobRequestId = guid.AsUUID(),
            Timestamp = DateTime.UtcNow.AsUnixTimestamp(),
            RideShare = topic
        };

        var cert1 = new JobRequest
        {
            Header = jobRequestHeader,
            Signature = this.Sign(jobRequestHeader),
        };

        var cancelJobRequestHeader = new CancelJobRequestHeader
        {
            Header = this.CreateCertificateHeader(
                    "Cancel",
                    guid,
                    senderspubkey,
                    sendersproperties),
            JobRequestId = guid.AsUUID(),
            Timestamp = DateTime.UtcNow.AsUnixTimestamp(),
        };

        var cert2 = new CancelJobRequest
        {
            Header = cancelJobRequestHeader,
            Signature = this.Sign(cancelJobRequestHeader),
        };

        TX.Commit();

        Task.Run(async () => await sendPushNotificationsAsync(topic.FromGeohash));

        return new BroadcastRequest { JobRequest = cert1, CancelJobRequest = cert2 };
    }

    private async Task sendPushNotificationsAsync(string geohash)
    {
        try
        {
            var appModes = (from g in settlerContext.Value.UserProperties where g.Name == "AppMode" select g).ToList().ToLookup(g => g.PublicKey).ToDictionary(g => g.Key, x => Encoding.Default.GetString(x.First().Secret));
            var langs = (from g in settlerContext.Value.UserProperties where g.Name == "Language" select g).ToList().ToLookup(g => g.PublicKey).ToDictionary(g => g.Key, x => Encoding.Default.GetString(x.First().Secret));
            var geohashes = (from g in settlerContext.Value.UserTraceProperties where g.Name == "Geohash" select g).ToList().ToLookup(g => g.PublicKey).ToDictionary(g => g.Key, x => Encoding.Default.GetString(x.First().Value));
            foreach (var pn in (from g in settlerContext.Value.UserProperties where g.Name == "PushNotificationsToken" select g).ToList())
            {
                try
                {
                    if (appModes.ContainsKey(pn.PublicKey) && appModes[pn.PublicKey] != "Rider")
                    {
                        if (geohashes.ContainsKey(pn.PublicKey) && GeoHashUtils.GeohashHaversineDistance(geohashes[pn.PublicKey], geohash) < 500)
                        {
                            var lang = langs.ContainsKey(pn.PublicKey) ? langs[pn.PublicKey] : "EN";
                            var token = Encoding.Default.GetString(pn.Secret);
                            var message = new FirebaseAdmin.Messaging.Message()
                            {
                                Notification = new Notification
                                {
                                    Title = Localize.GetStrings<LocaleStrings, Settler>(lang).NewRideRequestTitle,
                                    Body = Localize.GetStrings<LocaleStrings, Settler>(lang).NewRideRequestBody,
                                },
                                Token = token
                            };

                            var messaging = FirebaseMessaging.DefaultInstance;
                            try
                            {
                                var result = await messaging.SendAsync(message);
                            }
                            catch (FirebaseMessagingException)
                            {
                                Console.WriteLine("Cannot send PN - removing token");
                                settlerContext.Value
                                    .DELETE(pn)
                                    .SAVE();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(pn.PublicKey);
                    Console.WriteLine(ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    public async Task OpenCustomerDisputeAsync(string riderPublicKey, string driverPublicKey, string reason, Guid gigId, Guid repliercertificateId, string stripeClientSecret)
    {
        await ManageDisputeAsync(gigId, repliercertificateId, true);
        await CallCustomerDisputesAsync(riderPublicKey, driverPublicKey, reason, gigId, repliercertificateId, stripeClientSecret);
    }

    public async Task OpenDriverDisputeAsync(string riderPublicKey, string driverPublicKey, string reason, Guid gigId, Guid repliercertificateId)
    {
        await CallDriverDisputesAsync(riderPublicKey, driverPublicKey, reason, gigId, repliercertificateId);
    }

    public async Task OpenTicketAsync(string publicKey, string message)
    {
        await CallOpenTicketAsync(publicKey, message);
    }

    public async Task CallCustomerDisputesAsync(string riderPublicKey, string driverPublicKey, string reason, Guid gigId, Guid repliperCertificateId, string stripeClientSecret)
    {
        var client = new HttpClient();

        var requestData = new
        {
            riderPublicKey,
            driverPublicKey ,
            reason ,
            gigId ,
            repliperCertificateId,
            stripeClientSecret,
        };

        var requestContent = new StringContent(
            Newtonsoft.Json.JsonConvert.SerializeObject(requestData),
            System.Text.Encoding.UTF8,
            "application/json"
        );

        client.DefaultRequestHeaders.Add("X-Api-Key", scpSettings.ScpApiKey);


        HttpResponseMessage response = await client.PostAsync(scpSettings.ScpApiUri + "disputes", requestContent);
        response.EnsureSuccessStatusCode();
        string responseBody = await response.Content.ReadAsStringAsync();
    }


    public async Task CallDriverDisputesAsync(string riderPublicKey, string driverPublicKey, string reason, Guid gigId, Guid repliperCertificateId)
    {
        var client = new HttpClient();

        var requestData = new
        {
            riderPublicKey,
            driverPublicKey,
            reason,
            gigId,
            repliperCertificateId
        };

        var requestContent = new StringContent(
            Newtonsoft.Json.JsonConvert.SerializeObject(requestData),
            System.Text.Encoding.UTF8,
            "application/json"
        );

        client.DefaultRequestHeaders.Add("X-Api-Key", scpSettings.ScpApiKey);


        HttpResponseMessage response = await client.PostAsync(scpSettings.ScpApiUri + "disputes/driver", requestContent);
        response.EnsureSuccessStatusCode();
        string responseBody = await response.Content.ReadAsStringAsync();
    }


    public async Task CallOpenTicketAsync(string publicKey, string message)
    {
        var client = new HttpClient();

        var requestData = new
        {
            publicKey,
            message
        };

        var requestContent = new StringContent(
            Newtonsoft.Json.JsonConvert.SerializeObject(requestData),
            System.Text.Encoding.UTF8,
            "application/json"
        );

        client.DefaultRequestHeaders.Add("X-Api-Key", scpSettings.ScpApiKey);


        HttpResponseMessage response = await client.PostAsync(scpSettings.ScpApiUri + "tickets", requestContent);
        response.EnsureSuccessStatusCode();
        string responseBody = await response.Content.ReadAsStringAsync();
    }


    public async Task ManageDisputeAsync(Guid tid, Guid repliercertificateId, bool open)
    {
        using var TX = settlerContext.Value.BEGIN_TRANSACTION();

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
        using var TX = settlerContext.Value.BEGIN_TRANSACTION();

        var gig = (from g in settlerContext.Value.Gigs
                   where g.SignedRequestPayloadId == tid && g.ReplierCertificateId == repliercertificateId && (g.Status == GigStatus.Open || g.Status != GigStatus.Accepted)
                   select g).FirstOrDefault();

        if (gig == null)
            throw new NotFoundException();

        if (gig.Status == GigStatus.Accepted)
        {
            await DescheduleGigAsync(gig);
            var status = WalletAPIResult.Status(await lndWalletClient.CancelInvoiceAsync(MakeAuthToken(), gig.NetworkPaymentHash, CancellationTokenSource.Token));
            if (status != LNDWalletErrorCode.Ok)
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
        using var TX = settlerContext.Value.BEGIN_TRANSACTION();

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
        if (status != LNDWalletErrorCode.Ok)
            throw new InvoiceProblemException(status);

        TX.Commit();

        foreach (var pi in preims)
            this.FireOnPreimageReveal(pi.PaymentHash, pi.Preimage);

        FireOnGigStatus(gig.SignedRequestPayloadId, gig.ReplierCertificateId, GigStatus.Completed, gig.SymmetricKey);

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
        using var TX = settlerContext.Value.BEGIN_TRANSACTION();

        var certids = (from c in (from u in settlerContext.Value.UserCertificates where u.PublicKey == pubkey select u) select c.CertificateId).ToList().ToHashSet();
        if (certids.Count > 0)
            (from u in settlerContext.Value.CertificateProperties where certids.Contains(u.CertificateId) select u).ExecuteDelete();

        (from u in settlerContext.Value.UserCertificates where u.PublicKey == pubkey select u).ExecuteDelete();
        (from u in settlerContext.Value.UserProperties where u.PublicKey == pubkey select u).ExecuteDelete();
        (from u in settlerContext.Value.UserTraceProperties where u.PublicKey == pubkey select u).ExecuteDelete();


        TX.Commit();
    }

    public async Task<string> UploadBlobAsync(string blobName, Stream content)
    {
        // Create BlobServiceClient
        var blobServiceClient = new BlobServiceClient(azureStorageConnectionString);

        // Get the Blob Container
        var containerClient = blobServiceClient.GetBlobContainerClient(azureContainerName);

        // Ensure container exists
        await containerClient.CreateIfNotExistsAsync();

        // Set container access level to public if not already done
        await containerClient.SetAccessPolicyAsync(Azure.Storage.Blobs.Models.PublicAccessType.Blob);

        // Upload the Blob
        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(content, overwrite: true);

        // Return the public URI
        return blobClient.Uri.ToString();
    }
}

public class ScpSettings
{
    public required string ScpApiUri { get; set; }
    public required string ScpApiKey { get; set; }
}
