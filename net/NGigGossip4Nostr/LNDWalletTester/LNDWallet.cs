using System;
using NBitcoin.Secp256k1;
using CryptoToolkit;
using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using System.Reflection.Metadata;
using System.ComponentModel.DataAnnotations;

namespace LNDWallet
{
    public class User
    {
        [Key]
        public string pubkey { get; set; }
    }

    public class WaletContext : DbContext
    {
        string connectionString;
        public WaletContext(string connectionString)
        {
            this.connectionString = connectionString;
        }

        public DbSet<User> Users { get; set; }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite(connectionString);
        }
    }


    public class LNDWalletManager
	{
        WaletContext waletContext;
        public LNDWalletManager(string connectionString)
		{
            this.waletContext = new WaletContext(connectionString);
            waletContext.Database.EnsureCreated();
        }

        public void Signup(ECXOnlyPubKey pubkey)
        {
            waletContext.Users.Add(new User() { pubkey = pubkey.AsHex() });
            waletContext.SaveChanges();
        }

        public bool PubkeyIsSignedup(ECXOnlyPubKey pubkey)
        {
            return (from user in waletContext.Users select user).Count() > 0;
        }

    }
}

