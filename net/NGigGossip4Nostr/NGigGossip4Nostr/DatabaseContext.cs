using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Transactions;
using GigGossip;
using GigGossipSettlerAPIClient;
using GigLNDWalletAPIClient;
using Microsoft.EntityFrameworkCore;
using NBitcoin.Protocol;

namespace NGigGossip4Nostr;

public enum DBProvider
{
    Sqlite = 1,
    SQLServer,
}

[PrimaryKey(nameof(PublicKey), nameof(SignedRequestPayloadId), nameof(ContactPublicKey))]
public class BroadcastHistoryRow
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public required string PublicKey { get; set; }

    [Column(Order = 2)]
    public required Guid SignedRequestPayloadId { get; set; }

    [Column(Order = 3)]
    public required string ContactPublicKey { get; set; }
}

[PrimaryKey(nameof(PublicKey), nameof(SignedRequestPayloadId), nameof(ContactPublicKey))]
public class BroadcastCancelHistoryRow
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public required string PublicKey { get; set; }

    [Column(Order = 2)]
    public required Guid SignedRequestPayloadId { get; set; }

    [Column(Order = 3)]
    public required string ContactPublicKey { get; set; }
}

[PrimaryKey(nameof(PublicKey), nameof(ReplyId))]
public class ReplyPayloadRow
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public required string PublicKey { get; set; }

    [Column(Order = 2)]
    public required Guid ReplyId { get; set; }

    public required Guid SignedRequestPayloadId { get; set; }

    public required Guid ReplierCertificateId { get; set; }
    public required byte[] TheReplyPayload { get; set; }
    public required string NetworkPaymentRequest { get; set; }
    public required byte[] DecodedNetworkInvoice { get; set; }
    public required string ReplyInvoice { get; set; }
    public required byte[] DecodedReplyInvoice { get; set; }
}

[PrimaryKey(nameof(PublicKey), nameof(SettlerServiceUri), nameof(SignedRequestPayloadId), nameof(ReplyInvoiceHash))]
public class AcceptedBroadcastRow
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public required string PublicKey { get; set; }

    [Column(Order = 2)]
    public required Uri SettlerServiceUri { get; set; }

    [Column(Order = 3)]
    public required Guid SignedRequestPayloadId { get; set; }

    [Column(Order = 4)]
    public required string ReplyInvoiceHash { get; set; }

    public required Guid ReplierCertificateId { get; set; }

    public required byte[] SignedSettlementPromise { get; set; }
    public required string NetworkPaymentRequest { get; set; }

    public required byte[] EncryptedReplyPayload { get; set; }

    public required string ReplyInvoice { get; set; }
    public required byte[] DecodedNetworkInvoice { get; set; }
    public required byte[] DecodedReplyInvoice { get; set; }

    public required bool Cancelled { get; set; }

    public required byte[] BroadcastFrame { get; set; }

}


[PrimaryKey(nameof(PublicKey), nameof(PaymentHash))]
public class MonitoredInvoiceRow
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public required string PublicKey { get; set; }

    [Column(Order = 2)]
    public required string PaymentHash { get; set; }

    public required InvoiceState InvoiceState { get; set; }
    public required byte[] Data { get; set; }
}

[PrimaryKey(nameof(PublicKey), nameof(PaymentHash))]
public class MonitoredPaymentRow
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public required string PublicKey { get; set; }

    [Column(Order = 2)]
    public required string PaymentHash { get; set; }

    public required PaymentStatus PaymentStatus { get; set; }
    public required byte[] Data { get; set; }
}

[PrimaryKey(nameof(PublicKey), nameof(PaymentHash))]
public class MonitoredPreimageRow
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public required string PublicKey { get; set; }

    [Column(Order = 2)]
    public required string PaymentHash { get; set; }

    public required Uri ServiceUri { get; set; }
    public required string? Preimage { get; set; }
}

[PrimaryKey(nameof(PublicKey), nameof(SignedRequestPayloadId), nameof(ReplierCertificateId))]
public class MonitoredGigStatusRow
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public required string PublicKey { get; set; }

    [Column(Order = 2)]
    public required Guid SignedRequestPayloadId { get; set; }

    [Column(Order = 3)]
    public required Guid ReplierCertificateId { get; set; }

    public required Uri ServiceUri { get; set; }
    public required GigStatus Status { get; set; }
    public string? SymmetricKey { get; set; }
    public required byte[] Data { get; set; }
}

[PrimaryKey(nameof(PublicKey), nameof(MessageId))]
public class MessageTransactionRow
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public required string PublicKey { get; set; }

    [Column(Order = 2)]
    public required string MessageId { get; set; }

    [Column(Order = 3)]
    public required int EventKind { get; set; }

    [Column(Order = 4)]
    public required DateTime CreatedAt { get; set; }
}

[PrimaryKey(nameof(PublicKey), nameof(ContactPublicKey))]
public class NostrContact
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public required string PublicKey { get; set; }
    public required string ContactPublicKey { get; set; }
    public required DateTime LastSeen { get; set; }
}

