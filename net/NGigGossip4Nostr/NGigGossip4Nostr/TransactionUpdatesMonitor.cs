using System;
using System.Diagnostics;
using System.Net.WebSockets;
using GigDebugLoggerAPIClient;
using GigLNDWalletAPIClient;
using NetworkClientToolkit;

namespace NGigGossip4Nostr;

public class TransactionUpdatesMonitor : HubMonitor
{
    GigGossipNode gigGossipNode;
    public ITransactionUpdatesClient TransactionUpdatesClient;
    CancellationTokenSource CancellationTokenSource = new();

    private readonly LogWrapper<TransactionUpdatesMonitor> TRACE = FlowLoggerFactory.Trace<TransactionUpdatesMonitor>();

    public TransactionUpdatesMonitor(GigGossipNode gigGossipNode)
    {
        this.gigGossipNode = gigGossipNode;
    }

    public async Task StartAsync()
    {
        using var TL = TRACE.Log().Args();
        try
        {
            this.OnServerConnectionState += TransactionUpdatesMonitor_OnServerConnectionState;
            TransactionUpdatesClient = gigGossipNode.GetWalletClient().CreateTransactionUpdatesClient();

            await base.StartAsync(
                async () =>
                {
                    var token = await gigGossipNode.MakeWalletAuthToken();
                    await TransactionUpdatesClient.ConnectAsync(token, CancellationTokenSource.Token);
                },
                async () =>
                {
                   
                    await foreach (var newtrans in this.TransactionUpdatesClient.StreamAsync(await this.gigGossipNode.MakeWalletAuthToken(), CancellationTokenSource.Token))
                    {
                        TL.Iteration(newtrans.AmountSat+"|"+ newtrans.TxHash.ToString());
                        gigGossipNode.OnLNDNewTransaction(newtrans);
                    }
                },
                TransactionUpdatesClient.Uri,
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

    private void TransactionUpdatesMonitor_OnServerConnectionState(object? sender, ServerConnectionStateEventArgs e)
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
            OnServerConnectionState -= TransactionUpdatesMonitor_OnServerConnectionState;
            base.Stop(CancellationTokenSource);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }
}

