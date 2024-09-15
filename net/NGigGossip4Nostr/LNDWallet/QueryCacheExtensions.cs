using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace LNDWallet;

//https://juldhais.net/easy-way-to-implement-second-level-cache-in-entity-framework-6d7853f9a72b
public static class QueryCacheExtensions
{
    private static IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private static ConcurrentDictionary<string, HashSet<string>>_cashedContexts = new ();
    private const int AbsoluteExpirationSeconds = 3600;

    public static DbSet<T> ClearCache<T>(this DbSet<T> set) where T : class
    {
        lock (_cache)
        {
            if(_cashedContexts.TryRemove(set.EntityType.Name, out var keys))
            {
                foreach (var k in keys)
                    _cache.Remove(k);
            }
        }
        return set;
    }

    public static string GetCacheKey(IQueryable query)
    {
        var queryString = query.ToQueryString();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(queryString));
        return Convert.ToBase64String(hash);
    }

    public static List<T> FromCache<T, Q>(this IQueryable<T> query, DbSet<Q> set) where Q : class
    {
        Lazy<List<T>> result;
        lock (_cache)
        {
            var key = GetCacheKey(query);
            _cashedContexts.GetOrAdd(set.EntityType.Name, (_) => new HashSet<string>()).Add(key);
            result = _cache.GetOrCreate(key, cache => new Lazy<List<T>>(() =>
               {
                   cache.AbsoluteExpiration = DateTimeOffset.Now.AddSeconds(AbsoluteExpirationSeconds);
                   return query.ToList();
               }));
        }

        return result.Value;
    }

    public static void Clear()
    {
        lock (_cache)
        {
            _cashedContexts.Clear();
            _cache.Dispose();
        }
        _cache = new MemoryCache(new MemoryCacheOptions());
    }

    public static DbContext GetDbContext<T>(this IQueryable<T> queryable)
    {
        var serviceProvider = (queryable.Provider as IInfrastructure<IServiceProvider>)?.Instance;
        var currentDbContext = serviceProvider?.GetService<ICurrentDbContext>();
        return currentDbContext?.Context;
    }

    public static DbSet<T> GetDbSet<T>(this IQueryable<T> queryable) where T : class
    {
        var context = queryable.GetDbContext();
        return context?.Set<T>();
    }
}
