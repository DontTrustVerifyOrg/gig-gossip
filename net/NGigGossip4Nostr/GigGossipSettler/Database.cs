using System;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using CryptoToolkit;
using System.Security.Cryptography.X509Certificates;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;

namespace GigGossipSettler;

[Serializable]
public class SystemLogEntry
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Key]
    public required Guid EntryId { get; set; }

    public required string PublicKey { get; set; }
    public required DateTime DateTime { get; set; }
    public required TraceEventType EventType { get; set; }
    public required string Message { get; set; }
    public required string Exception { get; set; }
}

/// <summary>
/// This class represents a property of the subject of the certificate granted by Settlers Certification Authority.
/// </summary>
public class UserProperty
{
    /// <summary>
    /// The unique identifier of the property.
    /// </summary>
    [Key]
    public required Guid PropertyId { get; set; }

    /// <summary>
    /// The public key of the subject.
    /// </summary>
    public required string PublicKey { get; set; }

    /// <summary>
    /// The name of the property.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The public value of the property.
    /// </summary>
    public required byte[] Value { get; set; }

    /// <summary>
    /// The secret value of the property.
    /// </summary>
    public required byte[] Secret { get; set; }

    /// <summary>
    /// The validity till date of the property.
    /// </summary>
    public required DateTime ValidTill { get; set; }

    /// <summary>
    /// Represents whether the property is revoked.
    /// </summary>
    public required bool IsRevoked { get; set; }
}

/// <summary>
/// Certificate to property (1 to many) relationship.
/// </summary>
[PrimaryKey(nameof(Kind), nameof(CertificateId), nameof(PropertyId))]
public class CertificateProperty
{
    /// <summary>
    /// kind of the certificate.
    /// </summary>
    [Column(Order = 1)]
    public required string Kind { get; set; }

    /// <summary>
    /// The identifier of the certificate.
    /// </summary>
    [Column(Order = 2)]
    public required Guid CertificateId { get; set; }

    /// <summary>
    /// The unique identifier of the property.
    /// </summary>
    [Column(Order = 3)]
    public required Guid PropertyId { get; set; }
}

/// <summary>
/// Represents a certificate issued for the Subject by Certification Authority.
/// </summary>
[PrimaryKey(nameof(Kind), nameof(CertificateId))]
public class UserCertificate
{
    /// <summary>
    /// kind of the certificate.
    /// </summary>
    [Column(Order = 1)]
    public required string Kind { get; set; }

    /// <summary>
    /// The identifier of the certificate.
    /// </summary>
    [Column(Order = 2)]
    public required Guid CertificateId { get; set; }
 
     /// <summary>
    /// The public ke of the subject.
    /// </summary>
    public required string PublicKey { get; set; }

    /// <summary>
    /// Represent whether the certificate is revoked.
    /// </summary>
    public required bool IsRevoked { get; set; }
}

/// <summary>
/// The preimage.
/// </summary>
public class InvoicePreimage
{
    /// <summary>
    /// The hash of the preimage.
    /// </summary>
    [Key]
    public required string PaymentHash { get; set; }

    /// <summary>
    /// The ID of the gig-job.
    /// </summary>
    public required Guid SignedRequestPayloadId { get; set; }

    /// <summary>
    /// The public key of the replier.
    /// </summary>
    public required string ReplierPublicKey { get; set; }

    /// <summary>
    /// The public key of the subject.
    /// </summary>
    public required string PublicKey { get; set; }

    /// <summary>
    /// The preimage for the hash.
    /// </summary>
    public required string Preimage { get; set; }

    /// <summary>
    /// Represents whether the preimage is revealed and can be returned to the subject.
    /// </summary>
    public required bool IsRevealed { get; set; }
}

/// <summary>
/// User traces.
/// </summary>
[PrimaryKey(nameof(PublicKey), nameof(Name))]
public class UserTraceProperty
{
    /// <summary>
    /// The public key of the subject.
    /// </summary>
    [Column(Order = 1)]
    public required string PublicKey { get; set; }

    /// <summary>
    /// The name of the property.
    /// </summary>
    [Column(Order = 2)]
    public required string Name { get; set; }


    /// <summary>
    /// The public value of the property.
    /// </summary>
    public required byte[] Value { get; set; }
}

