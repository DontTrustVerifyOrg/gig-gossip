using System;
using System.Diagnostics;
using System.Net.WebSockets;
using GigLNDWalletAPIClient;

namespace NGigGossip4Nostr;

public interface IPaymentStatusUpdatesMonitorEvents
{
    public void OnPaymentStatusChange(string status, byte[] data);
}

public class PaymentStatusUpdatesMonitor
{
    GigGossipNode gigGossipNode;
    public PaymentStatusUpdatesClient PaymentStatusUpdatesClient;
    bool AppExiting = false;
    bool ClientConnected = false;
    object ClientLock = new();

    Thread paymentMonitorThread;
    CancellationTokenSource CancellationTokenSource = new();

    public PaymentStatusUpdatesMonitor(GigGossipNode gigGossipNode)
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
        if (AppExiting)
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
            await this.PaymentStatusUpdatesClient.MonitorAsync(gigGossipNode.MakeWalletAuthToken(), phash);
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

        await this.PaymentStatusUpdatesClient.StopMonitoringAsync(gigGossipNode.MakeWalletAuthToken(), phash);
        gigGossipNode.nodeContext.Value.RemoveObject(o);
    }


    public async Task StartAsync()
    {
        paymentMonitorThread = new Thread(async () =>
        {
            while (true)
            {
                try
                {
                    var token = gigGossipNode.MakeWalletAuthToken();

                    PaymentStatusUpdatesClient = new PaymentStatusUpdatesClient(gigGossipNode.LNDWalletClient, gigGossipNode.HttpMessageHandler);
                    await PaymentStatusUpdatesClient.ConnectAsync(token);

                    NotifyClientIsConnected(true);

                    {
                        var payToMon = (from i in gigGossipNode.nodeContext.Value.MonitoredPayments
                                        where i.PublicKey == this.gigGossipNode.PublicKey
                                        && i.PaymentStatus != "Succeeded" && i.PaymentStatus != "Failed"
                                        select i).ToList();

                        foreach (var pay in payToMon)
                        { 
                            try
                            {
                                var status = WalletAPIResult.Get<string>(await gigGossipNode.LNDWalletClient.GetPaymentStatusAsync(gigGossipNode.MakeWalletAuthToken(), pay.PaymentHash));
                                if (status != pay.PaymentStatus)
                                {
                                    gigGossipNode.OnPaymentStatusChange(status, pay.Data);
                                    pay.PaymentStatus = status;
                                    gigGossipNode.nodeContext.Value.SaveObject(pay);
                                }
                            }
                            catch(GigLNDWalletAPIException ex)
                            {
                                Trace.TraceError(ex.Message);
                            }
                        }
                    }

                    await foreach (var paystateupd in this.PaymentStatusUpdatesClient.StreamAsync(this.gigGossipNode.MakeWalletAuthToken(), CancellationTokenSource.Token))
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
                    NotifyAppClosing();
                    //stream closed
                    return;
                }
                catch (Exception ex) when (ex is Microsoft.AspNetCore.SignalR.HubException ||
                                           ex is TimeoutException ||
                                           ex is WebSocketException)
                {
                    NotifyClientIsConnected(false);
                    Trace.TraceWarning("Hub disconnected " + gigGossipNode.LNDWalletClient.BaseUrl + "/paymentstatusupdates, reconnecting");
                    Thread.Sleep(1000);
                    //reconnect
                }
            }
        });

        paymentMonitorThread.Start();
        WaitForClientConnected();

    }

    public void Stop()
    {
        CancellationTokenSource.Cancel();
        paymentMonitorThread.Join();
    }
}

