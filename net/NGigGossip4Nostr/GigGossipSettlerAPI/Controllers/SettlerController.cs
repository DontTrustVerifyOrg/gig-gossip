using CryptoToolkit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NGigGossip4Nostr;
using System.Text;

namespace GigGossipSettlerAPI.Controllers
{
    [Route("api")]
    [ApiController]
    public class SettlerController : ControllerBase
    {
        /// <summary>
        /// Public key of this Certification Authority.
        /// </summary>
        /// <remarks>
        /// Public key of this Certification Authority that can be used to validate signatures of e.g. issued certificates.
        /// </remarks>
        /// <returns></returns>
        [HttpGet("getcapublickey")]
        public string GetCaPublicKey()
        {
            return Singlethon.Settler.CaXOnlyPublicKey.AsHex();
        }


        /// <summary>
        /// Is the certificate revoked by this Certification Authority.
        /// </summary>
        /// <remarks>
        /// Returns true if the certificate has been revoked, false otherwise. Usefull to implement revocation list.
        /// </remarks>
        /// <param name="certid">Serial number of the certificate.</param>
        /// <response code="200">Returns true if the certificate has been revoked, false otherwise.</response>
        [HttpGet("iscertificaterevoked")]
        public bool IsCertificateRevoked(Guid certid)
        {
            return Singlethon.Settler.IsCertificateRevoked(certid);
        }


        /// <summary>
        ///     Creates authorisation token guid.
        /// </summary>
        /// <remarks>
        ///     Creates a new token Guid that is used for further communication with the API.
        /// </remarks>
        /// <param name="pubkey">Public key identifies the API user.</param>
        /// <returns></returns>
        [HttpGet("gettoken")]
        public Guid GetToken(string pubkey)
        {
            return Singlethon.Settler.GetTokenGuid(pubkey);
        }

        /// <summary>
        ///     Grants a property to the subject.
        /// </summary>
        /// <remarks>
        ///     Grants a property to the subject (e.g. driving licence). Only authorised users can grant the property.
        /// </remarks>
        /// <param name="authToken">Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.</param>
        /// <param name="pubkey">Public key of the subject.</param>
        /// <param name="name">Name of the property.</param>
        /// <param name="value">Value of the property.</param>
        /// <param name="validTill">Date and time after which the property will not be valid anymore.</param>
        [HttpGet("giveuserproperty")]
        public void GiveUserProperty(string authToken, string pubkey, string name, string value, DateTime validTill)
        {
            Singlethon.Settler.ValidateAuthToken(authToken);
            Singlethon.Settler.GiveUserProperty(pubkey, name, Convert.FromBase64String(value), validTill);
        }

        /// <summary>
        ///     Start verification of specific channel.
        /// </summary>
        /// <remarks>
        ///     Starts verification of specific channel.
        /// </remarks>
        /// <param name="authToken">Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.</param>
        /// <param name="pubkey">Public key of the subject.</param>
        /// <param name="name">Channel name (phone,email,...)</param>
        /// <param name="method">Method (sms,call,message)</param>
        /// <param name="value">Value of Channel for the method (phone number, email address).</param>
        [HttpGet("verifychannel")]
        public void VerifyChannel(string authToken, string pubkey, string name, string method, string value)
        {
            //TODO:shouldnt this return something??
            Singlethon.Settler.ValidateAuthToken(authToken);
        }

