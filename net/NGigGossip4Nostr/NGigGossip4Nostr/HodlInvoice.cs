using System;
namespace NGigGossip4Nostr;


public class HodlInvoice
{
    public byte[] Preimage;


    public Guid Id { get; }
    public byte[] PaymentHash { get; }
    public int Amount { get; }
    public DateTime ValidTill { get; }
    public bool IsAccepted { get; set; }
    public bool IsSettled { get; set; }
    public Action<HodlInvoice> OnAccepted { get; }
    public Action<HodlInvoice, byte[]> OnSettled { get; set; }

    public HodlInvoice(byte[] paymentHash, int amount, Action<HodlInvoice> onAccepted,
        DateTime validTill, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        PaymentHash = paymentHash;
        Amount = amount;
        ValidTill = validTill;
        IsAccepted = false;
        IsSettled = false;
        OnAccepted = onAccepted;
    }
}



