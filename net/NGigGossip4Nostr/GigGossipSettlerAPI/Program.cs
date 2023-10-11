using System;
using NBitcoin.Secp256k1;
using NGigGossip4Nostr;
using CryptoToolkit;
using Microsoft.AspNetCore.Builder;
using GigLNDWalletAPIClient;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using Microsoft.AspNetCore.SignalR.Client;
using GigGossipSettler;
using GigGossipSettlerAPI;
using System.Xml.Linq;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.EnableAnnotations();
});
builder.Services.AddSignalR();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

IConfigurationRoot GetConfigurationRoot(string defaultFolder, string iniName)
{
    var basePath = Environment.GetEnvironmentVariable("GIGGOSSIP_BASEDIR");
    if (basePath == null)
        basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), defaultFolder);
    foreach (var arg in args)
        if (arg.StartsWith("--basedir"))
            basePath = arg.Substring(arg.IndexOf('=') + 1).Trim().Replace("\"", "").Replace("\'", "");

    var builder = new ConfigurationBuilder();
    builder.SetBasePath(basePath)
           .AddIniFile(iniName)
           .AddEnvironmentVariables()
           .AddCommandLine(args);

    return builder.Build();
}

var config = GetConfigurationRoot(".giggossip", "settler.conf");
var settlerSettings = config.GetSection("settler").Get<SettlerSettings>();


var caPrivateKey = settlerSettings.SettlerPrivateKey.AsECPrivKey();

var httpClient = new HttpClient();
var lndWalletClient = new swaggerClient(settlerSettings.GigWalletOpenApi.AbsoluteUri, httpClient);

Singlethon.Settler = new Settler(settlerSettings.ServiceUri, caPrivateKey, settlerSettings.PriceAmountForSettlement, TimeSpan.FromSeconds(settlerSettings.InvoicePaymentTimeoutSec),TimeSpan.FromSeconds(settlerSettings.DisputeTimeoutSec));
Singlethon.Settler.Init(lndWalletClient, settlerSettings.ConnectionString.Replace("$HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), false);

Singlethon.Settler.Start();


app.MapGet("/getcapublickey", () =>
{
    return Singlethon.Settler.CaXOnlyPublicKey.AsHex();
})
.WithName("GetCaPublicKey")
.WithSummary("Public key of this Certification Authority.")
.WithDescription("Public key of this Certification Authority that can be used to validate signatures of e.g. issued certificates.")
.WithOpenApi();

app.MapGet("/iscertificaterevoked", (Guid certid) =>
{
    return Singlethon.Settler.IsCertificateRevoked(certid);
})
.WithName("IsCertificateRevoked")
.WithSummary("Is the certificate revoked by this Certification Authority.")
.WithDescription("Returns true if the certificate has been revoked, false otherwise. Usefull to implement revocation list.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Serial number of the certificate.";
    return g;
});


app.MapGet("/gettoken", (string pubkey) =>
{
    return Singlethon.Settler.GetTokenGuid(pubkey);
})
.WithName("GetToken")
.WithSummary("Creates authorisation token guid")
.WithDescription("Creates a new token Guid that is used for further communication with the API")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "public key identifies the API user";
    return g;
});

app.MapGet("/giveuserproperty", (string authToken, string pubkey, string name, string value, DateTime validTill) =>
{
    Singlethon.Settler.ValidateAuthToken(authToken);
    Singlethon.Settler.GiveUserProperty(pubkey, name, Convert.FromBase64String(value), validTill);
})
.WithName("GiveUserProperty")
.WithSummary("Grants a property to the subject.")
.WithDescription("Grants a property to the subject (e.g. driving licence). Only authorised users can grant the property.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.";
    g.Parameters[1].Description = "Public key of the subject.";
    g.Parameters[2].Description = "Name of the property.";
    g.Parameters[3].Description = "Value of the property.";
    g.Parameters[4].Description = "Date and time after which the property will not be valid anymore";
    return g;
});

app.MapGet("/verifychannel", (string authToken, string pubkey, string name, string method, string value) =>
{
    Singlethon.Settler.ValidateAuthToken(authToken);
    Singlethon.Settler.GiveUserProperty(pubkey, name, Convert.FromBase64String(method+":"+value), DateTime.MaxValue);
})
.WithName("VerifyChannel")
.WithSummary("Verifies specific channel.")
.WithDescription("Verifies specific channel.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.";
    g.Parameters[1].Description = "Public key of the subject.";
    g.Parameters[2].Description = "Channel name (phone,email,...)";
    g.Parameters[3].Description = "Method (sms,call,message)";
    g.Parameters[4].Description = "Value of Channel for the method (phone number, email address).";
    return g;
});

