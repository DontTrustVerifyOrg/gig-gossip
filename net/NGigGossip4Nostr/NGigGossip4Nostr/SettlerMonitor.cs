using System;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using CryptoToolkit;
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
            if ((from i in gigGossipNode.nodeContext.Value.MonitoredPreimages
                 where i.PaymentHash == phash && i.PublicKey == this.gigGossipNode.PublicKey
                 select i).FirstOrDefault() != null)
            {
                TL.Warning("Preimage already monitored");
                return TL.Ret(false);
            }

            await AttachMonitorPreimageAsync(serviceUri);

            var obj = new MonitoredPreimageRow()
            {
                PublicKey = this.gigGossipNode.PublicKey,
                ServiceUri = serviceUri,
                PaymentHash = phash,
                Preimage = null
            };

            gigGossipNode.nodeContext.Value.AddObject(obj);
            try
            {
                await (await this.gigGossipNode.GetPreimageRevealClientAsync(serviceUri)).MonitorAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), phash, CancellationTokenSource.Token);
            }
            catch
            {
                gigGossipNode.nodeContext.Value.RemoveObject(obj);
                throw;
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
            if ((from i in gigGossipNode.nodeContext.Value.MonitoredGigStatuses
                 where i.SignedRequestPayloadId == signedRequestPayloadId
                 && i.PublicKey == this.gigGossipNode.PublicKey
                 && i.ReplierCertificateId == replierCertificateId
                 select i).FirstOrDefault() != null)
            {
                TL.Warning("GigStatus already monitored");
                return TL.Ret(false);
            }

            await AttachMonitorGigStatusAsync(serviceUri);

            var obj = new MonitoredGigStatusRow
            {
                PublicKey = this.gigGossipNode.PublicKey,
                ReplierCertificateId = replierCertificateId,
                ServiceUri = serviceUri,
                SignedRequestPayloadId = signedRequestPayloadId,
                Data = data,
                Status = "",
                SymmetricKey = null
            };
            gigGossipNode.nodeContext.Value.AddObject(obj);
            try
            {
                await (await this.gigGossipNode.GetGigStatusClientAsync(serviceUri)).MonitorAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), signedRequestPayloadId, replierCertificateId, CancellationTokenSource.Token);
            }
            catch
            {
                gigGossipNode.nodeContext.Value.RemoveObject(obj);
                throw;
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
            var pToMon = (from i in gigGossipNode.nodeContext.Value.MonitoredPreimages
                          where i.PublicKey == this.gigGossipNode.PublicKey
                          && i.Preimage == null
                          select i).ToList();

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
                        gigGossipNode.nodeContext.Value.SaveObject(kv);
                    }
                    else
                        await AttachMonitorPreimageAsync(serviceUri);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }
        {
            var kToMon = (from i in gigGossipNode.nodeContext.Value.MonitoredGigStatuses
                          where i.PublicKey == this.gigGossipNode.PublicKey
                          && (i.SymmetricKey == null || (i.Status != "Cancelled" && i.Status != "Completed"))
                          select i).ToList();

            foreach (var kv in kToMon)
            {
                TL.Iteration(kv);
                try
                {
                    var serviceUri = kv.ServiceUri;
                    var signedReqestPayloadId = kv.SignedRequestPayloadId;
                    var stat = SettlerAPIResult.Get<string>(await gigGossipNode.SettlerSelector.GetSettlerClient(serviceUri).GetGigStatusAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), signedReqestPayloadId, kv.ReplierCertificateId, CancellationTokenSource.Token));
                    if (!string.IsNullOrWhiteSpace(stat))
                    {
                        var prts = stat.Split('|');
                        var status = prts[0];
                        var key = prts[1];
                        if (status == "Accepted" || status == "Completed" || status == "Disputed")
                        {
                            TL.Info("OnSymmetricKeyRevealed");
                            gigGossipNode.OnSymmetricKeyRevealed(kv.Data, key);
                            kv.SymmetricKey = key;
                            kv.Status = status;
                            gigGossipNode.nodeContext.Value.SaveObject(kv);
                        }
                        else if (status == "Cancelled")
                        {
                            TL.Info("OnGigCancelled");
                            gigGossipNode.OnGigCancelled(kv.Data);
                            kv.Status = status;
                            gigGossipNode.nodeContext.Value.SaveObject(kv);
                        }
                        else
                            await AttachMonitorGigStatusAsync(serviceUri);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
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
                        var pToMon = (from i in gigGossipNode.nodeContext.Value.MonitoredPreimages
                                      where i.PublicKey == this.gigGossipNode.PublicKey
                                      && i.Preimage == null
                                      && i.ServiceUri == serviceUri
                                      select i).ToList();

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
                                gigGossipNode.nodeContext.Value.SaveObject(kv);
                            }
                        }
                    }

                    await foreach (var preimupd in (await this.gigGossipNode.GetPreimageRevealClientAsync(serviceUri)).StreamAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), CancellationTokenSource.Token))
                    {
                        TL.Iteration(preimupd);
                        var pp = preimupd.Split('|');
                        var payhash = pp[0];
                        var preimage = pp[1];

                        var pToMon = (from i in gigGossipNode.nodeContext.Value.MonitoredPreimages
                                      where i.PublicKey == this.gigGossipNode.PublicKey
                                      && i.PaymentHash == payhash
                                      && i.Preimage == null
                                      select i).FirstOrDefault();
                        if (pToMon != null)
                        {
                            TL.Info("OnPreimageRevealedAsync");
                            await gigGossipNode.OnPreimageRevealedAsync(pToMon.ServiceUri, pToMon.PaymentHash, preimage, CancellationTokenSource.Token);
                            pToMon.Preimage = preimage;
                            gigGossipNode.nodeContext.Value.SaveObject(pToMon);
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
                    var kToMon = (from i in gigGossipNode.nodeContext.Value.MonitoredGigStatuses
                                  where i.PublicKey == this.gigGossipNode.PublicKey
                                  && i.SymmetricKey == null
                                  && i.ServiceUri == serviceUri
                                  select i).ToList();

                    foreach (var kv in kToMon)
                    {
                        TL.Iteration(kv);
                        var signedRequestPayloadId = kv.SignedRequestPayloadId;
                        var stat = SettlerAPIResult.Get<string>(await gigGossipNode.SettlerSelector.GetSettlerClient(serviceUri).GetGigStatusAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), signedRequestPayloadId, kv.ReplierCertificateId, CancellationTokenSource.Token));
                        if (!string.IsNullOrWhiteSpace(stat))
                        {
                            var prts = stat.Split('|');
                            var status = prts[0];
                            var key = prts[1];
                            if (status == "Accepted")
                            {
                                TL.Info("OnSymmetricKeyRevealed");
                                gigGossipNode.OnSymmetricKeyRevealed(kv.Data, key);
                                kv.SymmetricKey = key;
                                gigGossipNode.nodeContext.Value.SaveObject(kv);
                            }
                            else if (status == "Cancelled")
                            {
                                TL.Info("OnGigCancelled");
                                gigGossipNode.OnGigCancelled(kv.Data);
                                kv.Status = status;
                                gigGossipNode.nodeContext.Value.SaveObject(kv);
                            }
                        }
                    }
                }

                await foreach (var symkeyupd in (await this.gigGossipNode.GetGigStatusClientAsync(serviceUri)).StreamAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), CancellationTokenSource.Token))
                {
                    TL.Iteration(symkeyupd);
                    var pp = symkeyupd.Split('|');
                    var gigId = Guid.Parse(pp[0]);
                    var repliercertificateid = Guid.Parse(pp[1]);
                    var status = pp[2];
                    if (status == "Accepted")
                    {
                        var symkey = pp[3];
                        var kToMon = (from i in gigGossipNode.nodeContext.Value.MonitoredGigStatuses
                                      where i.PublicKey == this.gigGossipNode.PublicKey
                                      && i.SignedRequestPayloadId == gigId
                                      && i.ReplierCertificateId == repliercertificateid
                                      && i.SymmetricKey == null
                                      select i).FirstOrDefault();

                        if (kToMon != null)
                        {
                            TL.Info("OnSymmetricKeyRevealed");
                            gigGossipNode.OnSymmetricKeyRevealed(kToMon.Data, symkey);
                            kToMon.SymmetricKey = symkey;
                            kToMon.Status = status;
                            gigGossipNode.nodeContext.Value.SaveObject(kToMon);
                        }
                        else
                            TL.Warning("Accepted GigStatus not monitored");
                    }
                    else if (status == "Cancelled")
                    {
                        var kToMon = (from i in gigGossipNode.nodeContext.Value.MonitoredGigStatuses
                                      where i.PublicKey == this.gigGossipNode.PublicKey
                                      && i.SignedRequestPayloadId == gigId
                                      && i.ReplierCertificateId == repliercertificateid
                                      && i.Status != "Cancelled"
                                      select i).FirstOrDefault();

                        if (kToMon != null)
                        {
                            TL.Info("OnGigCancelled");
                            gigGossipNode.OnGigCancelled(kToMon.Data);
                            kToMon.Status = status;
                            gigGossipNode.nodeContext.Value.SaveObject(kToMon);
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
        using var TL = TRACE.Log().Args(sender, e);
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

