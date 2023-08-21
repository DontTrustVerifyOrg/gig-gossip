using System;
using System.IO;
using CryptoToolkit;
using NBitcoin;
using NBitcoin.Secp256k1;
using static System.Collections.Specialized.BitVector32;

namespace NGigGossip4Nostr
{
	public class SettlerMonitor
	{
		Dictionary<Tuple<Uri, string>, string> monitoredPreimages = new();
		Dictionary<string, Action<string>> monitoredPreimagesActions = new();
		Dictionary<Tuple<Uri, Guid>, string> monitoredKeys = new();
		Dictionary<Guid, Action<string>> monitoredKeysActions = new();
		GigGossipNode gigGossipNode;

		public SettlerMonitor(GigGossipNode gigGossipNode)
		{
			this.gigGossipNode = gigGossipNode;
		}

		public void MonitorPreimage(Uri serviceUri, string phash, Action<string> action)
		{
			lock (monitoredPreimages)
				monitoredPreimages[Tuple.Create(serviceUri, phash)] = null;
			lock (monitoredPreimagesActions)
				monitoredPreimagesActions[phash] = action;
		}

		public void MonitorSymmetricKey(Uri serviceUri, Guid tid, Action<string> action)
		{
			lock (monitoredKeys)
				monitoredKeys[Tuple.Create(serviceUri, tid)] = null;
			lock (monitoredKeysActions)
				monitoredKeysActions[tid] = action;
		}


		Thread monitorThread;


		public void Start()
		{
			monitorThread = new Thread(async () =>
			{
				while (true)
				{
					{
						List<KeyValuePair<Tuple<Uri, string>, string>> pToMon;
						lock (monitoredPreimages)
							pToMon = (from kv in monitoredPreimages.ToList() where (kv.Value == null) select kv).ToList();
						foreach (var kv in pToMon)
						{
							var serviceUri = kv.Key.Item1;
							var phash = kv.Key.Item2;
							var preimage = await gigGossipNode.SettlerSelector.GetSettlerClient(serviceUri).RevealPreimageAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), phash);
                            if (!string.IsNullOrWhiteSpace(preimage))
                            {
								lock (monitoredPreimages)
									monitoredPreimages[kv.Key] = preimage;
								Action<string> act = null;
								lock (monitoredPreimagesActions)
									if (monitoredPreimagesActions.ContainsKey(phash))
										act = monitoredPreimagesActions[phash];
								if (act != null)
									act(preimage);
							}
						}
					}
					{
						List<KeyValuePair<Tuple<Uri, Guid>, string>> kToMon;
						lock (monitoredKeys)
							kToMon = (from kv in monitoredKeys.ToList() where (kv.Value == null) select kv).ToList();
						foreach (var kv in kToMon)
						{
							var serviceUri = kv.Key.Item1;
							var tid = kv.Key.Item2;
							var key = await gigGossipNode.SettlerSelector.GetSettlerClient(serviceUri).RevealSymmetricKeyAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), tid.ToString());
							if (!string.IsNullOrWhiteSpace(key))
							{
								lock (monitoredKeys)
									monitoredKeys[kv.Key] = key;
								Action<string> act = null;
								lock (monitoredKeysActions)
									if (monitoredKeysActions.ContainsKey(tid))
										act = monitoredKeysActions[tid];
								if (act != null)
									act(key);
							}
						}
					}

					Thread.Sleep(1000);
				}
			});
			monitorThread.Start();
		}
	}
}

