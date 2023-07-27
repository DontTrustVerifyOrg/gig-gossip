using System;
using System.Linq;
using System.Xml.Linq;
using NBitcoin.Secp256k1;
using CryptoToolkit;

namespace NGigGossip4Nostr;

public class InvoiceSettlementStatus
{

}

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

    public byte[] GenerateReplyPaymentTrust(ECXOnlyPubKey pubKey)
    {
        byte[] replyPreimage = Crypto.GenerateSymmetricKey();
        byte[] replyPaymentHash = LND.ComputePaymentHash(replyPreimage);

        Guid invoiceId = Guid.NewGuid();
        lock (invoicePreimageById)
            invoicePreimageById[invoiceId] = replyPreimage;

        var trust = new Tuple<Guid, byte[]>(invoiceId, replyPaymentHash);

        return Crypto.EncryptObject(trust, pubKey, this.settlerPrivateKey);
    }

    public void RegisterForSettlementInPaymentChain(Guid sourceInvoiceId,Guid nextInvoiceId)
    {
        lock (invoicePreimageById)
            invoicePreimageById[nextInvoiceId] = invoicePreimageById[sourceInvoiceId];
    }

    public byte[] GenerateSettlementTrust(ECXOnlyPubKey pubkey, string payerName, byte[] message, HodlInvoice replyInvoice, RequestPayload signedRequestPayload, Certificate replierCertificate)
    {
        if (!invoicePreimageById.ContainsKey(replyInvoice.Id))
            return null;

        if (!LND.ComputePaymentHash(invoicePreimageById[replyInvoice.Id]).SequenceEqual(replyInvoice.PaymentHash))
            return null;

        byte[] networkPreimage = Crypto.GenerateSymmetricKey();
        byte[] networkPaymentHash = LND.ComputePaymentHash(networkPreimage);
        byte[] encryptedReplyMessage = Crypto.SymmetricEncrypt(networkPreimage, message);

        HodlInvoice networkInvoice = LND.CreateHodlInvoice(this.Name, payerName, this.Name,
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

        var trust= new Tuple<SettlementPromise, HodlInvoice, byte[]>(signedSettlementPromise, networkInvoice, encryptedReplyPayload);
        return Crypto.EncryptObject(trust, pubkey, settlerPrivateKey);
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

