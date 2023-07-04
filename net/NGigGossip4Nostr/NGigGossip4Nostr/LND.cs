using System;
namespace NGigGossip4Nostr;
public static class LND
{
    public static byte[] ComputePaymentHash(byte[] preimage)
    {
        return Crypto.ComputeSha512(new List<byte[]> { preimage });
    }

    private static object guard = new object();

    private static Dictionary<Guid, IHodlInvoiceIssuer> HODL_ISSUER_BY_ID = new Dictionary<Guid, IHodlInvoiceIssuer>();
    private static Dictionary<Guid, IHodlInvoicePayer> HODL_PAYER_BY_ID = new Dictionary<Guid, IHodlInvoicePayer>();
    private static Dictionary<Guid, IHodlInvoiceSettler> HODL_SETTLER_BY_ID = new Dictionary<Guid, IHodlInvoiceSettler>();

    public static HodlInvoice CreateHodlInvoice(string issuerName, string payerName, string settlerName, int amount, byte[] paymentHash, DateTime validTill, Guid invoiceId)
    {
        lock (guard)
        {
            HODL_ISSUER_BY_ID[invoiceId] = (IHodlInvoiceIssuer)NamedEntity.GetByEntityName(issuerName);
            HODL_PAYER_BY_ID[invoiceId] = (IHodlInvoicePayer)NamedEntity.GetByEntityName(payerName);
            HODL_SETTLER_BY_ID[invoiceId] = (IHodlInvoiceSettler)NamedEntity.GetByEntityName(settlerName);
            return new HodlInvoice(paymentHash, amount, validTill, invoiceId);
        }
    }

    public static Invoice CreateInvoice(int amount, byte[] preimage, DateTime validTill = default)
    {
        return new Invoice(preimage, amount, validTill);
    }

    public static void AcceptHodlInvoice(HodlInvoice invoice)
    {
        if (invoice.IsAccepted)
        {
            return;
        }

        if (DateTime.Now > invoice.ValidTill)
        {
            return;
        }

        IHodlInvoicePayer payer = null;
        lock (guard)
        {
            payer = HODL_PAYER_BY_ID[invoice.Id];
        }

        invoice.IsAccepted = payer.AcceptingHodlInvoice(invoice);

        if (!invoice.IsAccepted)
            return;

        IHodlInvoiceIssuer issuer = null;
        IHodlInvoiceSettler settler = null;
        lock (guard)
        {
            issuer = HODL_ISSUER_BY_ID[invoice.Id];
            settler = HODL_SETTLER_BY_ID[invoice.Id];
        }
        issuer.OnHodlInvoiceAccepted(invoice);
        settler.OnHodlInvoiceAccepted(invoice);

        invoice.IsSettled = settler.SettlingHodlInvoice(invoice);

        if (!invoice.IsSettled)
            return;

        payer.OnHodlInvoiceSettled(invoice);
        issuer.OnHodlInvoiceSettled(invoice);
    }
}

