using System;
namespace NGigGossip4Nostr;

[Serializable]
public class HodlInvoice
{
    public byte[] Preimage { get; set; }

    public Guid Id { get; }
    public byte[] PaymentHash { get; }
    public int Amount { get; }
    public DateTime ValidTill { get; }
    public bool IsAccepted { get; set; }
    public bool IsSettled { get; set; }

    public HodlInvoice(byte[] paymentHash, int amount,
        DateTime validTill, Guid? id = null)
    {
        Preimage = null;
        Id = id ?? Guid.NewGuid();
        PaymentHash = paymentHash;
        Amount = amount;
        ValidTill = validTill;
        IsAccepted = false;
        IsSettled = false;
    }
}


public abstract class HodlInvoicePayer
{
    private static readonly Dictionary<string, HodlInvoicePayer> PAYER_BY_NAME = new Dictionary<string, HodlInvoicePayer>();

    public abstract bool AcceptHodlInvoice(HodlInvoice invoice);

    public string Name;

    public HodlInvoicePayer(string name)
    {
        this.Name = name;
        PAYER_BY_NAME[name] = this;
    }

    public static HodlInvoicePayer GetHodlInvoicePayerByName(string name)
    {
        if (PAYER_BY_NAME.ContainsKey(name))
            return PAYER_BY_NAME[name];
        throw new ArgumentException("Payer not found");
    }
}


