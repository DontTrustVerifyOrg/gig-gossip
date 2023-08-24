using System;
using System.IO;
using CryptoToolkit;
using NBitcoin.Secp256k1;
using Newtonsoft.Json.Linq;

namespace NGigGossip4Nostr
{
	public interface ILNDWalletMonitorEvents
	{
		public void OnInvoiceStateChange(byte[] data);
	}

	public class LNDWalletMonitor
	{
		GigGossipNode gigGossipNode;

		public LNDWalletMonitor(GigGossipNode gigGossipNode)
		{
			this.gigGossipNode = gigGossipNode;
		}

		public void MonitorInvoice(string phash, string value, byte[] data)
		{
			if ((from i in gigGossipNode.nodeContext.Value.MonitoredInvoices
				 where i.PaymentHash == phash && i.InvoiceState == value && i.PublicKey == this.gigGossipNode.PublicKey
				 select i).FirstOrDefault() != null)
				return;

			gigGossipNode.nodeContext.Value.AddObject(
				new MonitoredInvoiceRow()
				{
					PublicKey = this.gigGossipNode.PublicKey,
					PaymentHash = phash,
					InvoiceState = value,
					Data = data,
				});
		}

		Thread monitorThread;
		long _monitorThreadStop;

		public void Start()
		{
			_monitorThreadStop = 0;
			monitorThread = new Thread(async () =>
			{
				while (Interlocked.Read(ref _monitorThreadStop) == 0)
				{
					var invToMon = (from i in gigGossipNode.nodeContext.Value.MonitoredInvoices
									where i.PublicKey == this.gigGossipNode.PublicKey
									&& i.InvoiceState != "Settled" && i.InvoiceState != "Cancelled"
									select i).ToList();

					foreach (var inv in invToMon)
					{
						var state = await gigGossipNode.LNDWalletClient.GetInvoiceStateAsync(gigGossipNode.MakeWalletAuthToken(), inv.PaymentHash);
						if (state != inv.InvoiceState)
						{
							gigGossipNode.OnInvoiceStateChange(inv.Data);
							inv.InvoiceState = state;
							gigGossipNode.nodeContext.Value.SaveObject(inv);
						}
					}
					Thread.Sleep(1000);
				}
			});
			monitorThread.Start();
		}

		public void Stop()
		{
			Interlocked.Add(ref _monitorThreadStop, 1);
			monitorThread.Join();
		}

	}
}

