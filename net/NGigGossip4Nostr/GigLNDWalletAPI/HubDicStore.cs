using System;
using Lnrpc;
using Microsoft.AspNetCore.Http.HttpResults;
using NBitcoin.Secp256k1;
using Walletrpc;

namespace GigLNDWalletAPI
{
	public class HubDicStore<T> where T:notnull
	{
		public Dictionary<string, HashSet<T>> Item4PublicKey = new();
		public Dictionary<T, HashSet<string>> PublicKeys4Items = new();

		public HubDicStore()
		{
		}

        public void RemoveConnection(string id)
        {
            lock (Item4PublicKey)
            {
                lock (PublicKeys4Items)
                {
                    if (Item4PublicKey.ContainsKey(id))
                        foreach (var payhash in Item4PublicKey[id])
                            PublicKeys4Items[payhash].Remove(id);
                }
                if (Item4PublicKey.ContainsKey(id))
                    Item4PublicKey.Remove(id);
            }
        }

        public void AddItem(string id,T item)
        {
            lock (Item4PublicKey)
            {
                if (!Item4PublicKey.ContainsKey(id))
                    Item4PublicKey[id] = new();
                Item4PublicKey[id].Add(item);

            }
            lock (PublicKeys4Items)
            {
                if (!PublicKeys4Items.ContainsKey(item))
                    PublicKeys4Items[item] = new();
                PublicKeys4Items[item].Add(id);
            }
        }

        public void RemoveItem(string id, T item)
        {
            lock (Item4PublicKey)
            {
                if (Item4PublicKey.ContainsKey(id))
                    Item4PublicKey[id].Remove(item);
            }
            lock (PublicKeys4Items)
            {
                if (PublicKeys4Items.ContainsKey(item))
                    PublicKeys4Items[item].Remove(id);
            }
        }

        public bool ContainsItem(string id,T item)
        {
            lock (Item4PublicKey)
            {
                if (Item4PublicKey.ContainsKey(id))
                    return Item4PublicKey[id].Contains(item);
            }
            return false;
        }

        public HashSet<string> IdsForItem(T item)
        {
            lock (PublicKeys4Items)
                if (PublicKeys4Items.ContainsKey(item))
                    return PublicKeys4Items[item];
            return new HashSet<string>();
        }

    }
}

