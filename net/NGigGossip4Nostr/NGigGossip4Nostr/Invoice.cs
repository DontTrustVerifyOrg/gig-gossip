using System;
namespace NGigGossip4Nostr;

public class Invoice
{
    public byte[] Preimage;

    public byte[] PaymentHash { get; }
    public int Amount { get; }
    public DateTime ValidTill { get; }
    public bool IsAccepted { get; set; }

    public Invoice(byte[] preimage, int amount, DateTime validTill)
    {
        Preimage = preimage;
        PaymentHash = LND.ComputePaymentHash(preimage);
        Amount = amount;
        ValidTill = validTill;
        IsAccepted = false;
    }

}