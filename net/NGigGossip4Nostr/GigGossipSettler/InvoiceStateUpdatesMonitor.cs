using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;

using GigLNDWalletAPIClient;
using NBitcoin.Secp256k1;
using Newtonsoft.Json.Linq;
using static System.Net.WebRequestMethods;
using NetworkClientToolkit;
using GigGossipSettler.Exceptions;

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
                {
                    using var TX = settler.settlerContext.Value.BEGIN_TRANSACTION( System.Data.IsolationLevel.Serializable);

                    List<Gig> gigs = (from g in settler.settlerContext.Value.Gigs where (g.Status == GigStatus.Open || g.Status == GigStatus.Accepted) select g).ToList();

                    foreach (var gig in gigs)
                    {
                        if (gig.Status == GigStatus.Open)
                        {
                            var network_state_result = await settler.lndWalletClient.GetInvoiceAsync(settler.MakeAuthToken(), gig.NetworkPaymentHash, CancellationTokenSource.Token);

                            InvoiceState network_invoice_state;
                            if (WalletAPIResult.Status(network_state_result) == LNDWalletErrorCode.UnknownInvoice)
                                network_invoice_state = InvoiceState.Cancelled;
                            else
                                network_invoice_state = WalletAPIResult.Get<InvoiceRecord>(network_state_result).State;

                            if (network_invoice_state == InvoiceState.Accepted && gig.SubStatus == GigSubStatus.AcceptedByReply)
                            {
                                gig.Status = GigStatus.Accepted;
                                gig.SubStatus = GigSubStatus.None;
                                gig.DisputeDeadline = DateTime.UtcNow + settler.disputeTimeout;
                                settler.settlerContext.Value
                                    .UPDATE(gig)
                                    .SAVE();
                                await settler.ScheduleGigAsync(gig);
                            }
                            else if (network_invoice_state == InvoiceState.Accepted)
                            {
                                gig.SubStatus = GigSubStatus.AcceptedByNetwork;
                                settler.settlerContext.Value
                                    .UPDATE(gig)
                                    .SAVE();
                            }
                            else if (network_invoice_state == InvoiceState.Cancelled)
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

                    TX.Commit();
                }

                await foreach (var invstateupd in invoiceStateUpdatesClient.StreamAsync(settler.MakeAuthToken(), CancellationTokenSource.Token))
                {
                    var payhash = invstateupd.PaymentHash;
                    var state = invstateupd.NewState;

                    if (state ==  InvoiceState.Accepted)
                    {
                        using var TX = settler.settlerContext.Value.BEGIN_TRANSACTION( System.Data.IsolationLevel.Serializable);

                        var gig = (from g in settler.settlerContext.Value.Gigs
                                   where (g.NetworkPaymentHash == payhash)
                                   select g).FirstOrDefault();
                        if (gig != null)
                        {
                            if (gig.SubStatus == GigSubStatus.None && gig.Status == GigStatus.Open)
                            {
                                gig.SubStatus = GigSubStatus.AcceptedByNetwork;
                                settler.settlerContext.Value
                                    .UPDATE(gig)
                                    .SAVE();
                            }
                            else if (gig.SubStatus == GigSubStatus.AcceptedByReply)
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
                        TX.Commit();
                    }
                    else if (state ==  InvoiceState.Cancelled)
                    {
                        using var TX = settler.settlerContext.Value.BEGIN_TRANSACTION(System.Data.IsolationLevel.Serializable);

                        var gig = (from g in settler.settlerContext.Value.Gigs
                                   where (g.NetworkPaymentHash == payhash) 
                                   select g).FirstOrDefault();
                        if (gig != null)
                        {
                            if (gig.Status == GigStatus.Accepted)
                            {
                                await settler.DescheduleGigAsync(gig);
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

                        TX.Commit();
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

