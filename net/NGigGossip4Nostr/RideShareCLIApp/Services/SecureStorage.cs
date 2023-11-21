using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NGigGossip4Nostr;

namespace RideShareCLIApp
{

    [PrimaryKey(nameof(Key))]
    public class SecureStorageRow
    {
        [Column(Order = 1)]
        public required string Key { get; set; }

        public required string Value { get; set; }
    }


    public class SecureStorage : DbContext
    {
        string connectionString;
        SemaphoreSlim protector = new(1, 1);

        public SecureStorage(string connectionString)
        {
            this.connectionString = connectionString;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite(connectionString).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        }

        public DbSet<SecureStorageRow> SecureStorageRows { get; set; }

        public static SecureStorage Default { get; private set; }

        public static void InitializeDefault(string connectionString)
        {
            SecureStorage.Default = new SecureStorage(connectionString);
            SecureStorage.Default.Database.EnsureCreated();
        }

        public async Task<string> GetAsync(string key, string? defaultvalue = null)
        {
            await protector.WaitAsync();
            try
            {
                var r = (from c in SecureStorageRows where c.Key == key select c).FirstOrDefault();
                return r != null ? r.Value : defaultvalue;
            }
            finally
            {
                protector.Release();
            }
        }

        public async Task SetAsync(string key, string value)
        {
            await protector.WaitAsync();
            try
            {
                var e = (from c in SecureStorageRows where c.Key == key select c).FirstOrDefault();
                if (e == null)
                    SecureStorageRows.Add(new SecureStorageRow { Key = key, Value = value });
                else
                {
                    e.Value = value;
                    SecureStorageRows.Update(e);
                }
                this.SaveChanges();
                this.ChangeTracker.Clear();
            }
            finally
            {
                protector.Release();
            }
        }
    }
}
