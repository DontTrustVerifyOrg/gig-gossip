using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LNDWallet;

public static class MemoCache<T>
{
    static ConcurrentDictionary<(string pubkey, string currency), ConcurrentDictionary<string,T>> _items = new();
    
    static T GetOrAdd(string pubkey, string currency, string key, Func<T> factory)
    {
        return _items.GetOrAdd((pubkey, currency), (k) => new ConcurrentDictionary<string, T>()).GetOrAdd(key, factory());
    }

    static void TryAddRange(string pubkey, string currency, Func<IEnumerable<T>> factory)
    {
        foreach (var item in factory())
        {
            _items.GetOrAdd((pubkey, currency), (k) => new ConcurrentDictionary<string, T>()).TryAdd(item.ToString(), item);
        }
    }

    static void Invalidate(string pubkey, string currency)
    {
        _items.TryRemove((pubkey, currency), out _);
    }

    static List<T> Select(string pubkey, string currency)
    {
        return _items.GetOrAdd((pubkey, currency), (k) => new ConcurrentDictionary<string, T>()).Values.ToList();
    }
}
