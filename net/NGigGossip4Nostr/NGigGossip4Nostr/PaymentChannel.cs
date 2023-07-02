using System;
using System.Diagnostics;

namespace NGigGossip4Nostr;


public class PaymentChannel
{
    private static readonly Dictionary<Guid, HodlInvoicePayer> HODL_PAYER_BY_ID = new Dictionary<Guid, HodlInvoicePayer>();
    private static readonly Dictionary<Guid, Settler> HODL_SETTLER_BY_ID = new Dictionary<Guid, Settler>();

    public HodlInvoice CreateHodlInvoice(string payerName, string settlerName, int amount, byte[] paymentHash,DateTime validTill, Guid invoiceId)
    {
        HODL_PAYER_BY_ID[invoiceId] = HodlInvoicePayer.GetHodlInvoicePayerByName(payerName);
        HODL_SETTLER_BY_ID[invoiceId] = Settler.GetSettlerByName(settlerName);
        return new HodlInvoice(paymentHash, amount, validTill, invoiceId);
    }

    public Invoice CreateInvoice(int amount, byte[] preimage, DateTime validTill = default)
    {
        return new Invoice(preimage, amount, validTill);
    }

    public void PayHodlInvoice(HodlInvoice invoice)
    {
        if (invoice.IsAccepted)
        {
            return;
        }

        if (DateTime.Now > invoice.ValidTill)
        {
            return;
        }

        invoice.IsAccepted = HODL_PAYER_BY_ID[invoice.Id].AcceptHodlInvoice(invoice);
    }

    public void SettleHodlInvoice(HodlInvoice invoice, byte[] preimage)
    {
        if (invoice.IsSettled)
        {
            return;
        }

        if (invoice.IsAccepted && LND.ComputePaymentHash(preimage) == invoice.PaymentHash)
        {
            invoice.Preimage = preimage;
            invoice.IsSettled = HODL_SETTLER_BY_ID[invoice.Id].SettleHodlInvoice(invoice);
        }
    }
}