app.MapGet("/revokeuserproperty", (string authToken, string pubkey, string name) =>
{
    Singlethon.Settler.ValidateAuthToken(authToken);
    Singlethon.Settler.RevokeUserProperty(pubkey, name);
})
.WithDescription("Revokes a property from the subject (e.g. driving licence is taken by the police). Only authorised users can revoke the property.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.";
    g.Parameters[1].Description = "Public key of the subject.";
    g.Parameters[2].Description = "Name of the property.";
    return g;
});

app.MapGet("/issuecertificate", (string authToken, string pubkey, string[] properties) =>
{
    Singlethon.Settler.ValidateAuthToken(authToken);
    return Crypto.SerializeObject(Singlethon.Settler.IssueCertificate(pubkey, properties));
})
.WithName("IssueCertificate")
.WithSummary("Issues a new Digital Certificate for the Subject.")
.WithDescription("Issues a new Digital Certificate for the Subject. Only authorised users can issue the certificate.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user including Subject.";
    g.Parameters[1].Description = "Public key of the Subject.";
    g.Parameters[2].Description = "List of properties for the certificate";
    return g;
});

app.MapGet("/getcertificate", (string authToken, string pubkey, Guid certid) =>
{
    Singlethon.Settler.ValidateAuthToken(authToken);
    return Crypto.SerializeObject(Singlethon.Settler.GetCertificate(pubkey, certid));
})
.WithName("GetCertificate")
.WithSummary("Returns an existing Digital Certificate for the Subject.")
.WithDescription("Returns an existing Digital Certificate for the Subject. Only authorised users can get the certificate.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user including Subject.";
    g.Parameters[1].Description = "Public key of the Subject.";
    g.Parameters[2].Description = "Serial number of the certificate.";
    return g;
});

app.MapGet("/listcertificates", (string authToken, string pubkey) =>
{
    Singlethon.Settler.ValidateAuthToken(authToken);
    return Singlethon.Settler.ListCertificates(pubkey);
})
.WithName("ListCertificates")
.WithSummary("Lists serial numbers of all Digital Certificate issued for the Subject.")
.WithDescription("Lists serial numbers of all Digital Certificate issued for the Subject. Only authorised users can list certificates.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user including Subject.";
    g.Parameters[1].Description = "Public key of the Subject.";
    return g;
});

app.MapGet("/generatereplypaymentpreimage", (string authToken, Guid gigId, string repliperPubKey) =>
{
    var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
    return Singlethon.Settler.GenerateReplyPaymentPreimage(pubkey, gigId, repliperPubKey);
})
.WithName("GenerateReplyPaymentPreimage")
.WithSummary("Generates new reply payment preimage and returns its hash.")
.WithDescription("Generates new reply payment preimage for the lightning network HODL invoice. This preimage is secret as long as the gig-job referenced by gigId is not marked as settled. The method returns hash of this preimage.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication.";
    g.Parameters[1].Description = "gig-job identifier";
    g.Parameters[2].Description = "Public key of the replier.";
    return g;
});

app.MapGet("/generaterelatedpreimage", (string authToken, string paymentHash) =>
{
    var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
    return Singlethon.Settler.GenerateRelatedPreimage(pubkey, paymentHash);
})
.WithName("GenerateRelatedPreimage")
.WithSummary("Generates new payment preimage that is related to the given paymentHash and returns its hash..")
.WithDescription("Generates new reply payment preimage for the lightning network HODL invoice. Allows implementing payment chains. This preimage is secret as long as the gig-job referenced paymentHash is not marked as settled. The method returns hash of this preimage.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication.";
    g.Parameters[1].Description = "Payment hash of related HODL invoice.";
    return g;
});

