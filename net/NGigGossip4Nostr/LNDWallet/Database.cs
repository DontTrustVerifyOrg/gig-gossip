using System;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace LNDWallet;


public class Address
{
    [Key]
    public string address { get; set; }
    public string pubkey { get; set; }
    public long txfee { get; set; }
}

public class Invoice
{
    [Key]
    public string hash { get; set; }
    public string pubkey { get; set; }
    public string paymentreq { get; set; }
    public long satoshis { get; set; }
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

