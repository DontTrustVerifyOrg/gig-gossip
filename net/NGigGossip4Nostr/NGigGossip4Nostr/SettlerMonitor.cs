using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using CryptoToolkit;
using GigGossipSettlerAPIClient;
using Microsoft.EntityFrameworkCore;

namespace NGigGossip4Nostr;

public interface ISettlerMonitorEvents
{
    public Task<bool> OnPreimageRevealedAsync(Uri settlerUri, string paymentHash, string preimage);
    public void OnSymmetricKeyRevealed(byte[] data, string key);
}

public class SettlerMonitor
{
    GigGossipNode gigGossipNode;
    HashSet<Uri> alreadyMonitoredPreimage = new();
    HashSet<Uri> alreadyMonitoredSymmetricKey = new();

    public SettlerMonitor(GigGossipNode gigGossipNode)
    {
        this.gigGossipNode = gigGossipNode;
    }

    public async Task<bool> MonitorPreimageAsync(Uri serviceUri, string phash)
    {
        if ((from i in gigGossipNode.nodeContext.Value.MonitoredPreimages
             where i.PaymentHash == phash && i.PublicKey == this.gigGossipNode.PublicKey
             select i).FirstOrDefault() != null)
            return false;

        AttachMonitorPreimage(serviceUri);

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
            await (await this.gigGossipNode.GetPreimageRevealClientAsync(serviceUri)).MonitorAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), phash);
        }
        catch (Microsoft.AspNetCore.SignalR.HubException)
        {
            gigGossipNode.nodeContext.Value.RemoveObject(obj);
            throw;
        }
        return true;
    }

    public async Task<bool> MonitorSymmetricKeyAsync(Uri serviceUri, Guid senderCertificateId, Guid tid, Guid replierCertificateId, byte[] data)
    {
        if ((from i in gigGossipNode.nodeContext.Value.MonitoredSymmetricKeys
             where i.SignedRequestPayloadId == tid && i.PublicKey == this.gigGossipNode.PublicKey
             select i).FirstOrDefault() != null)
            return false;

        AttachMonitorSymmetricKey(serviceUri);

        var obj = new MonitoredSymmetricKeyRow
        {
            SenderCertificateId = senderCertificateId,
            PublicKey = this.gigGossipNode.PublicKey,
            ReplierCertificateId = replierCertificateId,
            ServiceUri = serviceUri,
            SignedRequestPayloadId = tid,
            Data = data,
            SymmetricKey = null
        };
        gigGossipNode.nodeContext.Value.AddObject(obj);
        try
        {
            await (await this.gigGossipNode.GetSymmetricKeyRevealClientAsync(serviceUri)).MonitorAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), tid, replierCertificateId);
        }
        catch (Microsoft.AspNetCore.SignalR.HubException)
        {
            gigGossipNode.nodeContext.Value.RemoveObject(obj);
            throw;
        }
        return true;
    }

    CancellationTokenSource CancellationTokenSource = new();

    List<Thread> monitoringThreads = new List<Thread>();

    public async Task StartAsync()
    {
        {
            var pToMon = (from i in gigGossipNode.nodeContext.Value.MonitoredPreimages
                          where i.PublicKey == this.gigGossipNode.PublicKey
                          && i.Preimage == null
                          select i).ToList();

            foreach (var kv in pToMon)
            {
                var serviceUri = kv.ServiceUri;
                var phash = kv.PaymentHash;
                var preimage = SettlerAPIResult.Get<string>(await gigGossipNode.SettlerSelector.GetSettlerClient(serviceUri).RevealPreimageAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), phash));
                if (!string.IsNullOrWhiteSpace(preimage))
                {
                    await gigGossipNode.OnPreimageRevealedAsync(serviceUri, phash, preimage);
                    kv.Preimage = preimage;
                    gigGossipNode.nodeContext.Value.SaveObject(kv);
                }
            }
        }
        {
            var kToMon = (from i in gigGossipNode.nodeContext.Value.MonitoredSymmetricKeys
                          where i.PublicKey == this.gigGossipNode.PublicKey
                          && i.SymmetricKey == null
                          select i).ToList();

            foreach (var kv in kToMon)
            {
                var serviceUri = kv.ServiceUri;
                var tid = kv.SignedRequestPayloadId;
                var key = SettlerAPIResult.Get<string>(await gigGossipNode.SettlerSelector.GetSettlerClient(serviceUri).RevealSymmetricKeyAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), kv.SenderCertificateId.ToString(), tid.ToString(), kv.ReplierCertificateId.ToString()));
                if (!string.IsNullOrWhiteSpace(key))
                {
                    gigGossipNode.OnSymmetricKeyRevealed(kv.Data, key);
                    kv.SymmetricKey = key;
                    gigGossipNode.nodeContext.Value.SaveObject(kv);
                }
            }
        }
    }

    public void AttachMonitorPreimage(Uri serviceUri)
    {
        lock (alreadyMonitoredPreimage)
        {
            if (alreadyMonitoredPreimage.Contains(serviceUri))
                return;
            alreadyMonitoredPreimage.Add(serviceUri);
        }

        var preimthread = new Thread(async () =>
        {
            while (true)
            {
                try
                {
                    {
                        var pToMon = (from i in gigGossipNode.nodeContext.Value.MonitoredPreimages
                                      where i.PublicKey == this.gigGossipNode.PublicKey
                                      && i.Preimage == null
                                      && i.ServiceUri == serviceUri
                                      select i).ToList();

                        foreach (var kv in pToMon)
                        {
                            var phash = kv.PaymentHash;
                            var preimage = SettlerAPIResult.Get<string>(await gigGossipNode.SettlerSelector.GetSettlerClient(serviceUri).RevealPreimageAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), phash));
                            if (!string.IsNullOrWhiteSpace(preimage))
                            {
                                await gigGossipNode.OnPreimageRevealedAsync(serviceUri, phash, preimage);
                                kv.Preimage = preimage;
                                gigGossipNode.nodeContext.Value.SaveObject(kv);
                            }
                        }
                    }

                    await foreach (var preimupd in (await this.gigGossipNode.GetPreimageRevealClientAsync(serviceUri)).StreamAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), CancellationTokenSource.Token))
                    {
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
                            await gigGossipNode.OnPreimageRevealedAsync(pToMon.ServiceUri, pToMon.PaymentHash, preimage);
                            pToMon.Preimage = preimage;
                            gigGossipNode.nodeContext.Value.SaveObject(pToMon);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    //stream closed
                    return;
                }
                catch (Exception ex) when (ex is Microsoft.AspNetCore.SignalR.HubException ||
                                           ex is TimeoutException)
                {
                    this.gigGossipNode.DisposePreimageRevealClient(serviceUri);
                    Trace.TraceWarning("Timeout on " + serviceUri.AbsolutePath + "/revealpreimage, reconnecting");
                    Thread.Sleep(1000);
                    //reconnect
                }
            }
        });


        lock (monitoringThreads)
            monitoringThreads.Add(preimthread);
        preimthread.Start();
    }

    public void AttachMonitorSymmetricKey(Uri serviceUri)
    {
        lock (alreadyMonitoredSymmetricKey)
        {
            if (alreadyMonitoredSymmetricKey.Contains(serviceUri))
                return;
            alreadyMonitoredSymmetricKey.Add(serviceUri);
        }

        var symkeythread = new Thread(async () =>
        {
            while (true)
            {
                try
                {
                    {
                        var kToMon = (from i in gigGossipNode.nodeContext.Value.MonitoredSymmetricKeys
                                      where i.PublicKey == this.gigGossipNode.PublicKey
                                      && i.SymmetricKey == null
                                      && i.ServiceUri == serviceUri
                                      select i).ToList();

                        foreach (var kv in kToMon)
                        {
                            var tid = kv.SignedRequestPayloadId;
                            var key = SettlerAPIResult.Get<string>(await gigGossipNode.SettlerSelector.GetSettlerClient(serviceUri).RevealSymmetricKeyAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), kv.SenderCertificateId.ToString(), tid.ToString(), kv.ReplierCertificateId.ToString()));
                            if (!string.IsNullOrWhiteSpace(key))
                            {
                                gigGossipNode.OnSymmetricKeyRevealed(kv.Data, key);
                                kv.SymmetricKey = key;
                                gigGossipNode.nodeContext.Value.SaveObject(kv);
                            }
                        }
                    }

                    await foreach (var symkeyupd in (await this.gigGossipNode.GetSymmetricKeyRevealClientAsync(serviceUri)).StreamAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), CancellationTokenSource.Token))
                    {
                        var pp = symkeyupd.Split('|');
                        var gigId = Guid.Parse(pp[0]);
                        var repliercertificateid = Guid.Parse(pp[1]);
                        var symkey = pp[2];
                        var kToMon = (from i in gigGossipNode.nodeContext.Value.MonitoredSymmetricKeys
                                      where i.PublicKey == this.gigGossipNode.PublicKey
                                      && i.SignedRequestPayloadId == gigId
                                      && i.ReplierCertificateId == repliercertificateid
                                      && i.SymmetricKey == null
                                      select i).FirstOrDefault();

                        if (kToMon != null)
                        {
                            gigGossipNode.OnSymmetricKeyRevealed(kToMon.Data, symkey);
                            kToMon.SymmetricKey = symkey;
                            gigGossipNode.nodeContext.Value.SaveObject(kToMon);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    //stream closed
                    return;
                }
                catch (Exception ex) when (ex is Microsoft.AspNetCore.SignalR.HubException ||
                                           ex is TimeoutException)
                {
                    this.gigGossipNode.DisposeSymmetricKeyRevealClient(serviceUri);
                    Trace.TraceWarning("Timeout on " + serviceUri.AbsolutePath + "/revealsymmetrickey, reconnecting");
                    Thread.Sleep(1000);
                    //reconnect
                }
            }
        });

        lock (monitoringThreads)
            monitoringThreads.Add(symkeythread);
        symkeythread.Start();
    }

    public void Stop()
    {
        CancellationTokenSource.Cancel();
        lock (monitoringThreads)
        {
            foreach (var thread in monitoringThreads)
                thread.Join();
        }
    }
}

