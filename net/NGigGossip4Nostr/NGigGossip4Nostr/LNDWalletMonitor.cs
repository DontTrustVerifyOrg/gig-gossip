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
            this.gigGossipNode.InvoiceStateUpdatesClient.Monitor(gigGossipNode.MakeWalletAuthToken(), phash);
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

            this.gigGossipNode.PaymentStatusUpdatesClient.Monitor(gigGossipNode.MakeWalletAuthToken(), phash);
        }

        Thread invoiceMonitorThread;
        Thread paymentMonitorThread;
		CancellationTokenSource CancellationTokenSource = new();

        public void Start()
		{
			invoiceMonitorThread = new Thread(async () =>
			{
				try
				{
					await foreach (var invstateupd in this.gigGossipNode.InvoiceStateUpdatesClient.StreamAsync(this.gigGossipNode.MakeWalletAuthToken(), CancellationTokenSource.Token))
					{
						var invp = invstateupd.Split('|');
						var payhash = invp[0];
						var state = invp[1];
						var inv = (from i in gigGossipNode.nodeContext.Value.MonitoredInvoices
								   where i.PublicKey == this.gigGossipNode.PublicKey
								   && i.InvoiceState != "Settled" && i.InvoiceState != "Cancelled"
								   && i.PaymentHash == payhash
								   select i).FirstOrDefault();
						if (inv != null)
							if (state != inv.InvoiceState)
							{
								gigGossipNode.OnInvoiceStateChange(state, inv.Data);
								inv.InvoiceState = state;
								gigGossipNode.nodeContext.Value.SaveObject(inv);
							}
					}
				}
                catch (OperationCanceledException)
                {
                    //stream closed
                }

            });

			paymentMonitorThread = new Thread(async () =>
			{
				try
				{
					await foreach (var paystateupd in this.gigGossipNode.PaymentStatusUpdatesClient.StreamAsync(this.gigGossipNode.MakeWalletAuthToken(), CancellationTokenSource.Token))
					{
						var invp = paystateupd.Split('|');
						var payhash = invp[0];
						var status = invp[1];
						var pay = (from i in gigGossipNode.nodeContext.Value.MonitoredPayments
								   where i.PublicKey == this.gigGossipNode.PublicKey
								   && i.PaymentStatus != "Succeeded" && i.PaymentStatus != "Failed"
								   && i.PaymentHash == payhash
								   select i).FirstOrDefault();
						if (pay != null)
							if (status != pay.PaymentStatus)
							{
								gigGossipNode.OnPaymentStatusChange(status, pay.Data);
								pay.PaymentStatus = status;
								gigGossipNode.nodeContext.Value.SaveObject(pay);
							}
					}
				}
                catch (OperationCanceledException)
                {
                    //stream closed
                }
            });

			invoiceMonitorThread.Start();
			paymentMonitorThread.Start();

		}

		public void Stop()
		{
			CancellationTokenSource.Cancel();
            invoiceMonitorThread.Join();
            paymentMonitorThread.Join();
        }

    }
}

