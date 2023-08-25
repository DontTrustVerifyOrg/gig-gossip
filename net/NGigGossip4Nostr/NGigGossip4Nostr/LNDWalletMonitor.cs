using System;
using System.IO;
using CryptoToolkit;
using NBitcoin.Secp256k1;
using Newtonsoft.Json.Linq;
using static System.Net.WebRequestMethods;

namespace NGigGossip4Nostr
{
	public interface ILNDWalletMonitorEvents
	{
        public void OnInvoiceStateChange(string state, byte[] data);
        public void OnPaymentStatusChange(string status, byte[] data);
    }

    public class LNDWalletMonitor
	{
		GigGossipNode gigGossipNode;

		public LNDWalletMonitor(GigGossipNode gigGossipNode)
		{
			this.gigGossipNode = gigGossipNode;
		}

		public void MonitorInvoice(string phash, byte[] data)
		{
			if ((from i in gigGossipNode.nodeContext.Value.MonitoredInvoices
				 where i.PaymentHash == phash && i.PublicKey == this.gigGossipNode.PublicKey
				 select i).FirstOrDefault() != null)
				return;

			gigGossipNode.nodeContext.Value.AddObject(
				new MonitoredInvoiceRow()
				{
					PublicKey = this.gigGossipNode.PublicKey,
					PaymentHash = phash,
					InvoiceState = "Unknown",
					Data = data,
				});
		}

		public bool IsPaymentMonitored(string phash)
		{
			return (from i in gigGossipNode.nodeContext.Value.MonitoredPayments
					where i.PaymentHash == phash && i.PublicKey == this.gigGossipNode.PublicKey
					select i).FirstOrDefault() != null;
        }

        public void MonitorPayment(string phash, byte[] data)
        {
            if ((from i in gigGossipNode.nodeContext.Value.MonitoredPayments
                 where i.PaymentHash == phash && i.PublicKey == this.gigGossipNode.PublicKey
                 select i).FirstOrDefault() != null)
                return;

            gigGossipNode.nodeContext.Value.AddObject(
                new MonitoredPaymentRow()
                {
                    PublicKey = this.gigGossipNode.PublicKey,
                    PaymentHash = phash,
                    PaymentStatus = "Unknown",
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
								gigGossipNode.OnInvoiceStateChange(state, inv.Data);
								inv.InvoiceState = state;
								gigGossipNode.nodeContext.Value.SaveObject(inv);
							}
						}
					}

                    {
                        var payToMon = (from i in gigGossipNode.nodeContext.Value.MonitoredPayments
                                        where i.PublicKey == this.gigGossipNode.PublicKey
                                        && i.PaymentStatus != "Succeeded" && i.PaymentStatus != "Failed"
                                        select i).ToList();

                        foreach (var pay in payToMon)
                        {
                            var status = await gigGossipNode.LNDWalletClient.GetPaymentStatusAsync(gigGossipNode.MakeWalletAuthToken(), pay.PaymentHash);
                            if (status != pay.PaymentStatus)
                            {
                                gigGossipNode.OnPaymentStatusChange(status, pay.Data);
                                pay.PaymentStatus = status;
                                gigGossipNode.nodeContext.Value.SaveObject(pay);
                            }
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

