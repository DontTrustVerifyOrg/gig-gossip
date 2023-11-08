using System;
using System.Collections.Concurrent;

namespace NGigGossip4Nostr;

public static class Extensions
{
    /// <summary>
    /// Adds a key/value pair to the <see cref="ConcurrentDictionary{TKey, TValue}"/> by using the specified function 
    /// if the key does not already exist. Returns the new value, or the existing value if the key exists.
    /// </summary>
    public static async Task<TResult> GetOrAddAsync<TKey, TResult>(
        this ConcurrentDictionary<TKey, TResult> dict,
        TKey key, Func<TKey, Task<TResult>> asyncValueFactory)
    {
        if (dict.TryGetValue(key, out TResult resultingValue))
        {
            return resultingValue;
        }
        var newValue = await asyncValueFactory(key);
        return dict.GetOrAdd(key, newValue);
    }
}

