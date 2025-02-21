using System;
using System.Diagnostics;
using System.Net.WebSockets;
using GigDebugLoggerAPIClient;
using GigLNDWalletAPIClient;
using NetworkClientToolkit;

namespace NGigGossip4Nostr;

public interface IPaymentStatusUpdatesMonitorEvents
{
    public void OnPaymentStatusChange(PaymentStatus status, byte[] data);
}

public class PaymentStatusUpdatesMonitor : HubMonitor
{
    GigGossipNode gigGossipNode;
    public IPaymentStatusUpdatesClient PaymentStatusUpdatesClient;
    CancellationTokenSource CancellationTokenSource = new();

    private readonly LogWrapper<PaymentStatusUpdatesMonitor> TRACE = FlowLoggerFactory.Trace<PaymentStatusUpdatesMonitor>();

    public PaymentStatusUpdatesMonitor(GigGossipNode gigGossipNode)
    {
        this.gigGossipNode = gigGossipNode;
    }

    public bool IsPaymentMonitored(string phash)
    {
        using var TL = TRACE.Log().Args(phash);
        try
        {
            using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
            var ret = (from i in gigGossipNode.NodeDb.Context.MonitoredPayments
                       where i.PaymentHash == phash && i.PublicKey == this.gigGossipNode.PublicKey
                       select i).FirstOrDefault() != null;
            TX.Commit();
            return TL.Ret(ret);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
     }

    public async Task MonitorPaymentAsync(string phash, byte[] data)
    {
        using var TL = TRACE.Log().Args(phash,data);
        try
        {
            using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
            if ((from i in gigGossipNode.NodeDb.Context.MonitoredPayments
                 where i.PaymentHash == phash && i.PublicKey == this.gigGossipNode.PublicKey
                 select i).FirstOrDefault() != null)
            {
                TX.Commit();
                TL.Warning("Payment already monitored");
                return;
            }

            var obj = new MonitoredPaymentRow()
            {
                PublicKey = this.gigGossipNode.PublicKey,
                PaymentHash = phash,
                PaymentStatus =  PaymentStatus.Initiated,
                Data = data,
            };
            gigGossipNode.NodeDb.Context.INSERT(obj).SAVE();
            TX.Commit();
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task StopPaymentMonitoringAsync(string phash)
    {
        using var TL = TRACE.Log().Args(phash);
        try
        {
            using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
            var o = (from i in gigGossipNode.NodeDb.Context.MonitoredPayments
                     where i.PaymentHash == phash && i.PublicKey == this.gigGossipNode.PublicKey
                     select i).FirstOrDefault();
            if (o == null)
            {
                TL.Warning("Payment not monitored");
                TX.Commit();
                return;
            }

            gigGossipNode.NodeDb.Context.DELETE(o).SAVE();
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
        using var TL = TRACE.Log().Args();
        try
        {
            this.OnServerConnectionState += PaymentStatusUpdatesMonitor_OnServerConnectionState;
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
                        List<MonitoredPaymentRow> payToMon;
                        {
                            using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                            payToMon = (from i in gigGossipNode.NodeDb.Context.MonitoredPayments
                                            where i.PublicKey == this.gigGossipNode.PublicKey
                                            && i.PaymentStatus != PaymentStatus.Succeeded && i.PaymentStatus != PaymentStatus.Failed
                                            select i).ToList();
                            TX.Commit();
                        }

                        foreach (var pay in payToMon)
                        {
                            TL.Iteration(pay);
                            try
                            {
                                var status = WalletAPIResult.Get<PaymentRecord>(await gigGossipNode.GetWalletClient().GetPaymentAsync(await gigGossipNode.MakeWalletAuthToken(), pay.PaymentHash, CancellationTokenSource.Token)).Status;
                                if (status != pay.PaymentStatus)
                                {
                                    TL.Info("OnPaymentStatusChange");
                                    gigGossipNode.OnPaymentStatusChange(status, pay.Data);
                                    pay.PaymentStatus = status;
                                    using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                                    gigGossipNode.NodeDb.Context.UPDATE(pay).SAVE();
                                    TX.Commit();
                                }
                            }
                            catch (GigLNDWalletAPIException ex)
                            {
                                TL.Exception(ex);
                            }
                        }
                    }

                    await foreach (var paystateupd in this.PaymentStatusUpdatesClient.StreamAsync(await this.gigGossipNode.MakeWalletAuthToken(), CancellationTokenSource.Token))
                    {
                        TL.Iteration(paystateupd.PaymentHash+"|"+ paystateupd.NewStatus.ToString());
                        var payhash = paystateupd.PaymentHash;
                        var status = paystateupd.NewStatus;
                        {
                            MonitoredPaymentRow? pay;
                            {
                                using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                                pay = (from i in gigGossipNode.NodeDb.Context.MonitoredPayments
                                           where i.PublicKey == this.gigGossipNode.PublicKey
                                           && i.PaymentStatus != PaymentStatus.Succeeded && i.PaymentStatus != PaymentStatus.Failed
                                           && i.PaymentHash == payhash
                                           select i).FirstOrDefault();
                                TX.Commit();
                            }

                            if (pay != null)
                            {
                                if (status != pay.PaymentStatus)
                                {
                                    TL.Info("OnPaymentStatusChange");
                                    gigGossipNode.OnPaymentStatusChange(status, pay.Data);
                                    pay.PaymentStatus = status;
                                    using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                                    gigGossipNode.NodeDb.Context.UPDATE(pay).SAVE();
                                    TX.Commit();
                                }
                            }
                        }
                        gigGossipNode.OnLNDPaymentStatusChanged(paystateupd);
                    }
                },
                PaymentStatusUpdatesClient.Uri,
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

    private void PaymentStatusUpdatesMonitor_OnServerConnectionState(object? sender, ServerConnectionStateEventArgs e)
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
            OnServerConnectionState -= PaymentStatusUpdatesMonitor_OnServerConnectionState;
            base.Stop(CancellationTokenSource);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }
}

