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
using NBitcoin.Protocol;

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
                using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();

                {
                    if ((from i in gigGossipNode.NodeDb.Context.MonitoredInvoices
                         where i.PaymentHash == phash && i.PublicKey == this.gigGossipNode.PublicKey
                         select i).FirstOrDefault() != null)
                    {
                        TL.Warning("Invoice already monitored");
                        TX.Commit();
                        return;
                    }
                }

                var obj = new MonitoredInvoiceRow()
                {
                    PublicKey = this.gigGossipNode.PublicKey,
                    PaymentHash = phash,
                    InvoiceState = InvoiceState.Open,
                    Data = data,
                };
                gigGossipNode.NodeDb.Context
                    .INSERT(obj)
                    .SAVE();
                TX.Commit();
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
                        List<MonitoredInvoiceRow> invToMon;

                        {
                            using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();

                            invToMon = (from i in gigGossipNode.NodeDb.Context.MonitoredInvoices
                                        where i.PublicKey == this.gigGossipNode.PublicKey
                                        && i.InvoiceState != InvoiceState.Settled && i.InvoiceState != InvoiceState.Cancelled
                                        select i).ToList();
                            TX.Commit();
                        }

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
                                using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                                gigGossipNode.NodeDb.Context.UPDATE(inv).SAVE();
                                TX.Commit();
                            }
                        }

                    }

                    await foreach (var invstateupd in this.InvoiceStateUpdatesClient.StreamAsync(await this.gigGossipNode.MakeWalletAuthToken(), CancellationTokenSource.Token))
                    {
                        TL.Iteration(invstateupd);
                        var payhash = invstateupd.PaymentHash;
                        var state = invstateupd.NewState;
                        {
                            MonitoredInvoiceRow? inv;
                            {
                                using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                                inv = (from i in gigGossipNode.NodeDb.Context.MonitoredInvoices
                                           where i.PublicKey == this.gigGossipNode.PublicKey
                                           && i.InvoiceState != InvoiceState.Settled && i.InvoiceState != InvoiceState.Cancelled
                                           && i.PaymentHash == payhash
                                           select i).FirstOrDefault();
                                TX.Commit();
                            }
                            if (inv != null)
                            {
                                if (state != inv.InvoiceState)
                                {
                                    TL.Info("OnInvoiceStateChange");
                                    await gigGossipNode.OnInvoiceStateChangeAsync(state, inv.Data);
                                    inv.InvoiceState = state;
                                    using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();

                                    gigGossipNode.NodeDb.Context.UPDATE(inv).SAVE();
                                    TX.Commit();
                                }
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

