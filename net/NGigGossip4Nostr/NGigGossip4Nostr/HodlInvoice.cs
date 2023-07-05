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

    public string IssuerName { get; set; }
    public string PayerName { get; set; }
    public string SettlerName { get; set; }

    public HodlInvoice(string issuerName, string payerName, string setterName, byte[] paymentHash, int amount,
        DateTime validTill, Guid? id = null)
    {
        IssuerName = issuerName;
        PayerName = payerName;
        SettlerName = setterName;
        Preimage = null;
        Id = id ?? Guid.NewGuid();
        PaymentHash = paymentHash;
        Amount = amount;
        ValidTill = validTill;
        IsAccepted = false;
        IsSettled = false;
    }

    public HodlInvoice DeepCopy()
    {
        return new HodlInvoice(this.IssuerName, this.PayerName, this.SettlerName, this.PaymentHash.ToArray(), this.Amount, this.ValidTill, this.Id)
        {
            Preimage = (this.Preimage == null) ? null : this.Preimage.ToArray(),
            IsAccepted = this.IsAccepted,
            IsSettled = this.IsSettled,
        };
    }
}


public interface IHodlInvoicePayer
{
    public bool AcceptingHodlInvoice(HodlInvoice invoice);
    public void OnHodlInvoiceSettled(HodlInvoice invoice);
}

public interface IHodlInvoiceIssuer
{
    public void OnHodlInvoiceAccepted(HodlInvoice invoice);
    public void OnHodlInvoiceSettled(HodlInvoice invoice);
}

public interface IHodlInvoiceSettler
{
    public bool SettlingHodlInvoice(HodlInvoice invoice);
    public void OnHodlInvoiceAccepted(HodlInvoice invoice);
}
