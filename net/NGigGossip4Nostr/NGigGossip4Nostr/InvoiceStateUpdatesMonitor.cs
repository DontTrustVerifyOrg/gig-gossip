using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using CryptoToolkit;
using GigLNDWalletAPIClient;
using Microsoft.AspNetCore.SignalR;
using NBitcoin.Secp256k1;
using Newtonsoft.Json.Linq;
using static System.Net.WebRequestMethods;
using NetworkClientToolkit;
using System.Reflection;

namespace NGigGossip4Nostr
{
	public interface IInvoiceStateUpdatesMonitorEvents
	{
        public void OnInvoiceStateChange(string state, byte[] data);
    }

    public class InvoiceStateUpdatesMonitor : HubMonitor
    {
        GigDebugLoggerAPIClient.LogWrapper<InvoiceStateUpdatesMonitor> TRACE = GigDebugLoggerAPIClient.FlowLoggerFactory.Trace<InvoiceStateUpdatesMonitor>();

        GigGossipNode gigGossipNode;
        public IInvoiceStateUpdatesClient InvoiceStateUpdatesClient;
        CancellationTokenSource CancellationTokenSource = new();

        public InvoiceStateUpdatesMonitor(GigGossipNode gigGossipNode)
		{
			this.gigGossipNode = gigGossipNode;
		}

        public async Task MonitorInvoiceAsync(string phash, byte[] data)
        {
            using var TL = TRACE.Log().Args(phash, data);
            try
            {

                if ((from i in gigGossipNode.nodeContext.Value.MonitoredInvoices
                     where i.PaymentHash == phash && i.PublicKey == this.gigGossipNode.PublicKey
                     select i).FirstOrDefault() != null)
                    {
                        TL.Warning("Invoice already monitored");
                        return;
                    }

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
                    await this.InvoiceStateUpdatesClient.MonitorAsync(await gigGossipNode.MakeWalletAuthToken(), phash, CancellationTokenSource.Token);
                }
                catch
                {
                    gigGossipNode.nodeContext.Value.RemoveObject(obj);
                    throw;
                }
            }
            catch (Exception ex)
            {
                TL.Exception(ex);
                throw;
            }
        }

        public async Task StartAsync()
        {
            using var TL = TRACE.Log();

            this.OnServerConnectionState += InvoiceStateUpdatesMonitor_OnServerConnectionState;
            InvoiceStateUpdatesClient = gigGossipNode.GetWalletClient().CreateInvoiceStateUpdatesClient();

            await base.StartAsync(
                async () =>
                    {
                        var token = await gigGossipNode.MakeWalletAuthToken();
                        await InvoiceStateUpdatesClient.ConnectAsync(token, CancellationToken.None);
                    },
                async () =>
                {
                    {
                        var invToMon = (from i in gigGossipNode.nodeContext.Value.MonitoredInvoices
                                        where i.PublicKey == this.gigGossipNode.PublicKey
                                        && i.InvoiceState != "Settled" && i.InvoiceState != "Cancelled"
                                        select i).ToList();

                        foreach (var inv in invToMon)
                        {
                            TL.Iteration(inv);
                            string state = string.Empty;
                            try
                            {
                                state = WalletAPIResult.Get<string>(await gigGossipNode.GetWalletClient().GetInvoiceStateAsync(await gigGossipNode.MakeWalletAuthToken(), inv.PaymentHash, CancellationTokenSource.Token));
                                TL.Iteration(state);
                            }
                            catch (Exception ex)
                            {
                                TL.Exception(ex);
                            }

                            if (state != inv.InvoiceState)
                            {
                                TL.Info("OnInvoiceStateChange");
                                gigGossipNode.OnInvoiceStateChange(state, inv.Data);
                                inv.InvoiceState = state;
                                gigGossipNode.nodeContext.Value.SaveObject(inv);
                            }
                        }
                    }

                    await foreach (var invstateupd in this.InvoiceStateUpdatesClient.StreamAsync(await this.gigGossipNode.MakeWalletAuthToken(), CancellationTokenSource.Token))
                    {
                        TL.Iteration(invstateupd);
                        var invp = invstateupd.Split('|');
                        var payhash = invp[0];
                        var state = invp[1];
                        var inv = (from i in gigGossipNode.nodeContext.Value.MonitoredInvoices
                                   where i.PublicKey == this.gigGossipNode.PublicKey
                                   && i.InvoiceState != "Settled" && i.InvoiceState != "Cancelled"
                                   && i.PaymentHash == payhash
                                   select i).FirstOrDefault();
                        if (inv != null)
                        {
                            if (state != inv.InvoiceState)
                            {
                                TL.Info("OnInvoiceStateChange");
                                gigGossipNode.OnInvoiceStateChange(state, inv.Data);
                                inv.InvoiceState = state;
                                gigGossipNode.nodeContext.Value.SaveObject(inv);
                            }
                        }
                        else
                            TL.Warning("Invoice not found");
                    }
                },
                InvoiceStateUpdatesClient.Uri,
                gigGossipNode.RetryPolicy,
                CancellationTokenSource.Token
            );
        }

        private void InvoiceStateUpdatesMonitor_OnServerConnectionState(object? sender, ServerConnectionStateEventArgs e)
        {
            using var TL = TRACE.Log().Args(sender, e);
            try
            {
                gigGossipNode.FireOnServerConnectionState(ServerConnectionSource.WalletAPI, e.State, e.Uri);
            }
            catch (Exception ex)
            {
                TL.Exception(ex);
            }
        }

        public void Stop()
        {
            using var TL = TRACE.Log();
            try
            {
                OnServerConnectionState -= InvoiceStateUpdatesMonitor_OnServerConnectionState;
                base.Stop(CancellationTokenSource);
            }
            catch (Exception ex)
            {
                TL.Exception(ex);
            }
        }
    }
}