/// <summary>
/// Context class for interaction with database.
/// </summary>
public class GigGossipNodeContext : DbContext
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
    public GigGossipNodeContext(DBProvider provider, string connectionString)
    {
        this.provider = provider;
        this.connectionString = connectionString;
    }

    public Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction BEGIN_TRANSACTION(System.Data.IsolationLevel isolationLevel = System.Data.IsolationLevel.ReadCommitted)
    {
        if (provider == DBProvider.Sqlite)
            return new NullTransaction(this);
        else
            return new ThreadSafeTransaction(this, isolationLevel);
    }

    public DbSet<BroadcastHistoryRow> BroadcastHistory { get; set; }
    public DbSet<BroadcastCancelHistoryRow> BroadcastCancelHistory { get; set; }
    public DbSet<ReplyPayloadRow> ReplyPayloads { get; set; }
    public DbSet<AcceptedBroadcastRow> AcceptedBroadcasts { get; set; }
    public DbSet<MonitoredInvoiceRow> MonitoredInvoices { get; set; }
    public DbSet<MonitoredPaymentRow> MonitoredPayments { get; set; }
    public DbSet<MonitoredPreimageRow> MonitoredPreimages { get; set; }
    public DbSet<MonitoredGigStatusRow> MonitoredGigStatuses { get; set; }
    public DbSet<MessageTransactionRow> MessageTransactions { get; set; }
    public DbSet<NostrContact> NostrContacts { get; set; }

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
        else
            throw new NotImplementedException();

        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    dynamic Type2DbSet(object obj)
    {
        if (obj == null)
            throw new ArgumentNullException();

        if (obj is BroadcastHistoryRow)
            return this.BroadcastHistory;
        else if (obj is BroadcastCancelHistoryRow)
            return this.BroadcastCancelHistory;
        else if (obj is ReplyPayloadRow)
            return this.ReplyPayloads;
        else if (obj is AcceptedBroadcastRow)
            return this.AcceptedBroadcasts;
        else if (obj is MonitoredInvoiceRow)
            return this.MonitoredInvoices;
        else if (obj is MonitoredPaymentRow)
            return this.MonitoredPayments;
        else if (obj is MonitoredPreimageRow)
            return this.MonitoredPreimages;
        else if (obj is MonitoredGigStatusRow)
            return this.MonitoredGigStatuses;
        else if (obj is MessageTransactionRow)
            return this.MessageTransactions;
        else if (obj is NostrContact)
            return this.NostrContacts;

        throw new InvalidOperationException();
    }

    public GigGossipNodeContext UPDATE<T>(T obj)
    {
        this.Type2DbSet(obj!).Update(obj);
        return this;
    }

    public GigGossipNodeContext UPDATE_IF_EXISTS<T>(IQueryable<T> qs, Func<T, T> update) where T : class
    {
        var e = qs.FirstOrDefault();
        if (e != null)
        {
            var obj = update(e);
            UPDATE(obj);
        }
        return this;
    }

    public GigGossipNodeContext INSERT<T>(T obj)
    {
        this.Type2DbSet(obj!).Add(obj);
        return this;
    }

    public GigGossipNodeContext DELETE<T>(T obj) where T : class
    {
        this.Type2DbSet(obj!).Remove(obj);
        return this;
    }

    public GigGossipNodeContext DELETE_IF_EXISTS<T>(IQueryable<T> qs) where T : class
    {
        var e = qs.FirstOrDefault();
        if (e != null)
            DELETE(e);
        return this;
    }

    public GigGossipNodeContext DELETE_RANGE<T>(IEnumerable<T> range)
    {
        if (range.Count() == 0)
            return this;
        this.Type2DbSet(range.First()!).RemoveRange(range);
        return this;
    }

    public void SAVE()
    {
        this.SaveChanges();
        this.ChangeTracker.Clear();
    }

    public void INSERT_OR_UPDATE_AND_SAVE<T>(T obj)
    {
        try
        {
            this.INSERT(obj).SAVE();
        }
        catch
        {
            this.UPDATE(obj).SAVE();
        }
    }

    public bool TRY_SAVE()
    {
        try
        {
            this.SaveChanges();
            return true;
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            return false;
            //failed to add
        }
        finally
        {
            this.ChangeTracker.Clear();
        }
    }
}

public class NullTransaction : Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction
{
    public Guid TransactionId => Guid.NewGuid();
    private bool open = true;
    private GigGossipNodeContext ctx;

    public NullTransaction(GigGossipNodeContext ctx)
    {
        this.ctx = ctx; 
        Monitor.Enter(this.ctx);
    }

    public void Commit()
    {
        if(!open)
            throw new InvalidOperationException();
        open = false;
        Monitor.Exit(this.ctx);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        Commit();
    }

    public void Dispose()
    {
        Rollback();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
    }

    public void Rollback()
    {
        if (open)
        {
            open = false;
            Monitor.Exit(this.ctx);
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        Rollback();
    }

    ~NullTransaction()
    {
        Rollback();
    }
}


public class ThreadSafeTransaction : Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction
{
    public Guid TransactionId => Guid.NewGuid();
    private bool open = true;
    private GigGossipNodeContext ctx;
    Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction TX;

    public ThreadSafeTransaction(GigGossipNodeContext ctx, System.Data.IsolationLevel isolationLevel)
    {
        this.ctx = ctx;
        Monitor.Enter(this.ctx);
        this.TX = ctx.Database.BeginTransaction(isolationLevel);
    }

    public void Commit()
    {
        if (!open)
            throw new InvalidOperationException();
        TX.Commit();
        open = false;
        Monitor.Exit(this.ctx);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        Commit();
    }

    public void Dispose()
    {
        Rollback();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
    }

    public void Rollback()
    {
        if (open)
        {
            TX.Rollback();
            open = false;
            Monitor.Exit(this.ctx);
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        Rollback();
    }

    ~ThreadSafeTransaction()
    {
        Rollback();
    }
}