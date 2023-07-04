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

    public HodlInvoice DeepCopy()
    {
        return new HodlInvoice(this.PaymentHash.ToArray(), this.Amount, this.ValidTill, this.Id)
        {
            Preimage = (this.Preimage == null) ? null : this.Preimage.ToArray(),
            IsAccepted = this.IsAccepted,
            IsSettled = this.IsSettled,
        };
    }
}

public abstract class NamedEntity
{
    private static readonly Dictionary<string, NamedEntity> ENTITY_BY_NAME = new Dictionary<string, NamedEntity>();

    public string Name;
    public NamedEntity(string name)
    {
        this.Name = name;
        ENTITY_BY_NAME[name] = this;
    }

    public static NamedEntity GetByName(string name)
    {
        if (ENTITY_BY_NAME.ContainsKey(name))
            return ENTITY_BY_NAME[name];
        throw new ArgumentException("Entity not found");
    }
}


public interface IHodlInvoicePayer 
{
    public bool OnHodlInvoiceAccepting(HodlInvoice invoice);
    public void OnHodlInvoiceSettled(HodlInvoice invoice);
}

public interface IHodlInvoiceIssuer
{
}

public interface IHodlInvoiceSettler
{
    public void OnHodlInvoicePayed(HodlInvoice invoice);
    public void SettleHodlInvoice(HodlInvoice invoice);
}
