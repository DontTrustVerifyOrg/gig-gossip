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
    public required string BitcoinAddress { get; set; }

    /// <summary>
    /// The public key of the account associated with the Bitcoin address.
    /// </summary>
    public required string PublicKey { get; set; }

    /// <summary>
    /// The transaction fee charged on topups made on this address.
    /// </summary>
    public required long TxFee { get; set; }
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
    public required string PaymentHash { get; set; }

    /// <summary>
    /// Public key of the account associated with the invoice.
    /// </summary>
    public required string PublicKey { get; set; }

    /// <summary>
    /// The payment request associated with the invoice.
    /// </summary>
    public required string PaymentRequest { get; set; }

    /// <summary>
    /// The amount of satoshis on the invoice.
    /// </summary>
    public required long Satoshis { get; set; }

    /// <summary>
    /// The current state of the invoice.
    /// </summary>
    public required InvoiceState State { get; set; }

    /// <summary>
    /// The transaction fee charged on succesfull payment to this invoice.
    /// </summary>
    public required long TxFee { get; set; }

    /// <summary>
    /// Indicates if the invoice is a HODL type Lightning Invoice
    /// </summary>
    public required bool IsHodlInvoice { get; set; }

    /// <summary>
    /// Indicates if the invoice is self-managed - meaning the payment done to this invoice is done via account that is managing the same LND node. In this case there is no real Lightning Network payment but the system is managing this in the database only. 
    /// </summary>
    public required bool IsSelfManaged { get; set; }

    /// <summary>
    /// The preimage for the invoice, revealed for HODL invoices when settled.
    /// </summary>
    public byte[]? Preimage { get; set; }
}


/// <summary>
/// Represents a Payment for the invoice.
/// </summary>
public class Payment
{

    /// <summary>
    /// Payment hash of the invoice being payed.
    /// </summary>
    [Key]
    public required string PaymentHash { get; set; }

    /// <summary>
    /// Public key of the account associated with the invoice.
    /// </summary>
    public required string PublicKey { get; set; }

    /// <summary>
    /// Amount of satoshis on the payment.
    /// </summary>
    public required long Satoshis { get; set; }

    /// <summary>
    /// Current status of the payment.
    /// </summary>
    public required PaymentStatus Status { get; set; }

    /// <summary>
    /// Lightning network fee charged for this payment. 0 for self-managed payments
    /// </summary>
    public required long PaymentFee { get; set; }

    /// <summary>
    /// Transaction fee for the payment charged by the system.
    /// </summary>
    public required long TxFee { get; set; }

    /// <summary>
    /// Indicates if the payments is self-managed - meaning the payment done to this invoice is done via account that is managing the same LND node. In this case there is no real Lightning Network payment but the system is managing this in the database only. 
    /// </summary>
    public required bool IsSelfManaged { get; set; }
}


public enum PayoutState
{
    Open = 0,
    Sending = 1,
    Sent = 2,
}


/// <summary>
/// Represents a Payout from the account to external Bitcoin address.
/// </summary>
public class Payout
{
    /// <summary>
    /// Unique identifier for the Payout instance.
    /// </summary>
    [Key]
    public required Guid PayoutId { get; set; }

    /// <summary>
    /// Public key of the account associated with the invoice.
    /// </summary>
    public required string PublicKey { get; set; }

    /// <summary>
    /// Bitcoin address to which the payout was made.
    /// </summary>
    public required string BitcoinAddress { get; set; }

    /// <summary>
    /// Payout state.
    /// </summary>
    public required PayoutState State { get; set; }

    /// <summary>
    /// Amount of satoshis in the payout.
    /// </summary>
    public required long Satoshis { get; set; }

    /// <summary>
    /// Transaction fee for the payout.
    /// </summary>
    public required long TxFee { get; set; }

    /// <summary>
    /// Bitcoin transaction identifier for the payout.
    /// </summary>
    public string? Tx { get; set; }
}


/// <summary>
/// Represents a Reserved amount of funds.
/// </summary>
public class Reserve
{
    /// <summary>
    /// Unique identifier for the Reserve instance.
    /// </summary>
    [Key]
    public required Guid ReserveId { get; set; }

    /// <summary>
    /// Amount of satoshis in the reserve.
    /// </summary>
    public required long Satoshis { get; set; }
}

/// <summary>
/// This class represents an authorisation token.
/// </summary>
public class Token
{
    /// <summary>
    /// The ID of the token.
    /// </summary>
    [Key]
    public required Guid id { get; set; }

    /// <summary>
    /// The public key of the account.
    /// </summary>
    public required string pubkey { get; set; }
}

/// <summary>
/// Context class for interaction with database.
/// </summary>
public class WaletContext : DbContext
{
    /// <summary>
    /// Connection string to the database.
    /// </summary>
    string connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="WaletContext"/> class.
    /// </summary>
    /// <param name="connectionString">The connection string to connect to the database.</param>
    public WaletContext(string connectionString)
    {
        this.connectionString = connectionString;
    }

    /// <summary>
    /// FundingAddresses table.
    /// </summary>
    public DbSet<Address> FundingAddresses { get; set; }

    /// <summary>
    /// Payouts table.
    /// </summary>
    public DbSet<Reserve> Reserves { get; set; }

    /// <summary>
    /// Payouts table.
    /// </summary>
    public DbSet<Payout> Payouts { get; set; }

    /// <summary>
    /// Invoices table.
    /// </summary>
    public DbSet<Invoice> Invoices { get; set; }

    /// <summary>
    /// Payments table.
    /// </summary>
    public DbSet<Payment> Payments { get; set; }

    /// <summary>
    /// Tokens table.
    /// </summary>
    public DbSet<Token> Tokens { get; set; }

    /// <summary>
    /// Configures the context for use with a SQLite database.
    /// </summary>
    /// <param name="optionsBuilder">A builder used to create or modify options for this context.</param>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(connectionString).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }


    dynamic Type2DbSet(object obj)
    {
        if (obj == null)
            throw new ArgumentNullException();

        if (obj is Address)
            return this.FundingAddresses;
        else if (obj is Payout)
            return this.Payouts;
        else if (obj is Reserve)
            return this.Reserves;
        else if (obj is Invoice)
            return this.Invoices;
        else if (obj is Payment)
            return this.Payments;
        else if (obj is Token)
            return this.Tokens;

        throw new InvalidOperationException();
    }

    public void SaveObject<T>(T obj)
    {
        this.Type2DbSet(obj!).Update(obj);
        this.SaveChanges();
        this.ChangeTracker.Clear();
    }

    public void SaveObjectRange<T>(IEnumerable<T> range)
    {
        if (range.Count() == 0)
            return;
        this.Type2DbSet(range.First()!).UpdateRange(range);
        this.SaveChanges();
        this.ChangeTracker.Clear();
    }

    public void AddObject<T>(T obj)
    {
        this.Type2DbSet(obj!).Add(obj);
        this.SaveChanges();
        this.ChangeTracker.Clear();
    }

    public void AddObjectRange<T>(IEnumerable<T> range)
    {
        if (range.Count() == 0)
            return;
        this.Type2DbSet(range.First()!).AddRange(range);
        this.SaveChanges();
        this.ChangeTracker.Clear();
    }

}

