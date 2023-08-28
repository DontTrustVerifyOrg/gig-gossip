using System;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace GigGossipSettler;

/// <summary>
/// This class represents a property of the subject of the certificate granted by Settlers Certification Authority.
/// </summary>
public class UserProperty
{
    /// <summary>
    /// The unique identifier of the property.
    /// </summary>
    [Key]
    public Guid PropertyId { get; set; }

    /// <summary>
    /// The public key of the subject.
    /// </summary>
    public string PublicKey { get; set; }

    /// <summary>
    /// The name of the property.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The value of the property.
    /// </summary>
    public byte[] Value { get; set; }

    /// <summary>
    /// The validity till date of the property.
    /// </summary>
    public DateTime ValidTill { get; set; }

    /// <summary>
    /// Represents whether the property is revoked.
    /// </summary>
    public bool IsRevoked { get; set; }
}

/// <summary>
/// Certificate to property (1 to many) relationship.
/// </summary>
public class CertificateProperty
{
    /// <summary>
    /// The unique identidier of the certificate.
    /// </summary>
    [Key]
    public Guid CertificateId { get; set; }

    /// <summary>
    /// The unique identifier of the property.
    /// </summary>
    public Guid PropertyId { get; set; }
}

/// <summary>
/// Represents a certificate issued for the Subject by Certification Authority.
/// </summary>
public class UserCertificate
{
    /// <summary>
    /// The unique identifier of the certificate.
    /// </summary>
    [Key]
    public Guid CertificateId { get; set; }
 
     /// <summary>
    /// The public ke of the subject.
    /// </summary>
    public required string PublicKey { get; set; }

    /// <summary>
    /// The certificate in byte array format.
    /// </summary>
    public required byte[] TheCertificate { get; set; }
    /// <summary>
    /// Represent whether the certificate is revoked.
    /// </summary>
    public bool IsRevoked { get; set; }
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
    /// The PayloadID of the gig-job.
    /// </summary>
    public Guid GigId { get; set; }

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
    public bool IsRevealed { get; set; }
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

/// <summary>
/// A gig job
/// </summary>
public class Gig
{
    /// <summary>
    /// The PayloadId of the gig.
    /// </summary>
    [Key]
    public Guid GigId { get; set; }

    /// <summary>
    /// The public key of the sender.
    /// </summary>
    public required string SenderPublicKey { get; set; }

    /// <summary>
    /// The public key of the replier.
    /// </summary>
    public required string ReplierPublicKey { get; set; }

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
    public GigStatus Status { get; set; }

    /// <summary>
    /// The dispute deadline for the gig.
    /// </summary>
    public DateTime DisputeDeadline { get; set; }
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
    public Guid TokenId { get; set; }

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
    /// Configures the context.
    /// </summary>
    /// <param name="optionsBuilder"></param>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(connectionString).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    dynamic Type2DbSet(object obj)
    {
        if(obj is Token)
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
