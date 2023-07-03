using System;
using System.Xml.Linq;
using NBitcoin.Secp256k1;
namespace NGigGossip4Nostr;

public class Settler : HodlInvoicePayer
{
    private static readonly Dictionary<string, Settler> ST_BY_NAME = new Dictionary<string, Settler>();

    public readonly Certificate SettlerCertificate;
    private readonly ECPrivKey settlerPrivateKey;
    private readonly PaymentChannel paymentChannel;
    private readonly int priceAmountForSettlement;

    public static Dictionary<Guid, Tuple<PaymentChannel, byte[]>> InvoiceById = new Dictionary<Guid, Tuple<PaymentChannel, byte[]>>();

    public static void SetSettementCommand(PaymentChannel paymentChannel, Guid invoice_id, byte[] preimage)
    {
        InvoiceById[invoice_id] = new Tuple<PaymentChannel, byte[]>(paymentChannel, preimage);
    }

    public static void OnSettementCommand(HodlInvoice invoice)
    {
        if (InvoiceById.ContainsKey(invoice.Id))
        {
            var tuple = InvoiceById[invoice.Id];
            PaymentChannel paymentChannel = tuple.Item1;
            byte[] preimage = tuple.Item2;
            paymentChannel.SettleHodlInvoice(invoice, preimage);
        }
    }

    public Settler(string name,Certificate settlerCertificate, ECPrivKey settlerPrivateKey, PaymentChannel paymentChannel, int priceAmountForSettlement):base(name)
    {
        this.SettlerCertificate = settlerCertificate;
        this.settlerPrivateKey = settlerPrivateKey;
        this.paymentChannel = paymentChannel;
        this.priceAmountForSettlement = priceAmountForSettlement;
        ST_BY_NAME[name] = this;
    }

    public static Settler GetSettlerByName(string caName)
    {
        if (ST_BY_NAME.ContainsKey(caName))
            return ST_BY_NAME[caName];
        throw new ArgumentException("ST not found");
    }

    public Tuple<Guid, byte[]> GenerateReplyPaymentTrust()
    {
        byte[] replyPreimage = Crypto.GenerateSymmetricKey();
        byte[] replyPaymentHash = LND.ComputePaymentHash(replyPreimage);

        Guid invoiceId = Guid.NewGuid();
        SetSettementCommand(paymentChannel, invoiceId, replyPreimage);
        return new Tuple<Guid, byte[]>(invoiceId, replyPaymentHash);
    }

    public bool SettleHodlInvoice(HodlInvoice invoice)
    {
        return false;
    }

    public Tuple<SettlementPromise, HodlInvoice, byte[]> GenerateSettlementTrust(string payerName,byte[] message, HodlInvoice replyInvoice, RequestPayload signedRequestPayload, Certificate replierCertificate)
    {
        byte[] networkPreimage = Crypto.GenerateSymmetricKey();
        byte[] networkPaymentHash = LND.ComputePaymentHash(networkPreimage);
        byte[] encryptedReplyMessage = Crypto.SymmetricEncrypt(networkPreimage, message);

        HodlInvoice networkInvoice = paymentChannel.CreateHodlInvoice(payerName,this.Name,
            priceAmountForSettlement,
            networkPaymentHash,
            DateTime.MaxValue,
            Guid.NewGuid()
        );

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

    public override bool AcceptHodlInvoice(HodlInvoice invoice)
    {
        //            paymentChannel.SettleHodlInvoice(i, networkPreimage);

        throw new NotImplementedException();
    }
}