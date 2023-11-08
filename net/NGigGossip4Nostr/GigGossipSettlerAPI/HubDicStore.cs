using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace GigGossipSettlerAPI
{
    public class HubDicStore<T>
    {
        public static ConcurrentDictionary<string, ConcurrentDictionary<T, bool>> Item4PublicKey = new();
        public static ConcurrentDictionary<T, ConcurrentDictionary<string, bool>> PublicKeys4Items = new();

        public HubDicStore()
        {
        }

        public void AddItem(string connectionId, T item)
        {
            Item4PublicKey.GetOrAdd(connectionId, (_) => new ConcurrentDictionary<T, bool>()).TryAdd(item, true);
            PublicKeys4Items.GetOrAdd(item, (_) => new ConcurrentDictionary<string, bool>()).TryAdd(connectionId, true);
        }

        public void RemoveConnection(string connectionId)
        {
            ConcurrentDictionary<T, bool> inner;
            if (Item4PublicKey.TryGetValue(connectionId, out inner!))
                foreach (var payhash in inner.Keys.ToList())
                    PublicKeys4Items[payhash].TryRemove(connectionId, out _);
            Item4PublicKey.TryRemove(connectionId, out _);
        }

        public bool ContainsItem(string connectionId, T item)
        {
            ConcurrentDictionary<T, bool> inner;
            if (Item4PublicKey.TryGetValue(connectionId, out inner!))
                return inner.TryGetValue(item, out _);
            return false;
        }

        public HashSet<string> PublicKeysForItem(T item)
        {
            ConcurrentDictionary<string, bool> inner;
            if (PublicKeys4Items.TryGetValue(item, out inner!))
                return new HashSet<string>(inner.Keys);
            return new HashSet<string>();
        }

    }
}

