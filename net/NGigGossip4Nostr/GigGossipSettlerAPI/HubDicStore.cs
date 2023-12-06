using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace GigGossipSettlerAPI
{
    public class HubDicStore<T>
    {
        public static ConcurrentDictionary<string, ConcurrentDictionary<T, bool>> Item4Id = new();
        public static ConcurrentDictionary<T, ConcurrentDictionary<string, bool>> Id4Items = new();

        public void AddItem(string id, T item)
        {
            Item4Id.GetOrAdd(id, (_) => new ConcurrentDictionary<T, bool>()).TryAdd(item, true);
            Id4Items.GetOrAdd(item, (_) => new ConcurrentDictionary<string, bool>()).TryAdd(id, true);
        }

        public void RemoveId(string id)
        {
            ConcurrentDictionary<T, bool> inner;
            if (Item4Id.TryGetValue(id, out inner!))
                foreach (var payhash in inner.Keys.ToList())
                    Id4Items[payhash].TryRemove(id, out _);
            Item4Id.TryRemove(id, out _);
        }

        public bool ContainsItem(string id, T item)
        {
            ConcurrentDictionary<T, bool> inner;
            if (Item4Id.TryGetValue(id, out inner!))
                return inner.TryGetValue(item, out _);
            return false;
        }

        public HashSet<string> IdsForItem(T item)
        {
            ConcurrentDictionary<string, bool> inner;
            if (Id4Items.TryGetValue(item, out inner!))
                return new HashSet<string>(inner.Keys);
            return new HashSet<string>();
        }

    }
}

