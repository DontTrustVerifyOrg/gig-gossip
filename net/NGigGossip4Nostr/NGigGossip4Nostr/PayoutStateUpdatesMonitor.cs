using System;
using System.Diagnostics;
using System.Net.WebSockets;
using GigDebugLoggerAPIClient;
using GigLNDWalletAPIClient;
using NetworkClientToolkit;

namespace NGigGossip4Nostr;

public class PayoutStateUpdatesMonitor : HubMonitor
{
    GigGossipNode gigGossipNode;
    public IPayoutStateUpdatesClient PayoutStateUpdatesClient;
    CancellationTokenSource CancellationTokenSource = new();

    private readonly LogWrapper<PayoutStateUpdatesMonitor> TRACE = FlowLoggerFactory.Trace<PayoutStateUpdatesMonitor>();

    public PayoutStateUpdatesMonitor(GigGossipNode gigGossipNode)
    {
        this.gigGossipNode = gigGossipNode;
    }

    public async Task StartAsync()
    {
        using var TL = TRACE.Log().Args();
        try
        {
            this.OnServerConnectionState += PayoutStateUpdatesMonitor_OnServerConnectionState;
            PayoutStateUpdatesClient = gigGossipNode.GetWalletClient().CreatePayoutStateUpdatesClient();

            await base.StartAsync(
                async () =>
                {
                    var token = await gigGossipNode.MakeWalletAuthToken();
                    await PayoutStateUpdatesClient.ConnectAsync(token, CancellationTokenSource.Token);
                },
                async () =>
                {
                   
                    await foreach (var payout in this.PayoutStateUpdatesClient.StreamAsync(await this.gigGossipNode.MakeWalletAuthToken(), CancellationTokenSource.Token))
                    {
                        TL.Iteration(payout.PayoutId + "|" + payout.NewState.ToString() + "|" + payout.PayoutFee.ToString() + "|" + payout.Tx);
                        gigGossipNode.OnLNDPayoutStateChanged(payout);
                    }
                },
                PayoutStateUpdatesClient.Uri,
                gigGossipNode.RetryPolicy,
                CancellationTokenSource.Token
            );
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    private void PayoutStateUpdatesMonitor_OnServerConnectionState(object? sender, ServerConnectionStateEventArgs e)
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
            OnServerConnectionState -= PayoutStateUpdatesMonitor_OnServerConnectionState;
            base.Stop(CancellationTokenSource);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }
}

