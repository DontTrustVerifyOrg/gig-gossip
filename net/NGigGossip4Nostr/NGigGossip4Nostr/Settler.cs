using System;
using System.Linq;
using System.Xml.Linq;
using NBitcoin.Secp256k1;
namespace NGigGossip4Nostr;

public class Settler : NamedEntity, IHodlInvoiceIssuer, IHodlInvoiceSettler
{
    public readonly Certificate SettlerCertificate;
    private readonly ECPrivKey settlerPrivateKey;
    private readonly int priceAmountForSettlement;

    private Dictionary<Guid, byte[]> invoicePreimageById = new();


    public Settler(string name, Certificate settlerCertificate, ECPrivKey settlerPrivateKey, int priceAmountForSettlement) : base(name)
    {
        this.SettlerCertificate = settlerCertificate;
        this.settlerPrivateKey = settlerPrivateKey;
        this.priceAmountForSettlement = priceAmountForSettlement;
    }

    public Tuple<Guid, byte[]> GenerateReplyPaymentTrust()
    {
        byte[] replyPreimage = Crypto.GenerateSymmetricKey();
        byte[] replyPaymentHash = LND.ComputePaymentHash(replyPreimage);

        Guid invoiceId = Guid.NewGuid();
        lock (invoicePreimageById)
            invoicePreimageById[invoiceId] = replyPreimage;
        return new Tuple<Guid, byte[]>(invoiceId, replyPaymentHash);
    }

    public void RegisterForSettlementInPaymentChain(Guid sourceInvoiceId,Guid nextInvoiceId)
    {
        lock (invoicePreimageById)
            invoicePreimageById[nextInvoiceId] = invoicePreimageById[sourceInvoiceId];
    }

    public Tuple<SettlementPromise, HodlInvoice, byte[]> GenerateSettlementTrust(string issuerName, string payerName, byte[] message, HodlInvoice replyInvoice, RequestPayload signedRequestPayload, Certificate replierCertificate)
    {
        byte[] networkPreimage = Crypto.GenerateSymmetricKey();
        byte[] networkPaymentHash = LND.ComputePaymentHash(networkPreimage);
        byte[] encryptedReplyMessage = Crypto.SymmetricEncrypt(networkPreimage, message);

        HodlInvoice networkInvoice = LND.CreateHodlInvoice(issuerName, payerName, this.Name,
            priceAmountForSettlement,
            networkPaymentHash,
            DateTime.MaxValue,
            Guid.NewGuid()
        );

        lock(invoicePreimageById)
            invoicePreimageById[networkInvoice.Id] = networkPreimage;

        ReplyPayload replyPayload = new ReplyPayload()
        {
            ReplierCertificate = replierCertificate,
            SignedRequestPayload = signedRequestPayload,
            EncryptedReplyMessage = encryptedReplyMessage,
            ReplyInvoice = replyInvoice
        };

        byte[] encryptedReplyPayload = Crypto.EncryptObject(replyPayload,  signedRequestPayload.SenderCertificate.PublicKey, this.settlerPrivateKey);
        byte[] hashOfEncryptedReplyPayload = Crypto.ComputeSha256(new List<byte[]> { encryptedReplyPayload });

        SettlementPromise signedSettlementPromise = new SettlementPromise()
        {
            SettlerCertificate = SettlerCertificate,
            NetworkPaymentHash = networkPaymentHash,
            HashOfEncryptedReplyPayload = hashOfEncryptedReplyPayload,
            ReplyPaymentAmount = replyInvoice.Amount
        };
        signedSettlementPromise.Sign(settlerPrivateKey);

        return new Tuple<SettlementPromise, HodlInvoice, byte[]>(signedSettlementPromise, networkInvoice, encryptedReplyPayload);
    }

    public void OnHodlInvoiceAccepted(HodlInvoice invoice)
    {
    }

    public void OnHodlInvoiceSettled(HodlInvoice invoice)
    {
    }

    public bool SettlingHodlInvoice(HodlInvoice invoice)
    {
        if (invoice.IsSettled)
        {
            return false;
        }

        lock (invoicePreimageById)
            if (invoicePreimageById.ContainsKey(invoice.Id))
            {
                byte[] preimage = invoicePreimageById[invoice.Id];
                if (invoice.IsAccepted && LND.ComputePaymentHash(preimage).SequenceEqual(invoice.PaymentHash))
                {
                    invoice.Preimage = preimage;
                    return true;
                }
            }
        return false;
    }
}

