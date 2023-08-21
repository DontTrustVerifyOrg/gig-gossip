using System;
using CryptoToolkit;
using NBitcoin.Secp256k1;

namespace NGigGossip4Nostr
{
	public class LNDWalletMonitor
	{
		Dictionary<string, string> monitoredInvoices = new();
		Dictionary<Tuple<string, string>, Action> monitoredInvoicesActions = new();
		GigGossipNode gigGossipNode;

		public LNDWalletMonitor(GigGossipNode gigGossipNode)
		{
			this.gigGossipNode = gigGossipNode;
		}

		public void MonitorInvoice(string phash, string value, Action action)
		{
			lock (monitoredInvoices)
				monitoredInvoices[phash] = null;
			lock (monitoredInvoicesActions)
				monitoredInvoicesActions[Tuple.Create(phash, value)] = action;
		}

		Thread monitorThread;

		public void Start()
		{
			monitorThread = new Thread(async () =>
			{
				while (true)
				{
					List<KeyValuePair<string, string>> invToMon;
					lock (monitoredInvoices)
						invToMon = (from kv in monitoredInvoices.ToList() where (kv.Value != "Settled") && (kv.Value != "Cancelled") select kv).ToList();
					foreach (var inv in invToMon)
					{
						var state = await gigGossipNode.LNDWalletClient.GetInvoiceStateAsync(gigGossipNode.MakeWalletAuthToken(), inv.Key);
						if (state != inv.Value)
						{
							lock (monitoredInvoices)
								monitoredInvoices[inv.Key] = state;
							Action act = null;
							lock (monitoredInvoicesActions)
								if (monitoredInvoicesActions.ContainsKey(Tuple.Create(inv.Key, state)))
									act = monitoredInvoicesActions[Tuple.Create(inv.Key, state)];
							if (act != null)
								act();
						}
					}
					Thread.Sleep(1000);
				}
			});
			monitorThread.Start();
		}
	}
}

