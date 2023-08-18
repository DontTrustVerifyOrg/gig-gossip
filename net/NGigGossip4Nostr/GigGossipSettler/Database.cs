using System;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace GigGossipSettler;


public class UserProperty
{
    [Key]
    public Guid propid { get; set; }
    public string pubkey { get; set; }
    public string name { get; set; }
    public byte[] value { get; set; }
    public DateTime validtill { get; set; }
    public bool isrevoked { get; set; }
}

public class CertificateProperty
{
    [Key]
    public Guid certid { get; set; }
    public Guid propid { get; set; }
}

public class UserCertificate
{
    [Key]
    public Guid certid { get; set; }
    public string pubkey { get; set; }
    public byte[] certificate { get; set; }
    public bool isrevoked { get; set; }
}

public class Preimage
{
    [Key]
    public string hash { get; set; }
    public Guid tid { get; set; }
    public string pubkey { get; set; }
    public string preimage { get; set; }
    public bool revealed { get; set; }
}

public enum GigStatus
{
    Open = 0,
    Accepted = 1,
    Cancelled = 2,
    Disuputed = 3,
    Completed = 4,
}

public class Gig
{
    [Key]
    public Guid tid { get; set; }
    public string senderpubkey { get; set; }
    public string replierpubkey { get; set; }
    public string symmetrickey { get; set; }
    public string paymenthash { get; set; }
    public string networkhash { get; set; }
    public GigStatus status { get; set; }
    public DateTime disputedeadline { get; set; }
}

public class Token
{
    [Key]
    public Guid id { get; set; }
    public string pubkey { get; set; }
}

public class SettlerContext : DbContext
{
    string connectionString;

    public SettlerContext(string connectionString)
    {
        this.connectionString = connectionString;
    }

    public DbSet<Token> Tokens { get; set; }
    public DbSet<Preimage> Preimages { get; set; }
    public DbSet<Gig> Gigs { get; set; }
    public DbSet<UserProperty> UserProperties { get; set; }
    public DbSet<CertificateProperty> CertificateProperties { get; set; }
    public DbSet<UserCertificate> UserCertificates { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(connectionString);
    }

}
