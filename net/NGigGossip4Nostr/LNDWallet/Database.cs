using System;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace LNDWallet;

/// <summary>
/// Represents a Bitcoin address.
/// </summary>
public class Address
{
    /// <summary>
    /// The Bitcoin address.
    /// </summary>
    [Key]
    public string address { get; set; }

    /// <summary>
    /// The public key of the account associated with the Bitcoin address.
    /// </summary>
    public string pubkey { get; set; }

    /// <summary>
    /// The transaction fee charged on topups made on this address.
    /// </summary>
    public long txfee { get; set; }
}

/// <summary>
/// Class representing an Lightning Invoice. 
/// </summary>
public class Invoice
{
    /// <summary>
    /// The invoice payment hash.
    /// </summary>
    [Key]
    public string hash { get; set; }

    /// <summary>
    /// Public key of the account associated with the invoice.
    /// </summary>
    public string pubkey { get; set; }

    /// <summary>
    /// The payment request associated with the invoice.
    /// </summary>
    public string paymentreq { get; set; }

    /// <summary>
    /// The amount of satoshis on the invoice.
    /// </summary>
    public long satoshis { get; set; }

    /// <summary>
    /// The current state of the invoice.
    /// </summary>
    public InvoiceState state { get; set; }
    public long txfee { get; set; }
    public bool ishodl { get; set; }
    public bool isselfmanaged { get; set; }
    public byte[]? preimage { get; set; }
}

public class Payment
{
    [Key]
    public string hash { get; set; }
    public string pubkey { get; set; }
    public long satoshis { get; set; }
    public PaymentStatus status { get; set; }
    public long paymentfee { get; set; }
    public long txfee { get; set; }
    public bool isselfmanaged { get; set; }
}

public class Payout
{
    [Key]
    public Guid id { get; set; }
    public string pubkey { get; set; }
    public string address { get; set; }
    public bool ispending { get; set; }
    public long satoshis { get; set; }
    public long txfee { get; set; }
    public string tx { get; set; }
}

public class Token
{
    [Key]
    public Guid id { get; set; }
    public string pubkey { get; set; }
}

public class WaletContext : DbContext
{
    string connectionString;

    public WaletContext(string connectionString)
    {
        this.connectionString = connectionString;
    }

    public DbSet<Address> FundingAddresses { get; set; }
    public DbSet<Payout> Payouts { get; set; }
    public DbSet<Invoice> Invoices { get; set; }
    public DbSet<Payment> Payments { get; set; }
    public DbSet<Token> Tokens { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(connectionString);
    }

}

