using System;
using Lnrpc;
using Microsoft.AspNetCore.Http.HttpResults;
using NBitcoin.Secp256k1;
using Walletrpc;

namespace GigLNDWalletAPI
{
	public class HubDicStore<T>
	{
		public static Dictionary<string, HashSet<T>> Item4PublicKey = new();
		public static Dictionary<T, HashSet<string>> PublicKeys4Items = new();

		public HubDicStore()
		{
		}

        public void RemoveConnection(string connectionId)
        {
            lock (Item4PublicKey)
            {
                lock (PublicKeys4Items)
                {
                    if (Item4PublicKey.ContainsKey(connectionId))
                        foreach (var payhash in Item4PublicKey[connectionId])
                            PublicKeys4Items[payhash].Remove(connectionId);
                }
                if (Item4PublicKey.ContainsKey(connectionId))
                    Item4PublicKey.Remove(connectionId);
            }
        }

        public void AddItem(string connectionId,T item)
        {
            lock (Item4PublicKey)
            {
                if (!Item4PublicKey.ContainsKey(connectionId))
                    Item4PublicKey[connectionId] = new();
                Item4PublicKey[connectionId].Add(item);

            }
            lock (PublicKeys4Items)
            {
                if (!PublicKeys4Items.ContainsKey(item))
                    PublicKeys4Items[item] = new();
                PublicKeys4Items[item].Add(connectionId);
            }
        }

        public bool ContainsItem(string connectionId,T item)
        {
            lock (Item4PublicKey)
            {
                if (Item4PublicKey.ContainsKey(connectionId))
                    return Item4PublicKey[connectionId].Contains(item);
            }
            return false;
        }

        public HashSet<string> PublicKeysForItem(T item)
        {
            lock (PublicKeys4Items)
                if (PublicKeys4Items.ContainsKey(item))
                    return PublicKeys4Items[item];
            return new HashSet<string>();
        }

    }
}

