using System;
using System.Diagnostics;

namespace NGigGossip4Nostr;


public class PaymentChannel
{
    private static readonly Dictionary<Guid, IHodlInvoiceIssuer> HODL_ISSUER_BY_ID = new Dictionary<Guid, IHodlInvoiceIssuer>();
    private static readonly Dictionary<Guid, IHodlInvoicePayer> HODL_PAYER_BY_ID = new Dictionary<Guid, IHodlInvoicePayer>();
    private static readonly Dictionary<Guid, IHodlInvoiceSettler> HODL_SETTLER_BY_ID = new Dictionary<Guid, IHodlInvoiceSettler>();

    public HodlInvoice CreateHodlInvoice(string issuerName, string payerName, string settlerName, int amount, byte[] paymentHash,DateTime validTill, Guid invoiceId)
    {
        HODL_ISSUER_BY_ID[invoiceId] = (IHodlInvoiceIssuer)NamedEntity.GetByName(issuerName);
        HODL_PAYER_BY_ID[invoiceId] = (IHodlInvoicePayer)NamedEntity.GetByName(payerName);
        HODL_SETTLER_BY_ID[invoiceId] = (IHodlInvoiceSettler)NamedEntity.GetByName(settlerName);
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

        invoice.IsAccepted = HODL_PAYER_BY_ID[invoice.Id].OnHodlInvoiceAccepting(invoice);
        if(invoice.IsAccepted)
        {
            HODL_SETTLER_BY_ID[invoice.Id].SettleHodlInvoice(invoice);
        }
    }

    public void SettleHodlInvoiceComplete(HodlInvoice invoice)
    {
        if(invoice.IsSettled)
        {
            HODL_PAYER_BY_ID[invoice.Id].OnHodlInvoiceSettled(invoice);
        }
    }
}