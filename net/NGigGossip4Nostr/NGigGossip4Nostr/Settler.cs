using System;
using NBitcoin.Secp256k1;
namespace NGigGossip4Nostr;

public class Settler
{
    private readonly Certificate settlerCertificate;
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

    public Settler(Certificate settlerCertificate, ECPrivKey settlerPrivateKey, PaymentChannel paymentChannel, int priceAmountForSettlement)
    {
        this.settlerCertificate = settlerCertificate;
        this.settlerPrivateKey = settlerPrivateKey;
        this.paymentChannel = paymentChannel;
        this.priceAmountForSettlement = priceAmountForSettlement;
    }

    public Tuple<Guid, byte[], Action<HodlInvoice>> GenerateReplyPaymentTrust()
    {
        byte[] replyPreimage = Crypto.GenerateSymmetricKey();
        byte[] replyPaymentHash = LND.ComputePaymentHash(replyPreimage);

        Guid invoiceId = Guid.NewGuid();
        SetSettementCommand(paymentChannel, invoiceId, replyPreimage);
        return new Tuple<Guid, byte[], Action<HodlInvoice>>(invoiceId, replyPaymentHash, OnSettementCommand);
    }

    public Tuple<SettlementPromise, HodlInvoice, byte[]> GenerateSettlementTrust(byte[] message, HodlInvoice replyInvoice, RequestPayload signedRequestPayload, Certificate replierCertificate)
    {
        byte[] networkPreimage = Crypto.GenerateSymmetricKey();
        byte[] networkPaymentHash = LND.ComputePaymentHash(networkPreimage);
        byte[] encryptedReplyMessage = Crypto.SymmetricEncrypt(networkPreimage, message);

        void onAccepted(HodlInvoice i)
        {
            paymentChannel.SettleHodlInvoice(i, networkPreimage);
        }

        HodlInvoice networkInvoice = paymentChannel.CreateHodlInvoice(
            priceAmountForSettlement,
            networkPaymentHash,
            onAccepted,
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
            SettlerCertificate = settlerCertificate,
            NetworkPaymentHash = networkPaymentHash,
            HashOfEncryptedReplyPayload = hashOfEncryptedReplyPayload,
            ReplyPaymentAmount = replyInvoice.Amount
        };
        signedSettlementPromise.Sign(settlerPrivateKey);

        return new Tuple<SettlementPromise, HodlInvoice, byte[]>(signedSettlementPromise, networkInvoice, encryptedReplyPayload);
    }
}