app.MapGet("/validaterelatedpaymenthashes", (string authToken, string paymentHash1,string paymentHash2) =>
{
    var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
    return Singlethon.Settler.ValidateRelatedPaymentHashes(pubkey, paymentHash1,paymentHash2);
})
.WithName("ValidateRelatedPaymentHashes")
.WithSummary("Validates if given paymentHashes were generated by the same settler for the same gig.")
.WithDescription("Validates if given paymentHashes were generated by the same settler for the same gig. Allows implementing payment chains. The method returns true if the condition is met, false otherwise.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication.";
    g.Parameters[1].Description = "Payment hash of related HODL invoice.";
    g.Parameters[2].Description = "Payment hash of related HODL invoice.";
    return g;
});

app.MapGet("/revealpreimage", (string authToken, string paymentHash) =>
{
    var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
    return Singlethon.Settler.RevealPreimage(pubkey, paymentHash);
})
.WithName("RevealPreimage")
.WithSummary("Reveals payment preimage of the specific paymentHash")
.WithDescription("Reveals payment preimage for the settlement of lightning network HODL invoice. This preimage is secret as long as the gig-job referenced paymentHash is not marked as settled.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication.";
    g.Parameters[1].Description = "Payment hash of related HODL invoice.";
    return g;
});


app.MapGet("/generatesettlementtrust", (string authToken, string message, string replyinvoice, string signedRequestPayloadSerialized, string replierCertificateSerialized) =>
{
    var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
    var signedRequestPayload = Crypto.DeserializeObject< RequestPayload>(Convert.FromBase64String(signedRequestPayloadSerialized));
    var replierCertificate = Crypto.DeserializeObject< Certificate>(Convert.FromBase64String(replierCertificateSerialized));
    var st =  Singlethon.Settler.GenerateSettlementTrustAsync(pubkey, Convert.FromBase64String(message), replyinvoice, signedRequestPayload, replierCertificate).Result;
    return Convert.ToBase64String(Crypto.SerializeObject(st));
})
.WithName("GenerateSettlementTrust")
.WithSummary("Genertes Settlement Trust used by the gig-worker to estabilish trusted primise with the custmer.")
.WithDescription("Genertes Settlement Trust used by the gig-worker to estabilish trusted primise with the custmer.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication.";
    g.Parameters[1].Description = "Message to be delivered to the customer.";
    g.Parameters[2].Description = "Invoice for the job.";
    g.Parameters[3].Description = "Request payload";
    g.Parameters[4].Description = "Gig-worker certificate";
    return g;
});

app.MapGet("/revealsymmetrickey", (string authToken, Guid gigId, string repliperPubKey) =>
{
    var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
    return Singlethon.Settler.RevealSymmetricKey(pubkey, gigId, repliperPubKey);
})
.WithName("RevealSymmetricKey")
.WithSummary("Reveals symmetric key that customer can use to decrypt the message from gig-worker.")
.WithDescription("Reveals symmetric key that customer can use to decrypt the message from gig-worker. This key is secret as long as the gig-job is not marked as accepted.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication.";
    g.Parameters[1].Description = "Gig-job identifier.";
    g.Parameters[2].Description = "Public key of the replier.";
    return g;
});

app.MapGet("/managedispute", (string authToken, Guid gigId, string repliperPubKey, bool open) =>
{
    Singlethon.Settler.ValidateAuthToken(authToken);
    Singlethon.Settler.ManageDispute(gigId, repliperPubKey, open);
})
.WithName("ManageDispute")
.WithSummary("Allows opening and closing disputes.")
.WithDescription("Allows opening and closing disputes. After opening, the dispute needs to be solved positively before the HODL invoice timeouts occure. Otherwise all the invoices and payments will be cancelled.")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user.";
    g.Parameters[1].Description = "Gig-job identifier.";
    g.Parameters[2].Description = "Public key of the replier.";
    g.Parameters[3].Description = "True to open/False to close dispute.";
    return g;
});

app.MapHub<PreimageRevealHub>("/preimagereveal");
app.MapHub<SymmetricKeyRevealHub>("/symmetrickeyreveal");

app.Run(settlerSettings.ServiceUri.AbsoluteUri);

public class SettlerSettings
{
    public Uri ServiceUri { get; set; }
    public Uri GigWalletOpenApi { get; set; }
    public long PriceAmountForSettlement { get; set; }
    public string ConnectionString { get; set; }
    public string SettlerPrivateKey { get; set; }
    public long InvoicePaymentTimeoutSec { get; set; }
    public long DisputeTimeoutSec { get; set; }
}