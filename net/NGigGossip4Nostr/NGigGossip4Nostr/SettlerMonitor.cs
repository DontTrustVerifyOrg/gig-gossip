using System;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using GigGossipSettlerAPIClient;
using Microsoft.EntityFrameworkCore;
using static NBitcoin.Protocol.Behaviors.ChainBehavior;
using NetworkClientToolkit;

namespace NGigGossip4Nostr;

public interface ISettlerMonitorEvents
{
    public Task<bool> OnPreimageRevealedAsync(Uri settlerUri, string paymentHash, string preimage, CancellationToken cancellationToken);
    public void OnSymmetricKeyRevealed(byte[] data, string key);
}

public class SettlerMonitor
{

    GigDebugLoggerAPIClient.LogWrapper<SettlerMonitor> TRACE = GigDebugLoggerAPIClient.FlowLoggerFactory.Trace<SettlerMonitor>();

    GigGossipNode gigGossipNode;
    HashSet<Uri> alreadyMonitoredPreimage = new();
    HashSet<Uri> alreadyMonitoredSymmetricKey = new();

    CancellationTokenSource CancellationTokenSource = new();
    List<HubMonitor> monitoredHubs = new();

    public SettlerMonitor(GigGossipNode gigGossipNode)
    {
        this.gigGossipNode = gigGossipNode;
    }

