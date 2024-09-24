using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
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
        public Task OnInvoiceStateChangeAsync(InvoiceState state, byte[] data);
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
                    InvoiceState =  InvoiceState.Open,
                    Data = data,
                };
                gigGossipNode.nodeContext.Value.AddObject(obj);
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
                                        && i.InvoiceState != InvoiceState.Settled && i.InvoiceState !=  InvoiceState.Cancelled
                                        select i).ToList();

                        foreach (var inv in invToMon)
                        {
                            TL.Iteration(inv);
                            InvoiceState state = InvoiceState.Open;
                            try
                            {
                                state = WalletAPIResult.Get<InvoiceRecord>(await gigGossipNode.GetWalletClient().GetInvoiceAsync(await gigGossipNode.MakeWalletAuthToken(), inv.PaymentHash, CancellationTokenSource.Token)).State;
                                TL.Iteration(state);
                            }
                            catch (Exception ex)
                            {
                                TL.Exception(ex);
                            }

                            if (state != inv.InvoiceState)
                            {
                                TL.Info("OnInvoiceStateChange");
                                await gigGossipNode.OnInvoiceStateChangeAsync(state, inv.Data);
                                inv.InvoiceState = state;
                                gigGossipNode.nodeContext.Value.SaveObject(inv);
                            }
                        }
                    }

                    await foreach (var invstateupd in this.InvoiceStateUpdatesClient.StreamAsync(await this.gigGossipNode.MakeWalletAuthToken(), CancellationTokenSource.Token))
                    {
                        TL.Iteration(invstateupd);
                        var payhash = invstateupd.PaymentHash;
                        var state = invstateupd.NewState;
                        var inv = (from i in gigGossipNode.nodeContext.Value.MonitoredInvoices
                                   where i.PublicKey == this.gigGossipNode.PublicKey
                                   && i.InvoiceState !=  InvoiceState.Settled && i.InvoiceState != InvoiceState.Cancelled
                                   && i.PaymentHash == payhash
                                   select i).FirstOrDefault();
                        if (inv != null)
                        {
                            if (state != inv.InvoiceState)
                            {
                                TL.Info("OnInvoiceStateChange");
                                await gigGossipNode.OnInvoiceStateChangeAsync(state, inv.Data);
                                inv.InvoiceState = state;
                                gigGossipNode.nodeContext.Value.SaveObject(inv);
                            }
                        }
                        gigGossipNode.OnLNDInvoiceStateChanged(invstateupd);
                    }
                },
                InvoiceStateUpdatesClient.Uri,
                gigGossipNode.RetryPolicy,
                CancellationTokenSource.Token
            );
        }

        private void InvoiceStateUpdatesMonitor_OnServerConnectionState(object? sender, ServerConnectionStateEventArgs e)
        {
            using var TL = TRACE.Log().Args(e);
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

