using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using CryptoToolkit;
using GigLNDWalletAPIClient;
using NBitcoin.Secp256k1;
using Newtonsoft.Json.Linq;
using static System.Net.WebRequestMethods;
using NetworkClientToolkit;

namespace GigGossipSettler;

public interface IInvoiceStateUpdatesMonitorEvents
{
    public void OnInvoiceStateChange(string state, byte[] data);
}

public class InvoiceStateUpdatesMonitor : HubMonitor
{
    Settler settler;
    public IInvoiceStateUpdatesClient invoiceStateUpdatesClient;
    CancellationTokenSource CancellationTokenSource = new();


    public InvoiceStateUpdatesMonitor(Settler settler)
    {
        this.settler = settler;
    }

    public async Task MonitorInvoicesAsync(string inv1, string inv2)
    {
        var tok = settler.MakeAuthToken();
        await invoiceStateUpdatesClient.MonitorAsync(tok, inv1, CancellationTokenSource.Token);
        await invoiceStateUpdatesClient.MonitorAsync(tok, inv2, CancellationTokenSource.Token);
    }


    public async Task StartAsync()
    {
        invoiceStateUpdatesClient = settler.lndWalletClient.CreateInvoiceStateUpdatesClient();

        await base.StartAsync(
            async () =>
            {
                await invoiceStateUpdatesClient.ConnectAsync(settler.MakeAuthToken(), CancellationToken.None);
            },
            async () =>
            {
                List<Gig> gigs = (from g in settler.settlerContext.Value.Gigs where (g.Status == GigStatus.Open || g.Status == GigStatus.Accepted) select g).ToList();

                foreach (var gig in gigs)
                {
                    if (gig.Status == GigStatus.Open)
                    {
                        var network_state = WalletAPIResult.Get<string>(await settler.lndWalletClient.GetInvoiceStateAsync(settler.MakeAuthToken(), gig.NetworkPaymentHash, CancellationTokenSource.Token));
                        var payment_state = WalletAPIResult.Get<string>(await settler.lndWalletClient.GetInvoiceStateAsync(settler.MakeAuthToken(), gig.PaymentHash, CancellationTokenSource.Token));
                        if (network_state == "Accepted" && payment_state == "Accepted")
                        {
                            gig.Status = GigStatus.Accepted;
                            gig.SubStatus = GigSubStatus.None;
                            gig.DisputeDeadline = DateTime.UtcNow + settler.disputeTimeout;
                            settler.settlerContext.Value
                                .UPDATE(gig)
                                .SAVE();
                            await settler.ScheduleGigAsync(gig);
                        }
                        else if (network_state == "Accepted")
                        {
                            gig.SubStatus = GigSubStatus.AcceptedByNetwork;
                            settler.settlerContext.Value
                                .UPDATE(gig)
                                .SAVE();
                        }
                        else if (payment_state == "Accepted")
                        {
                            gig.SubStatus = GigSubStatus.AcceptedByReply;
                            settler.settlerContext.Value
                                .UPDATE(gig)
                                .SAVE();
                        }
                        else if (network_state == "Cancelled" || payment_state == "Cancelled")
                        {
                            gig.Status = GigStatus.Cancelled;
                            gig.SubStatus = GigSubStatus.None;
                            settler.settlerContext.Value
                                .UPDATE(gig)
                                .SAVE();
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
                                settler.settlerContext.Value
                                    .UPDATE(gig)
                                    .SAVE();
                            }
                            else if (gig.SubStatus == GigSubStatus.None && gig.PaymentHash == payhash && gig.Status == GigStatus.Open)
                            {
                                gig.SubStatus = GigSubStatus.AcceptedByReply;
                                settler.settlerContext.Value
                                    .UPDATE(gig)
                                    .SAVE();
                            }
                            else if ((gig.NetworkPaymentHash == payhash && gig.SubStatus == GigSubStatus.AcceptedByReply)
                            || (gig.PaymentHash == payhash && gig.SubStatus == GigSubStatus.AcceptedByNetwork))
                            {
                                gig.Status = GigStatus.Accepted;
                                gig.SubStatus = GigSubStatus.None;
                                gig.DisputeDeadline = DateTime.UtcNow + this.settler.disputeTimeout;
                                settler.settlerContext.Value
                                    .UPDATE(gig)
                                    .SAVE();
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
                                await settler.DescheduleGigAsync(gig);
                                var status = WalletAPIResult.Status(await settler.lndWalletClient.CancelInvoiceAsync(settler.MakeAuthToken(), gig.NetworkPaymentHash, CancellationTokenSource.Token));
                                if (status != GigLNDWalletAPIErrorCode.Ok)
                                    Trace.TraceWarning("CancelInvoice failed");
                            }
                            if (gig.Status != GigStatus.Cancelled)
                            {
                                gig.Status = GigStatus.Cancelled;
                                gig.SubStatus = GigSubStatus.None;
                                settler.settlerContext.Value
                                    .UPDATE(gig)
                                    .SAVE();
                                settler.FireOnGigStatus(gig.SignedRequestPayloadId, gig.ReplierCertificateId, GigStatus.Cancelled);
                            }
                        }
                    }
                }
            },
            invoiceStateUpdatesClient.Uri,
            settler.RetryPolicy,
            CancellationTokenSource.Token
        );
    }


    public void Stop()
    {
        base.Stop(CancellationTokenSource);
    }

}

