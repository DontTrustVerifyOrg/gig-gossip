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
    public Guid propid { get; set; }

    /// <summary>
    /// The public key of the subject.
    /// </summary>
    public string pubkey { get; set; }

    /// <summary>
    /// The name of the property.
    /// </summary>
    public string name { get; set; }

    /// <summary>
    /// The value of the property.
    /// </summary>
    public byte[] value { get; set; }

    /// <summary>
    /// The validity till date of the property.
    /// </summary>
    public DateTime validtill { get; set; }

    /// <summary>
    /// Represents whether the property is revoked.
    /// </summary>
    public bool isrevoked { get; set; }
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
    public Guid certid { get; set; }

    /// <summary>
    /// The unique identifier of the property.
    /// </summary>
    public Guid propid { get; set; }
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
    public Guid certid { get; set; }
 
     /// <summary>
    /// The public ke of the subject.
    /// </summary>
    public string pubkey { get; set; }

    /// <summary>
    /// The certificate in byte array format.
    /// </summary>
    public byte[] certificate { get; set; }
    /// <summary>
    /// Represent whether the certificate is revoked.
    /// </summary>
    public bool isrevoked { get; set; }
}

/// <summary>
/// The preimage.
/// </summary>
public class Preimage
{
    /// <summary>
    /// The hash of the preimage.
    /// </summary>
    [Key]
    public string hash { get; set; }

    /// <summary>
    /// The PayloadID of the gig-job.
    /// </summary>
    public Guid tid { get; set; }

    /// <summary>
    /// The public key of the subject.
    /// </summary>
    public string pubkey { get; set; }

    /// <summary>
    /// The preimage for the hash.
    /// </summary>
    public string preimage { get; set; }

    /// <summary>
    /// Represents whether the preimage is revealed and can be returned to the subject.
    /// </summary>
    public bool revealed { get; set; }
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
    public Guid tid { get; set; }

    /// <summary>
    /// The public key of the sender.
    /// </summary>
    public string senderpubkey { get; set; }

    /// <summary>
    /// The public key of the replier.
    /// </summary>
    public string replierpubkey { get; set; }

    /// <summary>
    /// The symmetric key.
    /// </summary>
    public string symmetrickey { get; set; }

    /// <summary>
    /// The payment hash.
    /// </summary>
    public string paymenthash { get; set; }

    /// <summary>
    /// The network payment hash.
    /// </summary>
    public string networkhash { get; set; }

    /// <summary>
    /// The status of the gig.
    /// </summary>
    public GigStatus status { get; set; }

    /// <summary>
    /// The dispute deadline for the gig.
    /// </summary>
    public DateTime disputedeadline { get; set; }
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
    public Guid id { get; set; }

    /// <summary>
    /// The public key of the subject.
    /// </summary>
    public string pubkey { get; set; }
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
    public DbSet<Preimage> Preimages { get; set; }

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
        optionsBuilder.UseSqlite(connectionString);
    }

}