        /// <summary>
        /// Submits the secret code for the channel.
        /// </summary>
        /// <remarks>
        /// Returns -1 if the secret is correct, otherwise the number of retries left is returned.
        /// </remarks>
        /// <param name="authToken">Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.</param>
        /// <param name="pubkey">Public key of the subject.</param>
        /// <param name="name">Channel name (phone,email,...)</param>
        /// <param name="method">Method (sms,call,message)</param>
        /// <param name="value">Value of Channel for the method (phone number, email address).</param>
        /// <param name="secret">Secret received from the channel.</param>
        /// <returns></returns>
        [HttpGet("submitchannelsecret")]
        public int SubmitChannelSecret(string authToken, string pubkey, string name, string method, string value, string secret)
        {
            Singlethon.Settler.ValidateAuthToken(authToken);
            Singlethon.Settler.GiveUserProperty(pubkey, name, Encoding.UTF8.GetBytes(method + ":" + value), DateTime.MaxValue);
            return -1;
        }


//app.MapGet("/revokeuserproperty", (string authToken, string pubkey, string name) =>
//{
//    Singlethon.Settler.ValidateAuthToken(authToken);
//    Singlethon.Settler.RevokeUserProperty(pubkey, name);
//})
//.WithDescription("Revokes a property from the subject (e.g. driving licence is taken by the police). Only authorised users can revoke the property.")
//.WithOpenApi(g =>
//{
//    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.";
//    g.Parameters[1].Description = "Public key of the subject.";
//    g.Parameters[2].Description = "Name of the property.";
//    return g;
//});

//app.MapGet("/generatereplypaymentpreimage", (string authToken, Guid gigId, string repliperPubKey) =>
//{
//    var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
//    return Singlethon.Settler.GenerateReplyPaymentPreimage(pubkey, gigId, repliperPubKey);
//})
//.WithName("GenerateReplyPaymentPreimage")
//.WithSummary("Generates new reply payment preimage and returns its hash.")
//.WithDescription("Generates new reply payment preimage for the lightning network HODL invoice. This preimage is secret as long as the gig-job referenced by gigId is not marked as settled. The method returns hash of this preimage.")
//.WithOpenApi(g =>
//{
//    g.Parameters[0].Description = "Authorisation token for the communication.";
//    g.Parameters[1].Description = "gig-job identifier";
//    g.Parameters[2].Description = "Public key of the replier.";
//    return g;
//});

//app.MapGet("/generaterelatedpreimage", (string authToken, string paymentHash) =>
//{
//    var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
//    return Singlethon.Settler.GenerateRelatedPreimage(pubkey, paymentHash);
//})
//.WithName("GenerateRelatedPreimage")
//.WithSummary("Generates new payment preimage that is related to the given paymentHash and returns its hash..")
//.WithDescription("Generates new reply payment preimage for the lightning network HODL invoice. Allows implementing payment chains. This preimage is secret as long as the gig-job referenced paymentHash is not marked as settled. The method returns hash of this preimage.")
//.WithOpenApi(g =>
//{
//    g.Parameters[0].Description = "Authorisation token for the communication.";
//    g.Parameters[1].Description = "Payment hash of related HODL invoice.";
//    return g;
//});

//app.MapGet("/validaterelatedpaymenthashes", (string authToken, string paymentHash1, string paymentHash2) =>
//{
//    var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
//    return Singlethon.Settler.ValidateRelatedPaymentHashes(pubkey, paymentHash1, paymentHash2);
//})
//.WithName("ValidateRelatedPaymentHashes")
//.WithSummary("Validates if given paymentHashes were generated by the same settler for the same gig.")
//.WithDescription("Validates if given paymentHashes were generated by the same settler for the same gig. Allows implementing payment chains. The method returns true if the condition is met, false otherwise.")
//.WithOpenApi(g =>
//{
//    g.Parameters[0].Description = "Authorisation token for the communication.";
//    g.Parameters[1].Description = "Payment hash of related HODL invoice.";
//    g.Parameters[2].Description = "Payment hash of related HODL invoice.";
//    return g;
//});

//app.MapGet("/revealpreimage", (string authToken, string paymentHash) =>
//{
//    var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
//    return Singlethon.Settler.RevealPreimage(pubkey, paymentHash);
//})
//.WithName("RevealPreimage")
//.WithSummary("Reveals payment preimage of the specific paymentHash")
//.WithDescription("Reveals payment preimage for the settlement of lightning network HODL invoice. This preimage is secret as long as the gig-job referenced paymentHash is not marked as settled.")
//.WithOpenApi(g =>
//{
//    g.Parameters[0].Description = "Authorisation token for the communication.";
//    g.Parameters[1].Description = "Payment hash of related HODL invoice.";
//    return g;
//});

//app.MapGet("/revealsymmetrickey", (string authToken, Guid senderCertificateId, Guid gigId, Guid repliperCertificateId) =>
//{
//    var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
//    return Singlethon.Settler.RevealSymmetricKey(senderCertificateId, gigId, repliperCertificateId);
//})
//.WithName("RevealSymmetricKey")
//.WithSummary("Reveals symmetric key that customer can use to decrypt the message from gig-worker.")
//.WithDescription("Reveals symmetric key that customer can use to decrypt the message from gig-worker. This key is secret as long as the gig-job is not marked as accepted.")
//.WithOpenApi(g =>
//{
//    g.Parameters[0].Description = "Authorisation token for the communication.";
//    g.Parameters[1].Description = "CertificateId of the sender.";
//    g.Parameters[2].Description = "Gig-job identifier.";
//    g.Parameters[3].Description = "CertificateId of the replier.";
//    return g;
//});

//app.MapGet("/generaterequestpayload", (string authToken, string[] properties, string serialisedTopic) =>
//{
//    var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
//    var st = Singlethon.Settler.GenerateRequestPayload(pubkey, properties, Convert.FromBase64String(serialisedTopic));
//    return Convert.ToBase64String(Crypto.SerializeObject(st));
//})
//.WithName("GenerateRequestPayload")
//.WithSummary("Genertes RequestPayload for the specific topic.")
//.WithDescription("Genertes RequestPayload for the specific topic.")
//.WithOpenApi(g =>
//{
//    g.Parameters[0].Description = "Authorisation token for the communication.";
//    g.Parameters[1].Description = "Requested properties of the sender.";
//    g.Parameters[2].Description = "Topic";
//    return g;
//});

//app.MapGet("/generatesettlementtrust", async (string authToken, string[] properties, string message, string replyinvoice, string signedRequestPayloadSerialized) =>
//{
//    var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
//    var signedRequestPayload = Crypto.DeserializeObject<Certificate<RequestPayloadValue>>(Convert.FromBase64String(signedRequestPayloadSerialized));
//    var st = await Singlethon.Settler.GenerateSettlementTrustAsync(pubkey, properties, Convert.FromBase64String(message), replyinvoice, signedRequestPayload);
//    return Convert.ToBase64String(Crypto.SerializeObject(st));
//})
//.WithName("GenerateSettlementTrust")
//.WithSummary("Genertes Settlement Trust used by the gig-worker to estabilish trusted primise with the custmer.")
//.WithDescription("Genertes Settlement Trust used by the gig-worker to estabilish trusted primise with the custmer.")
//.WithOpenApi(g =>
//{
//    g.Parameters[0].Description = "Authorisation token for the communication.";
//    g.Parameters[1].Description = "Requested properties of the replier.";
//    g.Parameters[2].Description = "Message to be encrypted";
//    g.Parameters[3].Description = "Invoice for the job.";
//    g.Parameters[4].Description = "Request payload";
//    return g;
//});

//app.MapGet("/encryptobjectforcertificateid", (Guid certificateId, string objectSerialized) =>
//{
//    byte[] encryptedReplyPayload = Singlethon.Settler.EncryptObjectForCertificateId(Convert.FromBase64String(objectSerialized), certificateId);
//    return Convert.ToBase64String(encryptedReplyPayload);
//})
//.WithName("EncryptObjectForCertificateId")
//.WithSummary("Encrypts the object using public key related to the specific certioficate id.")
//.WithDescription("Encrypts the object using public key related to the specific certioficate id.")
//.WithOpenApi(g =>
//{
//    g.Parameters[0].Description = "Certificate ID";
//    g.Parameters[1].Description = "Serialized Object";
//    return g;
//});

//app.MapGet("/managedispute", async (string authToken, Guid gigId, Guid repliperCertificateId, bool open) =>
//{
//    Singlethon.Settler.ValidateAuthToken(authToken);
//    await Singlethon.Settler.ManageDisputeAsync(gigId, repliperCertificateId, open);
//})
//.WithName("ManageDispute")
//.WithSummary("Allows opening and closing disputes.")
//.WithDescription("Allows opening and closing disputes. After opening, the dispute needs to be solved positively before the HODL invoice timeouts occure. Otherwise all the invoices and payments will be cancelled.")
//.WithOpenApi(g =>
//{
//    g.Parameters[0].Description = "Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user.";
//    g.Parameters[1].Description = "Gig-job identifier.";
//    g.Parameters[2].Description = "CertificateId of the replier.";
//    g.Parameters[3].Description = "True to open/False to close dispute.";
//    return g;
//});
    }
}
