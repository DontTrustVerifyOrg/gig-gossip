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

namespace GigGossipSettler;

public interface IInvoiceStateUpdatesMonitorEvents
{
    public void OnInvoiceStateChange(string state, byte[] data);
}

public class InvoiceStateUpdatesMonitor
{
    Settler settler;
    public InvoiceStateUpdatesClient invoiceStateUpdatesClient;
    bool AppExiting = false;
    bool ClientConnected = false;
    object ClientLock = new();
    Thread invoiceMonitorThread;
    CancellationTokenSource CancellationTokenSource = new();


    public InvoiceStateUpdatesMonitor(Settler settler)
    {
        this.settler = settler;
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


    public async Task MonitorInvoicesAsync(string inv1, string inv2)
    {
        var tok = settler.MakeAuthToken();
        await invoiceStateUpdatesClient.MonitorAsync(tok, inv1);
        await invoiceStateUpdatesClient.MonitorAsync(tok, inv2);
    }


    public async Task StartAsync()
    {
        invoiceMonitorThread = new Thread(async () =>
        {
            while (true)
            {
                try
                {
                    var token = settler.MakeAuthToken();

                    invoiceStateUpdatesClient = new InvoiceStateUpdatesClient(settler.lndWalletClient, settler.HttpMessageHandler);
                    await invoiceStateUpdatesClient.ConnectAsync(settler.MakeAuthToken());


                    NotifyClientIsConnected(true);

                    List<Gig> gigs = (from g in settler.settlerContext.Value.Gigs where (g.Status == GigStatus.Open || g.Status == GigStatus.Accepted) select g).ToList();

                    foreach (var gig in gigs)
                    {
                        if (gig.Status == GigStatus.Open)
                        {
                            var network_state = WalletAPIResult.Get<string>(await settler.lndWalletClient.GetInvoiceStateAsync(settler.MakeAuthToken(), gig.NetworkPaymentHash));
                            var payment_state = WalletAPIResult.Get<string>(await settler.lndWalletClient.GetInvoiceStateAsync(settler.MakeAuthToken(), gig.PaymentHash));
                            if (network_state == "Accepted" && payment_state == "Accepted")
                            {
                                gig.Status = GigStatus.Accepted;
                                gig.SubStatus = GigSubStatus.None;
                                gig.DisputeDeadline = DateTime.UtcNow + settler.disputeTimeout;
                                settler.settlerContext.Value.SaveObject(gig);
                                await settler.ScheduleGigAsync(gig);
                            }
                            else if (network_state == "Accepted")
                            {
                                gig.SubStatus = GigSubStatus.AcceptedByNetwork;
                                settler.settlerContext.Value.SaveObject(gig);
                            }
                            else if (payment_state == "Accepted")
                            {
                                gig.SubStatus = GigSubStatus.AcceptedByReply;
                                settler.settlerContext.Value.SaveObject(gig);
                            }
                            else if (network_state == "Cancelled" || payment_state == "Cancelled")
                            {
                                gig.Status = GigStatus.Cancelled;
                                gig.SubStatus = GigSubStatus.None;
                                settler.settlerContext.Value.SaveObject(gig);
                            }
                        }
                        else if (gig.Status == GigStatus.Accepted)
                        {
                            settler.FireOnGigStatus(gig.SignedRequestPayloadId, gig.ReplierCertificateId, gig.Status, gig.SymmetricKey);
                            if (DateTime.UtcNow >= gig.DisputeDeadline)
                            {
                                await settler.SettleGigAsync(gig);
                            }
                            else
                            {
                                await settler.ScheduleGigAsync(gig);
                            }
                        }
                    }

                    await foreach (var invstateupd in invoiceStateUpdatesClient.StreamAsync(settler.MakeAuthToken(), CancellationTokenSource.Token))
                    {
                        var invp = invstateupd.Split('|');
                        var payhash = invp[0];
                        var state = invp[1];

                        if (state == "Accepted")
                        {
                            var gig = (from g in settler.settlerContext.Value.Gigs
                                       where (g.NetworkPaymentHash == payhash) || (g.PaymentHash == payhash)
                                       select g).FirstOrDefault();
                            if (gig != null)
                            {
                                if (gig.SubStatus == GigSubStatus.None && gig.NetworkPaymentHash == payhash && gig.Status == GigStatus.Open)
                                {
                                    gig.SubStatus = GigSubStatus.AcceptedByNetwork;
                                    settler.settlerContext.Value.SaveObject(gig);
                                }
                                else if (gig.SubStatus == GigSubStatus.None && gig.PaymentHash == payhash && gig.Status == GigStatus.Open)
                                {
                                    gig.SubStatus = GigSubStatus.AcceptedByReply;
                                    settler.settlerContext.Value.SaveObject(gig);
                                }
                                else if ((gig.NetworkPaymentHash == payhash && gig.SubStatus == GigSubStatus.AcceptedByReply)
                                || (gig.PaymentHash == payhash && gig.SubStatus == GigSubStatus.AcceptedByNetwork))
                                {
                                    gig.Status = GigStatus.Accepted;
                                    gig.SubStatus = GigSubStatus.None;
                                    gig.DisputeDeadline = DateTime.UtcNow + this.settler.disputeTimeout;
                                    settler.settlerContext.Value.SaveObject(gig);
                                    settler.FireOnGigStatus(gig.SignedRequestPayloadId, gig.ReplierCertificateId, gig.Status, gig.SymmetricKey);
                                    await settler.ScheduleGigAsync(gig);
                                }
                            }
                        }
                        else if (state == "Cancelled")
                        {
                            var gig = (from g in settler.settlerContext.Value.Gigs
                                       where (g.NetworkPaymentHash == payhash) || (g.PaymentHash == payhash)
                                       select g).FirstOrDefault();
                            if (gig != null)
                            {
                                if (gig.Status == GigStatus.Accepted)
                                {
                                    await settler.CancelGigAsync(gig);
                                }
                                if (gig.Status != GigStatus.Cancelled)
                                {
                                    gig.Status = GigStatus.Cancelled;
                                    gig.SubStatus = GigSubStatus.None;
                                    settler.settlerContext.Value.SaveObject(gig);
                                }
                            }
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
                    Trace.TraceWarning("Hub disconnected " + settler.lndWalletClient.BaseUrl + "/invoicestateupdates, reconnecting");
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