    public async Task<bool> MonitorPreimageAsync(Uri serviceUri, string phash)
    {
        using var TL = TRACE.Log().Args(serviceUri, phash);
        try
        {
            {
                using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                if ((from i in gigGossipNode.NodeDb.Context.MonitoredPreimages
                     where i.PaymentHash == phash && i.PublicKey == this.gigGossipNode.PublicKey
                     select i).FirstOrDefault() != null)
                {
                    TX.Commit();
                    TL.Warning("Preimage already monitored");
                    return TL.Ret(false);
                }
            }

            await AttachMonitorPreimageAsync(serviceUri);

            var obj = new MonitoredPreimageRow()
            {
                PublicKey = this.gigGossipNode.PublicKey,
                ServiceUri = serviceUri,
                PaymentHash = phash,
                Preimage = null
            };

            {
                using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                gigGossipNode.NodeDb.Context.INSERT(obj).SAVE();
                TX.Commit();
            }

            return TL.Ret(true);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<bool> MonitorGigStatusAsync(Uri serviceUri, Guid signedRequestPayloadId, Guid replierCertificateId, byte[] data)
    {
        using var TL = TRACE.Log().Args(serviceUri, signedRequestPayloadId, replierCertificateId, data);
        try
        {
            {
                using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                if ((from i in gigGossipNode.NodeDb.Context.MonitoredGigStatuses
                     where i.SignedRequestPayloadId == signedRequestPayloadId
                     && i.PublicKey == this.gigGossipNode.PublicKey
                     && i.ReplierCertificateId == replierCertificateId
                     select i).FirstOrDefault() != null)
                {
                    TL.Warning("GigStatus already monitored");
                    TX.Commit();
                    return TL.Ret(false);
                }
            }

            await AttachMonitorGigStatusAsync(serviceUri);

            var obj = new MonitoredGigStatusRow
            {
                PublicKey = this.gigGossipNode.PublicKey,
                ReplierCertificateId = replierCertificateId,
                ServiceUri = serviceUri,
                SignedRequestPayloadId = signedRequestPayloadId,
                Data = data,
                Status =  GigStatus.Open,
                SymmetricKey = null
            };

            {
                using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                gigGossipNode.NodeDb.Context.INSERT(obj).SAVE();
                TX.Commit();
            }

            return TL.Ret(true);
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
        {
            List<MonitoredPreimageRow> pToMon;
            {
                using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                pToMon = (from i in gigGossipNode.NodeDb.Context.MonitoredPreimages
                              where i.PublicKey == this.gigGossipNode.PublicKey
                              && i.Preimage == null
                              select i).ToList();
                TX.Commit();
            }

            foreach (var kv in pToMon)
            {
                TL.Iteration(kv);
                try
                {
                    var serviceUri = kv.ServiceUri;
                    var phash = kv.PaymentHash;
                    var preimage = SettlerAPIResult.Get<string>(await gigGossipNode.SettlerSelector.GetSettlerClient(serviceUri).RevealPreimageAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), phash, CancellationTokenSource.Token));
                    if (!string.IsNullOrWhiteSpace(preimage))
                    {
                        TL.Info("OnPreimageRevealedAsync");
                        await gigGossipNode.OnPreimageRevealedAsync(serviceUri, phash, preimage, CancellationTokenSource.Token);
                        kv.Preimage = preimage;
                        using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                        gigGossipNode.NodeDb.Context.UPDATE(kv).SAVE();
                        TX.Commit();
                    }
                    else
                        await AttachMonitorPreimageAsync(serviceUri);
                }
                catch (Exception ex)
                {
                    TL.Exception(ex);
                }
            }
        }
        {
            List<MonitoredGigStatusRow> kToMon;
            {
                using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                kToMon = (from i in gigGossipNode.NodeDb.Context.MonitoredGigStatuses
                              where i.PublicKey == this.gigGossipNode.PublicKey
                              && (i.SymmetricKey == null || (i.Status != GigStatus.Cancelled && i.Status != GigStatus.Completed))
                              select i).ToList();
                TX.Commit();
            }

            foreach (var kv in kToMon)
            {
                TL.Iteration(kv);
                try
                {
                    var serviceUri = kv.ServiceUri;
                    var signedReqestPayloadId = kv.SignedRequestPayloadId;
                    var stat = SettlerAPIResult.Get<GigStatusKey>(await gigGossipNode.SettlerSelector.GetSettlerClient(serviceUri).GetGigStatusAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), signedReqestPayloadId, kv.ReplierCertificateId, CancellationTokenSource.Token));
                    if (!string.IsNullOrWhiteSpace(stat.SymmetricKey))
                    {
                        var status = stat.Status;
                        var key = stat.SymmetricKey;
                        if (status ==  GigStatus.Accepted|| status == GigStatus.Completed || status == GigStatus.Disuputed)
                        {
                            TL.Info("OnSymmetricKeyRevealed");
                            Task.Run(() => gigGossipNode.OnSymmetricKeyRevealed(kv.Data.ToArray(), new string(key)));
                            kv.SymmetricKey = key;
                            kv.Status = status;
                            using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                            gigGossipNode.NodeDb.Context.UPDATE(kv).SAVE();
                            TX.Commit();
                        }
                        else if (status == GigStatus.Cancelled)
                        {
                            TL.Info("OnGigCancelled");
                            Task.Run(() => gigGossipNode.OnGigCancelled(kv.Data.ToArray()));
                            kv.Status = status;
                            using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                            gigGossipNode.NodeDb.Context.UPDATE(kv).SAVE();
                            TX.Commit();
                        }
                        else
                            await AttachMonitorGigStatusAsync(serviceUri);
                    }
                }
                catch (Exception ex)
                {
                    TL.Exception(ex);
                }
            }
        }
    }

    public async Task AttachMonitorPreimageAsync(Uri serviceUri)
    {
        using var TL = TRACE.Log().Args(serviceUri);
        try
        {
            lock (alreadyMonitoredPreimage)
            {
                if (alreadyMonitoredPreimage.Contains(serviceUri))
                {
                    TL.Info("Already monitored");
                    return;
                }
                alreadyMonitoredPreimage.Add(serviceUri);
            }

            HubMonitor hubMonitor = new HubMonitor();
            hubMonitor.OnServerConnectionState += HubMonitor_OnServerConnectionState;

            await hubMonitor.StartAsync(async () =>
                {
                    gigGossipNode.SettlerSelector.RemoveSettlerClient(serviceUri);
                    gigGossipNode.DisposePreimageRevealClient(serviceUri);
                    gigGossipNode.DisposeGigStatusClient(serviceUri);
                }, async () =>
                {
                    {
                        List<MonitoredPreimageRow> pToMon;
                        {
                            using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                            pToMon = (from i in gigGossipNode.NodeDb.Context.MonitoredPreimages
                                          where i.PublicKey == this.gigGossipNode.PublicKey
                                          && i.Preimage == null
                                          && i.ServiceUri == serviceUri
                                          select i).ToList();
                            TX.Commit();
                        }
                        foreach (var kv in pToMon)
                        {
                            TL.Iteration(kv);
                            var phash = kv.PaymentHash;
                            var preimage = SettlerAPIResult.Get<string>(await gigGossipNode.SettlerSelector.GetSettlerClient(serviceUri).RevealPreimageAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), phash, CancellationTokenSource.Token));
                            if (!string.IsNullOrWhiteSpace(preimage))
                            {
                                TL.Info("OnPreimageRevealedAsync");
                                await gigGossipNode.OnPreimageRevealedAsync(serviceUri, phash, preimage, CancellationTokenSource.Token);
                                kv.Preimage = preimage;
                                using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                                gigGossipNode.NodeDb.Context.UPDATE(kv).SAVE();
                                TX.Commit();
                            }
                        }
                    }

                    await foreach (var preimupd in (await this.gigGossipNode.GetPreimageRevealClientAsync(serviceUri)).StreamAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), CancellationTokenSource.Token))
                    {
                        TL.Iteration(preimupd);
                        var payhash = preimupd.PaymentHash;
                        var preimage = preimupd.Preimage;

                        MonitoredPreimageRow? pToMon;
                        {
                            using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                            pToMon = (from i in gigGossipNode.NodeDb.Context.MonitoredPreimages
                                          where i.PublicKey == this.gigGossipNode.PublicKey
                                          && i.PaymentHash == payhash
                                          && i.Preimage == null
                                          select i).FirstOrDefault();
                            TX.Commit();
                        }
                        if (pToMon != null)
                        {
                            TL.Info("OnPreimageRevealedAsync");
                            await gigGossipNode.OnPreimageRevealedAsync(pToMon.ServiceUri, pToMon.PaymentHash, preimage, CancellationTokenSource.Token);
                            pToMon.Preimage = preimage;
                            using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                            gigGossipNode.NodeDb.Context.UPDATE(pToMon).SAVE();
                            TX.Commit();
                        }
                        else
                            TL.Warning("Preimage not monitored");
                    }
                },
                serviceUri,
                gigGossipNode.RetryPolicy,
                CancellationTokenSource.Token);

            lock (monitoredHubs)
                monitoredHubs.Add(hubMonitor);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task AttachMonitorGigStatusAsync(Uri serviceUri)
    {
        using var TL = TRACE.Log().Args(serviceUri);
        try
        {
            lock (alreadyMonitoredSymmetricKey)
            {
                if (alreadyMonitoredSymmetricKey.Contains(serviceUri))
                {
                    TL.Info("Already monitored");
                    return;
                }
                alreadyMonitoredSymmetricKey.Add(serviceUri);
            }

            HubMonitor hubMonitor = new HubMonitor();
            hubMonitor.OnServerConnectionState += HubMonitor_OnServerConnectionState;
            await hubMonitor.StartAsync(async () =>
            {
                gigGossipNode.DisposeGigStatusClient(serviceUri);
                gigGossipNode.DisposePreimageRevealClient(serviceUri);
                gigGossipNode.SettlerSelector.RemoveSettlerClient(serviceUri);
            }, async () =>
            {

                {
                    List<MonitoredGigStatusRow> kToMon;
                    {
                        using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                        kToMon = (from i in gigGossipNode.NodeDb.Context.MonitoredGigStatuses
                                      where i.PublicKey == this.gigGossipNode.PublicKey
                                      && i.SymmetricKey == null
                                      && i.ServiceUri == serviceUri
                                      select i).ToList();
                        TX.Commit();
                    }

                    foreach (var kv in kToMon)
                    {
                        TL.Iteration(kv);
                        var signedRequestPayloadId = kv.SignedRequestPayloadId;
                        var stat = SettlerAPIResult.Get<GigStatusKey>(await gigGossipNode.SettlerSelector.GetSettlerClient(serviceUri).GetGigStatusAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), signedRequestPayloadId, kv.ReplierCertificateId, CancellationTokenSource.Token));
                        if (!string.IsNullOrWhiteSpace(stat.SymmetricKey))
                        {
                            var status = stat.Status;
                            var key = stat.SymmetricKey;
                            if (status ==  GigStatus.Accepted)
                            {
                                TL.Info("OnSymmetricKeyRevealed");
                                gigGossipNode.OnSymmetricKeyRevealed(kv.Data, key);
                                kv.SymmetricKey = key;
                                using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                                gigGossipNode.NodeDb.Context.UPDATE(kv).SAVE();
                                TX.Commit();
                            }
                            else if (status ==  GigStatus.Cancelled)
                            {
                                TL.Info("OnGigCancelled");
                                gigGossipNode.OnGigCancelled(kv.Data);
                                kv.Status = status;
                                using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                                gigGossipNode.NodeDb.Context.UPDATE(kv).SAVE();
                                TX.Commit();
                            }
                        }
                    }
                }

                await foreach (var symkeyupd in (await this.gigGossipNode.GetGigStatusClientAsync(serviceUri)).StreamAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), CancellationTokenSource.Token))
                {
                    TL.Iteration(symkeyupd);
                    var gigId = symkeyupd.JobRequestId;
                    var repliercertificateid = symkeyupd.JobReplyId;
                    var status = symkeyupd.Status;
                    if (status == GigStatus.Accepted)
                    {
                        var symkey = symkeyupd.SymmetricKey;
                        MonitoredGigStatusRow? kToMon;
                        {
                            using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                            kToMon = (from i in gigGossipNode.NodeDb.Context.MonitoredGigStatuses
                                          where i.PublicKey == this.gigGossipNode.PublicKey
                                          && i.SignedRequestPayloadId == gigId
                                          && i.ReplierCertificateId == repliercertificateid
                                          && i.SymmetricKey == null
                                          select i).FirstOrDefault();
                            TX.Commit();
                        }

                        if (kToMon != null)
                        {
                            TL.Info("OnSymmetricKeyRevealed");
                            gigGossipNode.OnSymmetricKeyRevealed(kToMon.Data, symkey);
                            kToMon.SymmetricKey = symkey;
                            kToMon.Status = status;
                            using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                            gigGossipNode.NodeDb.Context.UPDATE(kToMon).SAVE();
                            TX.Commit();
                        }
                        else
                            TL.Warning("Accepted GigStatus not monitored");
                    }
                    else if (status ==  GigStatus.Cancelled)
                    {
                        MonitoredGigStatusRow? kToMon;
                        {
                            using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                            kToMon = (from i in gigGossipNode.NodeDb.Context.MonitoredGigStatuses
                                      where i.PublicKey == this.gigGossipNode.PublicKey
                                      && i.SignedRequestPayloadId == gigId
                                      && i.ReplierCertificateId == repliercertificateid
                                      && i.Status != GigStatus.Cancelled
                                      select i).FirstOrDefault();
                            TX.Commit();
                        }

                        if (kToMon != null)
                        {
                            TL.Info("OnGigCancelled");
                            gigGossipNode.OnGigCancelled(kToMon.Data);
                            kToMon.Status = status;
                            using var TX = gigGossipNode.NodeDb.Context.BEGIN_TRANSACTION();
                            gigGossipNode.NodeDb.Context.UPDATE(kToMon).SAVE();
                            TX.Commit();
                        }
                        else
                            TL.Warning("Cancelled GigStatus not monitored");
                    }
                }
            },
            serviceUri,
            gigGossipNode.RetryPolicy,
            CancellationTokenSource.Token);

            lock (monitoredHubs)
                monitoredHubs.Add(hubMonitor);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    private void HubMonitor_OnServerConnectionState(object? sender, ServerConnectionStateEventArgs e)
    {
        using var TL = TRACE.Log().Args(e);
        try
        {
            gigGossipNode.FireOnServerConnectionState(ServerConnectionSource.SettlerAPI, e.State, e.Uri);
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
            lock (monitoredHubs)
            {
                foreach (var hubMonitor in monitoredHubs)
                {
                    hubMonitor.OnServerConnectionState -= HubMonitor_OnServerConnectionState;
                    hubMonitor.Stop(CancellationTokenSource);
                }
                monitoredHubs = new();
            }
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
        }
    }
}

