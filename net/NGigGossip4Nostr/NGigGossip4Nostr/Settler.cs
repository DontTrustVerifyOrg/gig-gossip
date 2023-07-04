using System;
using System.Linq;
using System.Xml.Linq;
using NBitcoin.Secp256k1;
namespace NGigGossip4Nostr;

public class Settler : NamedEntity, IHodlInvoiceIssuer, IHodlInvoiceSettler
{
    public readonly Certificate SettlerCertificate;
    private readonly ECPrivKey settlerPrivateKey;
    private readonly PaymentChannel paymentChannel;
    private readonly int priceAmountForSettlement;

    public static Dictionary<Guid, Tuple<PaymentChannel, byte[]>> InvoiceById = new Dictionary<Guid, Tuple<PaymentChannel, byte[]>>();


    public Settler(string name,Certificate settlerCertificate, ECPrivKey settlerPrivateKey, PaymentChannel paymentChannel, int priceAmountForSettlement):base(name)
    {
        this.SettlerCertificate = settlerCertificate;
        this.settlerPrivateKey = settlerPrivateKey;
        this.paymentChannel = paymentChannel;
        this.priceAmountForSettlement = priceAmountForSettlement;
    }

    public Tuple<Guid, byte[]> GenerateReplyPaymentTrust()
    {
        byte[] replyPreimage = Crypto.GenerateSymmetricKey();
        byte[] replyPaymentHash = LND.ComputePaymentHash(replyPreimage);

        Guid invoiceId = Guid.NewGuid();
        InvoiceById[invoiceId] = new Tuple<PaymentChannel, byte[]>(paymentChannel, replyPreimage);
        return new Tuple<Guid, byte[]>(invoiceId, replyPaymentHash);
    }

    public void SettleHodlInvoice(HodlInvoice invoice)
    {
        if (invoice.IsSettled)
        {
            return;
        }

        if (InvoiceById.ContainsKey(invoice.Id))
        {
            var tuple = InvoiceById[invoice.Id];
            PaymentChannel paymentChannel = tuple.Item1;
            byte[] preimage = tuple.Item2;
            if (invoice.IsAccepted && LND.ComputePaymentHash(preimage).SequenceEqual(invoice.PaymentHash))
            {
                invoice.Preimage = preimage;
                invoice.IsSettled = true;
                paymentChannel.SettleHodlInvoiceComplete(invoice);
            }
        }

    }

    public Tuple<SettlementPromise, HodlInvoice, byte[]> GenerateSettlementTrust(string issuerName, string payerName, byte[] message, HodlInvoice replyInvoice, RequestPayload signedRequestPayload, Certificate replierCertificate)
    {
        byte[] networkPreimage = Crypto.GenerateSymmetricKey();
        byte[] networkPaymentHash = LND.ComputePaymentHash(networkPreimage);
        byte[] encryptedReplyMessage = Crypto.SymmetricEncrypt(networkPreimage, message);

        HodlInvoice networkInvoice = paymentChannel.CreateHodlInvoice(issuerName, payerName,this.Name,
            priceAmountForSettlement,
            networkPaymentHash,
            DateTime.MaxValue,
            Guid.NewGuid()
        );

        InvoiceById[networkInvoice.Id] = new Tuple<PaymentChannel, byte[]>(paymentChannel, networkPreimage);

        ReplyPayload replyPayload = new ReplyPayload()
        {
            ReplierCertificate = replierCertificate,
            SignedRequestPayload = signedRequestPayload,
            EncryptedReplyMessage = encryptedReplyMessage,
            ReplyInvoice = replyInvoice
        };

        byte[] encryptedReplyPayload = Crypto.EncryptObject(replyPayload, this.settlerPrivateKey, signedRequestPayload.SenderCertificate.PublicKey);
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

    public bool OnHodlInvoiceAccepting(HodlInvoice invoice)
    {
        //            paymentChannel.SettleHodlInvoice(i, networkPreimage);

        throw new NotImplementedException();
    }


    public void OnHodlInvoicePayed(HodlInvoice invoice)
    {
        throw new NotImplementedException();
    }

    public void OnHodlInvoiceSettled(HodlInvoice invoice)
    {
        throw new NotImplementedException();
    }
}