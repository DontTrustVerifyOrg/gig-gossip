using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using CryptoToolkit;
using Microsoft.EntityFrameworkCore;
using NBitcoin.Protocol;
using NNostr.Client;

namespace NGigGossip4Nostr;

[PrimaryKey(nameof(PublicKey), nameof(PayloadId), nameof(ContactPublicKey))]
public class BroadcastHistoryRow
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public required string PublicKey { get; set; }

    [Column(Order = 2)]
    public required Guid PayloadId { get; set; }

    [Column(Order = 3)]
    public required string ContactPublicKey { get; set; }
}

[PrimaryKey(nameof(PublicKey), nameof(PayloadId), nameof(ContactPublicKey))]
public class BroadcastCancelHistoryRow
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public required string PublicKey { get; set; }

    [Column(Order = 2)]
    public required Guid PayloadId { get; set; }

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

    public required Guid PayloadId { get; set; }

    public required Guid ReplierCertificateId { get; set; }
    public required byte[] TheReplyPayload { get; set; }
    public required string NetworkInvoice { get; set; }
    public required byte[] DecodedNetworkInvoice { get; set; }
    public required string ReplyInvoice { get; set; }
    public required byte[] DecodedReplyInvoice { get; set; }
}

[PrimaryKey(nameof(PublicKey), nameof(SettlerServiceUri), nameof(PayloadId))]
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
    public required Guid PayloadId { get; set; }

    public required byte[] SignedSettlementPromise { get; set; }
    public required string NetworkInvoice { get; set; }

    public required byte[] EncryptedReplyPayload { get; set; }

    public required string ReplyInvoice { get; set; }
    public required byte[] DecodedNetworkInvoice { get; set; }
    public required byte[] DecodedReplyInvoice { get; set; }
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

    public required string InvoiceState { get; set; }
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

    public required string PaymentStatus { get; set; }
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

[PrimaryKey(nameof(PublicKey), nameof(PayloadId), nameof(ReplierCertificateId))]
public class MonitoredSymmetricKeyRow
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public required string PublicKey { get; set; }

    [Column(Order = 2)]
    public required Guid PayloadId { get; set; }

    [Column(Order = 3)]
    public required Guid ReplierCertificateId { get; set; }

    public required Guid SenderCertificateId { get; set; }

    public required Uri ServiceUri { get; set; }
    public string? SymmetricKey { get; set; }
    public required byte[] Data { get; set; }
}

[PrimaryKey(nameof(PublicKey), nameof(MessageId))]
public class MessageDoneRow
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public required string PublicKey { get; set; }

    [Column(Order = 2)]
    public required string MessageId { get; set; }
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
    public required string Relay { get; set; }
    public required string Petname { get; set; }
}

/// <summary>
/// Context class for interaction with database.
/// </summary>
public class GigGossipNodeContext : DbContext
{
    /// <summary>
    /// Connection string to the database.
    /// </summary>
    string connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="WaletContext"/> class.
    /// </summary>
    /// <param name="connectionString">The connection string to connect to the database.</param>
    public GigGossipNodeContext(string connectionString)
    {
        this.connectionString = connectionString;
    }

    public DbSet<BroadcastHistoryRow> BroadcastHistory { get; set; }
    public DbSet<BroadcastCancelHistoryRow> BroadcastCancelHistory { get; set; }
    public DbSet<ReplyPayloadRow> ReplyPayloads { get; set; }
    public DbSet<AcceptedBroadcastRow> AcceptedBroadcasts { get; set; }
    public DbSet<MonitoredInvoiceRow> MonitoredInvoices { get; set; }
    public DbSet<MonitoredPaymentRow> MonitoredPayments { get; set; }
    public DbSet<MonitoredPreimageRow> MonitoredPreimages { get; set; }
    public DbSet<MonitoredSymmetricKeyRow> MonitoredSymmetricKeys { get; set; }
    public DbSet<MessageDoneRow> MessagesDone { get; set; }
    public DbSet<NostrContact> NostrContacts { get; set; }

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
        else if (obj is MonitoredSymmetricKeyRow)
            return this.MonitoredSymmetricKeys;
        else if (obj is MessageDoneRow)
            return this.MessagesDone;
        else if (obj is NostrContact)
            return this.NostrContacts;

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

    public void DeleteObjectRange<T>(IEnumerable<T> range)
    {
        if (range.Count() == 0)
            return;
        this.Type2DbSet(range.First()!).RemoveRange(range);
        this.SaveChanges();
        this.ChangeTracker.Clear();
    }

    public void AddObject<T>(T obj)
    {
        this.Type2DbSet(obj!).Add(obj);
        this.SaveChanges();

        this.ChangeTracker.Clear();
    }

    public bool TryAddObject<T>(T obj)
    {
        this.Type2DbSet(obj!).Add(obj);
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

    public void AddObjectRange<T>(IEnumerable<T> range)
    {
        if (range.Count() == 0)
            return;
        this.Type2DbSet(range.First()!).AddRange(range);
        this.SaveChanges();
        this.ChangeTracker.Clear();
    }

    public bool TryAddObjectRange<T>(IEnumerable<T> range)
    {
        if (range.Count() == 0)
            return true;
        this.Type2DbSet(range.First()!).AddRange(range);
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