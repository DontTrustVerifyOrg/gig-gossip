using System;
namespace NGigGossip4Nostr;


public class PaymentChannel
{


    public HodlInvoice CreateHodlInvoice(int amount, byte[] paymentHash, Action<HodlInvoice> onAccepted,
        DateTime validTill, Guid? invoiceId)
    {
        return new HodlInvoice(paymentHash, amount, onAccepted, validTill, invoiceId);
    }

    public Invoice CreateInvoice(int amount, byte[] preimage, DateTime validTill = default)
    {
        return new Invoice(preimage, amount, validTill);
    }

    public void PayHodlInvoice(HodlInvoice invoice, Action<HodlInvoice, byte[]> onSettled)
    {
        if (invoice.IsAccepted)
        {
            return;
        }

        if (DateTime.Now > invoice.ValidTill)
        {
            return;
        }

        invoice.OnSettled = onSettled;
        invoice.IsAccepted = true;
        invoice.OnAccepted.Invoke(invoice);
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
            invoice.IsSettled = true;
            invoice.OnSettled.Invoke(invoice, preimage);
        }
    }
}