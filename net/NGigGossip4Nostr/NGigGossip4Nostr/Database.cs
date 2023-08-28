using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography.X509Certificates;
using CryptoToolkit;
using Microsoft.EntityFrameworkCore;
using NBitcoin.Protocol;
using NNostr.Client;

namespace NGigGossip4Nostr;


/// <summary>
/// Represents a certificate issued for the Subject by Certification Authority.
/// </summary>
[PrimaryKey(nameof(PublicKey), nameof(CertificateId))]
public class UserCertificate
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public required string PublicKey { get; set; }

    [Column(Order = 2)]
    public Guid CertificateId { get; set; }


    /// <summary>
    /// The certificate in byte array format.
    /// </summary>
    public required byte[] TheCertificate { get; set; }
}

[PrimaryKey(nameof(PublicKey), nameof(AskId))]
public class BroadcastPayloadRow
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public required string PublicKey { get; set; }

    [Column(Order = 2)]
    public Guid AskId { get; set; }

    public required byte[] TheBroadcastPayload { get; set; }
}

[PrimaryKey(nameof(PublicKey), nameof(AskId))]
public class POWBroadcastConditionsFrameRow
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public required string PublicKey { get; set; }

    [Column(Order = 2)]
    public Guid AskId { get; set; }

    public required byte[] ThePOWBroadcastConditionsFrame { get; set; }
}

[PrimaryKey(nameof(PublicKey), nameof(PayloadId))]
public class BroadcastCounterRow
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public required string PublicKey { get; set; }

    [Column(Order = 2)]
    public Guid PayloadId { get; set; }

    public required int Counter { get; set; }
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
    public Guid ReplyId { get; set; }

    public Guid PayloadId { get; set; }

    public string ReplierPublicKey { get; set; }
    public byte[] TheReplyPayload { get; set; }
    public string NetworkInvoice { get; set; }
    public byte[] DecodedNetworkInvoice { get; set; }
    public string ReplyInvoice { get; set; }
    public byte[] DecodedReplyInvoice { get; set; }
}

[PrimaryKey(nameof(PublicKey), nameof(SettlerServiceUri), nameof(PayloadId))]
public class AcceptedBroadcastRow
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public required string PublicKey { get; set; }

    [Column(Order = 1)]
    public Uri SettlerServiceUri { get; set; }

    [Column(Order = 2)]
    public Guid PayloadId { get; set; }

    public byte[] SignedSettlementPromise { get; set; }
    public string NetworkInvoice { get; set; }
    public byte[] EncryptedReplyPayload { get; set; }
}


[PrimaryKey(nameof(PublicKey), nameof(PaymentHash))]
public class MonitoredInvoiceRow
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public string PublicKey { get; set; }

    [Column(Order = 2)]
    public string PaymentHash { get; set; }

    public string InvoiceState { get; set; }
    public byte[] Data { get; set; }
}

[PrimaryKey(nameof(PublicKey), nameof(PaymentHash))]
public class MonitoredPaymentRow
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public string PublicKey { get; set; }

    [Column(Order = 2)]
    public string PaymentHash { get; set; }

    public string PaymentStatus { get; set; }
    public byte[] Data { get; set; }
}

[PrimaryKey(nameof(PublicKey), nameof(PaymentHash))]
public class MonitoredPreimageRow
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public string PublicKey { get; set; }

    [Column(Order = 2)]
    public string PaymentHash { get; set; }

    public Uri ServiceUri { get; set; }
    public string? Preimage { get; set; }
}

[PrimaryKey(nameof(PublicKey), nameof(PayloadId))]
public class MonitoredSymmetricKeyRow
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public string PublicKey { get; set; }

    [Column(Order = 2)]
    public Guid PayloadId { get; set; }

    public Uri ServiceUri { get; set; }
    public string? SymmetricKey { get; set; }
    public byte[] Data { get; set; }
}

[PrimaryKey(nameof(PublicKey), nameof(MessageId))]
public class MessageDoneRow
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public string PublicKey { get; set; }

    [Column(Order = 2)]
    public string MessageId { get; set; }
}

[PrimaryKey(nameof(PublicKey), nameof(ContactPublicKey))]
public class NostrContact
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public string PublicKey { get; set; }

    public string ContactPublicKey { get; set; }
    public string Relay { get; set; }
    public string Petname { get; set; }
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

    public DbSet<UserCertificate> UserCertificates { get; set; }
    public DbSet<BroadcastPayloadRow> BroadcastPayloadsByAskId { get; set; }
    public DbSet<POWBroadcastConditionsFrameRow> POWBroadcastConditionsFrameRowByAskId { get; set; }
    public DbSet<BroadcastCounterRow> BroadcastCounters { get; set; }
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
        if (obj is UserCertificate)
            return this.UserCertificates;
        else if (obj is BroadcastPayloadRow)
            return this.BroadcastPayloadsByAskId;
        else if (obj is POWBroadcastConditionsFrameRow)
            return this.POWBroadcastConditionsFrameRowByAskId;
        else if (obj is BroadcastCounterRow)
            return this.BroadcastCounters;
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
        this.Type2DbSet(obj).Update(obj);
        this.SaveChanges();
        this.ChangeTracker.Clear();
    }

    public void SaveObjectRange<T>(IEnumerable<T> range)
    {
        if (range.Count() == 0)
            return;
        this.Type2DbSet(range.First()).UpdateRange(range);
        this.SaveChanges();
        this.ChangeTracker.Clear();
    }

    public void AddObject<T>(T obj)
    {
        this.Type2DbSet(obj).Add(obj);
        this.SaveChanges();
        this.ChangeTracker.Clear();
    }

    public void AddObjectRange<T>(IEnumerable<T> range)
    {
        if (range.Count() == 0)
            return;
        this.Type2DbSet(range.First()).AddRange(range);
        this.SaveChanges();
        this.ChangeTracker.Clear();
    }

}