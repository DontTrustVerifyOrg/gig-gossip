using System;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Data.Common;

using Microsoft.EntityFrameworkCore.Metadata.Internal;
using static Google.Protobuf.WellKnownTypes.Field.Types;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Concurrent;

namespace LNDWallet;

public enum DBProvider
{
    Sqlite=1,
    SQLServer,
    PostgreSQL,
    MySQL,
    Oracle,
}

public enum InternalPaymentStatus
{
    InFlight = 1,
    Succeeded = 2,
}

public enum ExternalPaymentStatus
{
    InFlight = 1,
    Succeeded = 2,
    Initiated = 4,
}

/// <summary>
/// Represents a Bitcoin address.
/// </summary>
public class TopupAddress
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

}

public enum TackingIndexId
{
    AddInvoice,
    SettleInvoice,
    StartTransactions,
}

public class TrackingIndex
{
    [Key]
    public required TackingIndexId Id { get; set; }

    public required ulong Value { get; set; }
}

/// <summary>
/// Class representing an Lightning Invoice. 
/// </summary>
public class ClassicInvoice
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

}

/// <summary>
/// Class representing an Lightning Invoice. 
/// </summary>
public class HodlInvoice
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

}

/// <summary>
/// Represents a Payment for the invoice.
/// </summary>
public class InternalPayment
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
    /// Current status of the payment.
    /// </summary>
    public required InternalPaymentStatus Status { get; set; }

    public required long Satoshis { get; set; }

    /// <summary>
    /// Fee charged for this payment. 
    /// </summary>
    public required long PaymentFee { get; set; }

    public required DateTime CreationTime { get; set; }

    public required string Currency { get; set; }   
    public required long Amount { get; set; }
}

/// <summary>
/// Represents a Payment for the invoice.
/// </summary>
public class ExternalPayment
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
    /// Current status of the payment.
    /// </summary>
    public required ExternalPaymentStatus Status { get; set; }
}



/// <summary>
/// Represents a Payment for the invoice.
/// </summary>
public class FailedPayment
{
    [Key]
    public required Guid Id { get; set; }

    /// <summary>
    /// Payment hash of the invoice being payed.
    /// </summary>
    public required string PaymentHash { get; set; }

    /// <summary>
    /// Public key of the account associated with the invoice.
    /// </summary>
    public required string PublicKey { get; set; }

    public required DateTime DateTime { get; set; }

    public required PaymentFailureReason FailureReason { get; set; }

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
    public required long PayoutFee { get; set; }

    /// <summary>
    /// Bitcoin transaction identifier for the payout.
    /// </summary>
    public string? Tx { get; set; }

    public required DateTime CreationTime { get; set; }
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
    public required Guid Id { get; set; }

    /// <summary>
    /// The public key of the account.
    /// </summary>
    public required string PublicKey { get; set; }
}

/// <summary>
/// Context class for interaction with database.
/// </summary>
public class WaletContext : DbContext
{
    DBProvider provider;
    /// <summary>
    /// Connection string to the database.
    /// </summary>
    string connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="WaletContext"/> class.
    /// </summary>
    /// <param name="connectionString">The connection string to connect to the database.</param>
    public WaletContext(DBProvider provider, string connectionString)
    {
        this.provider = provider;
        this.connectionString = connectionString;
    }

    public Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction BEGIN_TRANSACTION(System.Data.IsolationLevel isolationLevel= System.Data.IsolationLevel.ReadCommitted)
    {
        if (provider == DBProvider.Sqlite)
            return new NullTransaction();
        else
            return this.Database.BeginTransaction(isolationLevel);
    }

    public DbSet<TrackingIndex> TrackingIndexes { get; set; }

    /// <summary>
    /// FundingAddresses table.
    /// </summary>
    public DbSet<TopupAddress> TopupAddresses { get; set; }

    /// <summary>
    /// Payouts table.
    /// </summary>
    public DbSet<Reserve> Reserves { get; set; }

    /// <summary>
    /// Payouts table.
    /// </summary>
    public DbSet<Payout> Payouts { get; set; }

    /// <summary>
    /// Invoices tables.
    /// </summary>
    public DbSet<ClassicInvoice> ClassicInvoices { get; set; }
    public DbSet<HodlInvoice> HodlInvoices { get; set; }

    /// <summary>
    /// Payments tables.
    /// </summary>
    public DbSet<InternalPayment> InternalPayments { get; set; }
    public DbSet<ExternalPayment> ExternalPayments { get; set; }
    public DbSet<FailedPayment> FailedPayments { get; set; }

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
        if (provider == DBProvider.Sqlite)
            optionsBuilder.UseSqlite(connectionString);
        else if (provider == DBProvider.SQLServer)
            optionsBuilder.UseSqlServer(connectionString);
        else if (provider == DBProvider.PostgreSQL)
            optionsBuilder.UseNpgsql(connectionString);
        else if (provider == DBProvider.MySQL)
            optionsBuilder.UseMySql(connectionString,ServerVersion.AutoDetect(connectionString));
        else if (provider == DBProvider.Oracle)
            optionsBuilder.UseOracle(connectionString);
        else
            throw new NotImplementedException();

        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    public WaletContext UPDATE<T>(T obj) where T : class
    {
        var set = this.Set<T>();
        QueryCacheExtensions.ClearCache<T>(set);
        set.Update(obj);
        return this;
    }

    public WaletContext UPDATE_IF_EXISTS<T>(IQueryable<T> qs, Func<T, T> update) where T : class
    {
        var e = qs.FirstOrDefault();
        if (e != null)
        {
            var obj = update(e);
            UPDATE(obj);
        }
        return this;
    }

    public WaletContext INSERT<T>(T obj) where T:class
    {
        var set = this.Set<T>();
        QueryCacheExtensions.ClearCache<T>(set);
        set.Add(obj);
        return this;
    }

    public WaletContext DELETE<T>(T obj) where T : class
    {
        var set = this.Set<T>();
        QueryCacheExtensions.ClearCache<T>(set);
        set.Remove(obj);
        return this;
    }

    public WaletContext DELETE_IF_EXISTS<T>(IQueryable<T> qs) where T : class
    {
        var e = qs.FirstOrDefault();
        if (e != null)
            DELETE(e);
        return this;
    }

    public void SAVE()
    {
        this.SaveChanges();
        this.ChangeTracker.Clear();
    }

}


public class NullTransaction : Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction
{
    public Guid TransactionId => Guid.NewGuid();

    public void Commit()
    {
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
    }

    public void Dispose()
    {
    }

    public async ValueTask DisposeAsync()
    {
    }

    public void Rollback()
    {
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
    }
}