/// <summary>
/// The status of a gig.
/// </summary>
public enum GigStatus
{
    Open = 0,
    Accepted = 1,
    Cancelled = 2,
    Disuputed = 3,
    Completed = 4,
}

public enum GigSubStatus
{
    None = 0,
    AcceptedByNetwork = 1,
    AcceptedByReply = 2,
}

/// <summary>
/// A gig job
/// </summary>
[PrimaryKey(nameof(SignedRequestPayloadId), nameof(ReplierCertificateId))]
public class Gig
{
    /// <summary>
    /// The Id of the gig.
    /// </summary>
    [Column(Order = 1)]
    public required Guid SignedRequestPayloadId { get; set; }

    /// <summary>
    /// The public key of the replier.
    /// </summary>
    [Column(Order = 2)]
    public required Guid ReplierCertificateId { get; set; }

    /// <summary>
    /// The symmetric key.
    /// </summary>
    public required string SymmetricKey { get; set; }

    /// <summary>
    /// The payment hash.
    /// </summary>
    public required string PaymentHash { get; set; }

    /// <summary>
    /// The network payment hash.
    /// </summary>
    public required string NetworkPaymentHash { get; set; }

    /// <summary>
    /// The status of the gig.
    /// </summary>
    public required GigStatus Status { get; set; }

    /// <summary>
    /// The sub status of the gig.
    /// </summary>
    public required GigSubStatus SubStatus { get; set; }

    /// <summary>
    /// The dispute deadline for the gig.
    /// </summary>
    public required DateTime DisputeDeadline { get; set; }
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
    public required Guid TokenId { get; set; }

    /// <summary>
    /// The public key of the subject.
    /// </summary>
    public required string PublicKey { get; set; }
}

/// <summary>
/// This class establishes a context for communicating with a database using Entity Framework Core.
/// </summary>
public class SettlerContext : DbContext
{
    /// <summary>
    /// The connection string for the database.
    /// </summary>
    string connectionString;

    /// <summary>
    /// Creates a new instance of the SettlerContext class.
    /// </summary>
    /// <param name="connectionString">The connection string for the database.</param>
    public SettlerContext(string connectionString)
    {
        this.connectionString = connectionString;
    }

    /// <summary>
    /// Tokens table.
    /// </summary>
    public DbSet<Token> Tokens { get; set; }
 
     /// <summary>
    /// Preimages table.
    /// </summary>
    public DbSet<InvoicePreimage> Preimages { get; set; }

    /// <summary>
    /// Gigs table.
    /// </summary>
    public DbSet<Gig> Gigs { get; set; }

    /// <summary>
    /// User properties table.
    /// </summary>
    public DbSet<UserProperty> UserProperties { get; set; }
    /// <summary>
    /// Certificate->UserProperty 1-to-many table.
    /// </summary>
    public DbSet<CertificateProperty> CertificateProperties { get; set; }
    
    /// <summary>
    /// UserCertificates table.
    /// </summary>
    public DbSet<UserCertificate> UserCertificates { get; set; }

    /// <summary>
    /// UserCertificates table.
    /// </summary>
    public DbSet<UserTraceProperty> UserTraceProperties { get; set; }

    public DbSet<SystemLogEntry> SystemLogEntries { get; set; }

    /// <summary>
    /// Configures the context.
    /// </summary>
    /// <param name="optionsBuilder"></param>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(connectionString).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    dynamic Type2DbSet(object obj)
    {
        if (obj == null)
            throw new ArgumentNullException();

        if (obj is Token)
            return this.Tokens;
        else if (obj is InvoicePreimage)
            return this.Preimages;
        else if (obj is Gig)
            return this.Gigs;
        else if (obj is UserProperty)
            return this.UserProperties;
        else if (obj is CertificateProperty)
            return this.CertificateProperties;
        else if (obj is UserCertificate)
            return this.UserCertificates;
        else if (obj is UserTraceProperty)
            return this.UserTraceProperties;
        else if (obj is SystemLogEntry)
            return this.SystemLogEntries;
        throw new InvalidOperationException();
    }

    public void SaveObject<T>(T obj)
    {
        this.Type2DbSet(obj!).Update(obj!);
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
