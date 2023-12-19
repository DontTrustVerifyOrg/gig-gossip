using CryptoToolkit;
using Microsoft.AspNetCore.Mvc;
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
        public void GiveUserProperty(string authToken, string pubkey, string name, string value, string secret, DateTime validTill)
        {
            Singlethon.Settler.ValidateAuthToken(authToken);
            Singlethon.Settler.GiveUserProperty(pubkey, name, Convert.FromBase64String(value), Convert.FromBase64String(secret), validTill);
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
            throw new NotImplementedException();
        }

        /// <summary>
        ///     Submits the secret code for the channel.
        /// </summary>
        /// <remarks>
        ///     Returns -1 if the secret is correct, otherwise the number of retries left is returned.
        /// </remarks>
        /// <param name="authToken">Authorisation token for the communication. 
        ///     This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.
        /// </param>
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
            Singlethon.Settler.GiveUserProperty(pubkey, name, Encoding.UTF8.GetBytes("valid"), Encoding.UTF8.GetBytes(method + ":" + value), DateTime.MaxValue);
            return -1;
        }

        /// <summary>
        ///     Revokes a property from the subject (e.g. driving licence is taken by the police). Only authorised users can revoke the property.
        /// </summary>
        /// <param name="authToken">Authorisation token for the communication. 
        ///     This is a restricted call and authToken needs to be the token of the authorised user excluding the Subject.</param>
        /// <param name="pubkey">Public key of the subject.</param>
        /// <param name="name">Name of the property.</param>
        [HttpGet("revokeuserproperty")]
        public void RevokeUserProperty(string authToken, string pubkey, string name)
        {
            Singlethon.Settler.ValidateAuthToken(authToken);
            Singlethon.Settler.RevokeUserProperty(pubkey, name);
        }


        /// <summary>
        ///     Generates new reply payment preimage and returns its hash.
        /// </summary>
        /// <remarks>
        ///     Generates new reply payment preimage for the lightning network HODL invoice. 
        ///     This preimage is secret as long as the gig-job referenced by gigId is not marked as settled. 
        ///     The method returns hash of this preimage.
        /// </remarks>
        /// <param name="authToken">Authorisation token for the communication.</param>
        /// <param name="gigId">gig-job identifier</param>
        /// <param name="repliperPubKey">Public key of the replier.</param>
        /// <returns></returns>
        [HttpGet("generatereplypaymentpreimage")]
        public string GenerateReplyPaymentPreimage(string authToken, Guid gigId, string repliperPubKey)
        {
            var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
            return Singlethon.Settler.GenerateReplyPaymentPreimage(pubkey, gigId, repliperPubKey);
        }

        /// <summary>
        ///     Generates new payment preimage that is related to the given paymentHash and returns its hash..
        /// </summary>
        /// <remarks>
        ///     Generates new reply payment preimage for the lightning network HODL invoice. Allows implementing payment chains. 
        ///     This preimage is secret as long as the gig-job referenced paymentHash is not marked as settled. 
        ///     The method returns hash of this preimage.
        /// </remarks>
        /// <param name="authToken">Authorisation token for the communication.</param>
        /// <param name="paymentHash">Payment hash of related HODL invoice.</param>
        /// <returns></returns>
        [HttpGet("generaterelatedpreimage")]
        public string GenerateRelatedPreimage(string authToken, string paymentHash)
        {
            var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
            return Singlethon.Settler.GenerateRelatedPreimage(pubkey, paymentHash);
        }

        /// <summary>
        ///     Validates if given paymentHashes were generated by the same settler for the same gig.
        /// </summary>
        /// <remarks>
        ///     Validates if given paymentHashes were generated by the same settler for the same gig. 
        ///     Allows implementing payment chains. 
        ///     The method returns true if the condition is met, false otherwise.
        /// </remarks>
        /// <param name="authToken">Authorisation token for the communication.</param>
        /// <param name="paymentHash1">Payment hash of related HODL invoice.</param>
        /// <param name="paymentHash2">Payment hash of related HODL invoice.</param>
        /// <returns></returns>
        [HttpGet("validaterelatedpaymenthashes")]
        public bool ValidateRelatedPaymentHashes(string authToken, string paymentHash1, string paymentHash2)
        {
            var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
            return Singlethon.Settler.ValidateRelatedPaymentHashes(pubkey, paymentHash1, paymentHash2);
        }


        /// <summary>
        ///     Reveals payment preimage of the specific paymentHash
        /// </summary>
        /// <remarks>
        ///     Reveals payment preimage for the settlement of lightning network HODL invoice. This preimage is secret as long as the gig-job referenced paymentHash is not marked as settled.
        /// </remarks>
        /// <param name="authToken">Authorisation token for the communication.</param>
        /// <param name="paymentHash">Payment hash of related HODL invoice.</param>
        /// <returns></returns>
        [HttpGet("revealpreimage")]
        public string RevealPreimage(string authToken, string paymentHash)
        {
            var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
            return Singlethon.Settler.RevealPreimage(pubkey, paymentHash);
        }

        /// <summary>
        ///     Reveals symmetric key that customer can use to decrypt the message from gig-worker.
        /// </summary>
        /// <remarks>
        ///     Reveals symmetric key that customer can use to decrypt the message from gig-worker. This key is secret as long as the gig-job is not marked as accepted.
        /// </remarks>
        /// <param name="authToken">Authorisation token for the communication.</param>
        /// <param name="signedRequestPayloadId">CertificateId of the sender.</param>
        /// <param name="repliperCertificateId">CertificateId of the replier.</param>
        /// <returns></returns>
        [HttpGet("revealsymmetrickey")]
        public string RevealSymmetricKey(string authToken, Guid signedRequestPayloadId, Guid repliperCertificateId)
        {
            var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
            return Singlethon.Settler.RevealSymmetricKey(signedRequestPayloadId, repliperCertificateId);
        }

        /// <summary>
        ///     Genertes RequestPayload for the specific topic.
        /// </summary>
        /// <remarks>
        ///     Genertes RequestPayload for the specific topic.
        /// </remarks>
        /// <param name="authToken">Authorisation token for the communication.</param>
        /// <param name="properties">Requested properties of the sender.</param>
        /// <param name="serialisedTopic">Topic</param>
        /// <returns></returns>
        [HttpGet("generaterequestpayload")]
        public string GenerateRequestPayload(string authToken, string[] properties, string serialisedTopic)
        {
            var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
            var st = Singlethon.Settler.GenerateRequestPayload(pubkey, properties, Convert.FromBase64String(serialisedTopic));
            return Convert.ToBase64String(Crypto.SerializeObject(st));
        }

        /// <summary>
        ///     Genertes Settlement Trust used by the gig-worker to estabilish trusted primise with the custmer.
        /// </summary>
        /// <remarks>
        ///     Genertes Settlement Trust used by the gig-worker to estabilish trusted primise with the custmer.
        /// </remarks>
        /// <param name="authToken">Authorisation token for the communication.</param>
        /// <param name="properties">Requested properties of the replier.</param>
        /// <param name="message">Message to be encrypted</param>
        /// <param name="replyinvoice">Invoice for the job.</param>
        /// <param name="signedRequestPayloadSerialized">Request payload</param>
        /// <returns></returns>
        [HttpGet("generatesettlementtrust")]
        public async Task<string> GenerateSettlementTrust(string authToken, string[] properties, string message, string replyinvoice, string signedRequestPayloadSerialized)
        {
            var pubkey = Singlethon.Settler.ValidateAuthToken(authToken);
            var signedRequestPayload = Crypto.DeserializeObject<Certificate<RequestPayloadValue>>(Convert.FromBase64String(signedRequestPayloadSerialized));
            var st = await Singlethon.Settler.GenerateSettlementTrustAsync(pubkey, properties, Convert.FromBase64String(message), replyinvoice, signedRequestPayload);
            return Convert.ToBase64String(Crypto.SerializeObject(st));
        }

        /// <summary>
        ///     Encrypts the object using public key related to the specific certioficate id.
        /// </summary>
        /// <remarks>
        ///     Encrypts the object using public key related to the specific certioficate id.
        /// </remarks>
        /// <param name="certificateId">Certificate ID</param>
        /// <param name="objectSerialized">Serialized Object</param>
        /// <returns></returns>
        [HttpGet("encryptobjectforcertificateid")]
        public string EncryptObjectForCertificateId(Guid certificateId, string objectSerialized)
        {
            byte[] encryptedReplyPayload = Singlethon.Settler.EncryptObjectForCertificateId(Convert.FromBase64String(objectSerialized), certificateId);
            return Convert.ToBase64String(encryptedReplyPayload);
        }


        /// <summary>
        ///     Allows opening and closing disputes.
        /// </summary>
        /// <remarks>
        ///     Allows opening and closing disputes. After opening, the dispute needs to be solved positively before the HODL invoice timeouts occure. 
        ///     Otherwise all the invoices and payments will be cancelled.
        /// </remarks>
        /// <param name="authToken">Authorisation token for the communication. This is a restricted call and authToken needs to be the token of the authorised user.</param>
        /// <param name="gigId">Gig-job identifier.</param>
        /// <param name="repliperCertificateId">CertificateId of the replier.</param>
        /// <param name="open">True to open/False to close dispute.</param>
        /// <returns></returns>
        [HttpGet("managedispute")]
        public async Task ManageDispute(string authToken, Guid gigId, Guid repliperCertificateId, bool open)
        {
            Singlethon.Settler.ValidateAuthToken(authToken);
            await Singlethon.Settler.ManageDisputeAsync(gigId, repliperCertificateId, open);
        }
    }
}
