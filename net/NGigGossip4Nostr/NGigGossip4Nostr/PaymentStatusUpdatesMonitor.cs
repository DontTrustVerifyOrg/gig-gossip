using System;
using System.Diagnostics;
using System.Net.WebSockets;
using GigLNDWalletAPIClient;
using NetworkClientToolkit;

namespace NGigGossip4Nostr;

public interface IPaymentStatusUpdatesMonitorEvents
{
    public void OnPaymentStatusChange(string status, byte[] data);
}

public class PaymentStatusUpdatesMonitor : HubMonitor
{
    GigGossipNode gigGossipNode;
    public IPaymentStatusUpdatesClient PaymentStatusUpdatesClient;
    CancellationTokenSource CancellationTokenSource = new();

    public PaymentStatusUpdatesMonitor(GigGossipNode gigGossipNode)
    {
        this.gigGossipNode = gigGossipNode;
    }

    public bool IsPaymentMonitored(string phash)
    {
        return (from i in gigGossipNode.nodeContext.Value.MonitoredPayments
                where i.PaymentHash == phash && i.PublicKey == this.gigGossipNode.PublicKey
                select i).FirstOrDefault() != null;
    }

    public async Task MonitorPaymentAsync(string phash, byte[] data)
    {
        if ((from i in gigGossipNode.nodeContext.Value.MonitoredPayments
             where i.PaymentHash == phash && i.PublicKey == this.gigGossipNode.PublicKey
             select i).FirstOrDefault() != null)
            return;

        var obj = new MonitoredPaymentRow()
        {
            PublicKey = this.gigGossipNode.PublicKey,
            PaymentHash = phash,
            PaymentStatus = "Unknown",
            Data = data,
        };
        gigGossipNode.nodeContext.Value.AddObject(obj);

        try
        {
            await this.PaymentStatusUpdatesClient.MonitorAsync(await gigGossipNode.MakeWalletAuthToken(), phash, CancellationTokenSource.Token);
        }
        catch
        {
            gigGossipNode.nodeContext.Value.RemoveObject(obj);
            throw;
        }
    }

    public async Task StopPaymentMonitoringAsync(string phash)
    {
        var o = (from i in gigGossipNode.nodeContext.Value.MonitoredPayments
                 where i.PaymentHash == phash && i.PublicKey == this.gigGossipNode.PublicKey
                 select i).FirstOrDefault();
        if (o == null)
            return;

        await this.PaymentStatusUpdatesClient.StopMonitoringAsync(await gigGossipNode.MakeWalletAuthToken(), phash, CancellationTokenSource.Token);
        gigGossipNode.nodeContext.Value.RemoveObject(o);
    }


    public async Task StartAsync()
    {
        PaymentStatusUpdatesClient = gigGossipNode.GetWalletClient().CreatePaymentStatusUpdatesClient();

        await base.StartAsync(
            async () =>
            {
                var token = await gigGossipNode.MakeWalletAuthToken();
                await PaymentStatusUpdatesClient.ConnectAsync(token, CancellationTokenSource.Token);
            },
            async () =>
            {
                {
                    var payToMon = (from i in gigGossipNode.nodeContext.Value.MonitoredPayments
                                    where i.PublicKey == this.gigGossipNode.PublicKey
                                    && i.PaymentStatus != "Succeeded" && i.PaymentStatus != "Failed"
                                    select i).ToList();

                    foreach (var pay in payToMon)
                    {
                        try
                        {
                            var status = WalletAPIResult.Get<string>(await gigGossipNode.GetWalletClient().GetPaymentStatusAsync(await gigGossipNode.MakeWalletAuthToken(), pay.PaymentHash, CancellationTokenSource.Token));
                            if (status != pay.PaymentStatus)
                            {
                                gigGossipNode.OnPaymentStatusChange(status, pay.Data);
                                pay.PaymentStatus = status;
                                gigGossipNode.nodeContext.Value.SaveObject(pay);
                            }
                        }
                        catch (GigLNDWalletAPIException ex)
                        {
                            await gigGossipNode.FlowLogger.TraceExceptionAsync(ex);
                        }
                    }
                }

                await foreach (var paystateupd in this.PaymentStatusUpdatesClient.StreamAsync(await this.gigGossipNode.MakeWalletAuthToken(), CancellationTokenSource.Token))
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
            },
            PaymentStatusUpdatesClient.Uri,
            gigGossipNode.RetryPolicy,
            CancellationTokenSource.Token
        );
    }

    public void Stop()
    {
        base.Stop(CancellationTokenSource);
    }
}

