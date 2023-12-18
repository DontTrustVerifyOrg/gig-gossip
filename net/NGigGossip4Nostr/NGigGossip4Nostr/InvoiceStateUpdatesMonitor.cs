using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using CryptoToolkit;
using GigLNDWalletAPIClient;
using NBitcoin.Secp256k1;
using Newtonsoft.Json.Linq;
using static System.Net.WebRequestMethods;

namespace NGigGossip4Nostr
{
	public interface IInvoiceStateUpdatesMonitorEvents
	{
        public void OnInvoiceStateChange(string state, byte[] data);
    }

    public class InvoiceStateUpdatesMonitor
	{
		GigGossipNode gigGossipNode;
        public InvoiceStateUpdatesClient InvoiceStateUpdatesClient;
        bool AppExiting = false;
        bool ClientConnected = false;
        object ClientLock = new();
        Thread invoiceMonitorThread;
        CancellationTokenSource CancellationTokenSource = new();


        public InvoiceStateUpdatesMonitor(GigGossipNode gigGossipNode)
		{
			this.gigGossipNode = gigGossipNode;
		}

        void WaitForClientConnected()
        {
            lock (ClientLock)
            {
                while (!ClientConnected)
                    Monitor.Wait(ClientLock);
            }
            if(AppExiting)
                throw new OperationCanceledException();
        }

        void NotifyClientIsConnected(bool isconnected)
        {
            lock (ClientLock)
            {
                ClientConnected = true;
                Monitor.PulseAll(ClientLock);
            }
        }

        void NotifyAppClosing()
        {
            lock (ClientLock)
            {
                AppExiting = true;
                Monitor.PulseAll(ClientLock);
            }
        }


        public async Task MonitorInvoiceAsync(string phash, byte[] data)
        {
            if ((from i in gigGossipNode.nodeContext.Value.MonitoredInvoices
                 where i.PaymentHash == phash && i.PublicKey == this.gigGossipNode.PublicKey
                 select i).FirstOrDefault() != null)
                return;

            var obj = new MonitoredInvoiceRow()
            {
                PublicKey = this.gigGossipNode.PublicKey,
                PaymentHash = phash,
                InvoiceState = "Unknown",
                Data = data,
            };
            gigGossipNode.nodeContext.Value.AddObject(obj);
            try
            {
                await this.InvoiceStateUpdatesClient.MonitorAsync(gigGossipNode.MakeWalletAuthToken(), phash);
            }
            catch
            {
                gigGossipNode.nodeContext.Value.RemoveObject(obj);
                throw;
            }
        }

        public async Task StartAsync()
		{
            invoiceMonitorThread = new Thread(async () =>
            {
                while (true)
                {
                    try
                    {
                        var token = gigGossipNode.MakeWalletAuthToken();

                        InvoiceStateUpdatesClient = new InvoiceStateUpdatesClient(gigGossipNode.LNDWalletClient, gigGossipNode.HttpMessageHandler);
                        await InvoiceStateUpdatesClient.ConnectAsync(token);

                        NotifyClientIsConnected(true);

                        {
                            var invToMon = (from i in gigGossipNode.nodeContext.Value.MonitoredInvoices
                                            where i.PublicKey == this.gigGossipNode.PublicKey
                                            && i.InvoiceState != "Settled" && i.InvoiceState != "Cancelled"
                                            select i).ToList();

                            foreach (var inv in invToMon)
                            {
                                string state = string.Empty;
                                try
                                {
                                    //TODO PAWEL UNKNOW INVOICE
                                    state = WalletAPIResult.Get<string>(await gigGossipNode.LNDWalletClient.GetInvoiceStateAsync(gigGossipNode.MakeWalletAuthToken(), inv.PaymentHash));
                                }
                                catch (Exception ex) { Console.WriteLine(ex.Message); }
                                if (state != inv.InvoiceState)
                                {
                                    gigGossipNode.OnInvoiceStateChange(state, inv.Data);
                                    inv.InvoiceState = state;
                                    gigGossipNode.nodeContext.Value.SaveObject(inv);
                                }
                            }
                        }

                        await foreach (var invstateupd in this.InvoiceStateUpdatesClient.StreamAsync(this.gigGossipNode.MakeWalletAuthToken(), CancellationTokenSource.Token))
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
                        NotifyAppClosing();
                        //stream closed
                        return;
                    }
                    catch (Exception ex) when (ex is Microsoft.AspNetCore.SignalR.HubException ||
                                               ex is TimeoutException ||
                                               ex is WebSocketException)
                    {
                        NotifyClientIsConnected(false);
                        Trace.TraceWarning("Hub disconnected " + gigGossipNode.LNDWalletClient.BaseUrl + "/invoicestateupdates, reconnecting");
                        Thread.Sleep(1000);
                        //reconnect
                    }
                }
            });

            invoiceMonitorThread.Start();
            WaitForClientConnected();
        }


        public void Stop()
		{
			CancellationTokenSource.Cancel();
            invoiceMonitorThread.Join();
        }

    }
}

