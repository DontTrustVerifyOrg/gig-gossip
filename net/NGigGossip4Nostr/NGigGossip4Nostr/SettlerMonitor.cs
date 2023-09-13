using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using CryptoToolkit;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.Secp256k1;
using Newtonsoft.Json.Linq;
using static System.Collections.Specialized.BitVector32;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace NGigGossip4Nostr
{
    public interface ISettlerMonitorEvents
    {
        public void OnPreimageRevealed(Uri settlerUri, string paymentHash, string preimage);
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

        public bool MonitorPreimage(Uri serviceUri, string phash)
        {
            if ((from i in gigGossipNode.nodeContext.Value.MonitoredPreimages
                 where i.PaymentHash == phash && i.PublicKey == this.gigGossipNode.PublicKey
                 select i).FirstOrDefault() != null)
                return false;

            lock (alreadyMonitoredPreimage)
                if (!alreadyMonitoredPreimage.Contains(serviceUri))
                {
                    alreadyMonitoredPreimage.Add(serviceUri);
                    AttachMonitorPreimage(serviceUri);
                }

            gigGossipNode.nodeContext.Value.AddObject(
                new MonitoredPreimageRow()
                {
                    PublicKey = this.gigGossipNode.PublicKey,
                    ServiceUri = serviceUri,
                    PaymentHash = phash,
                    Preimage = null
                });
            this.gigGossipNode.GetPreimageRevealClient(serviceUri).Monitor(this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri).Result, phash);
            return true;
        }

        public bool MonitorSymmetricKey(Uri serviceUri, Guid tid, string replierPublicKey, byte[] data)
        {
            if ((from i in gigGossipNode.nodeContext.Value.MonitoredSymmetricKeys
                 where i.PayloadId == tid && i.PublicKey == this.gigGossipNode.PublicKey
                 select i).FirstOrDefault() != null)
                return false;

            lock (alreadyMonitoredSymmetricKey)
                if (!alreadyMonitoredSymmetricKey.Contains(serviceUri))
                {
                    alreadyMonitoredSymmetricKey.Add(serviceUri);
                    AttachMonitorSymmetricKey(serviceUri);
                }

            gigGossipNode.nodeContext.Value.AddObject(
                new MonitoredSymmetricKeyRow
                {
                    PublicKey = this.gigGossipNode.PublicKey,
                    ReplierPublicKey = replierPublicKey,
                    ServiceUri = serviceUri,
                    PayloadId = tid,
                    Data = data,
                    SymmetricKey = null
                });
            this.gigGossipNode.GetSymmetricKeyRevealClient(serviceUri).Monitor(this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri).Result, tid, replierPublicKey);
            return true;
        }

        CancellationTokenSource CancellationTokenSource = new();

        List<Thread> monitoringThreads = new List<Thread>();

        public void Start()
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
                    var preimage = gigGossipNode.SettlerSelector.GetSettlerClient(serviceUri).RevealPreimageAsync(this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri).Result, phash).Result;
                    if (!string.IsNullOrWhiteSpace(preimage))
                    {
                        gigGossipNode.OnPreimageRevealed(serviceUri, phash, preimage);
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
                    var tid = kv.PayloadId;
                    var key = gigGossipNode.SettlerSelector.GetSettlerClient(serviceUri).RevealSymmetricKeyAsync(this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri).Result, tid.ToString(), kv.ReplierPublicKey).Result;
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        gigGossipNode.OnSymmetricKeyRevealed(kv.Data, key);
                        kv.SymmetricKey = key;
                        gigGossipNode.nodeContext.Value.SaveObject(kv);
                    }
                }
            }
        }

        public void AttachMonitorPreimage(Uri settlerUri)
        {
            var preimthread = new Thread(async () =>
            {
                while (true)
                {
                    try
                    {
                        await foreach (var preimupd in this.gigGossipNode.GetPreimageRevealClient(settlerUri).StreamAsync(this.gigGossipNode.MakeSettlerAuthTokenAsync(settlerUri).Result, CancellationTokenSource.Token))
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
                                gigGossipNode.OnPreimageRevealed(pToMon.ServiceUri,pToMon.PaymentHash, preimage);
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
                    catch(TimeoutException)
                    {
                        Trace.TraceWarning("Timeout on "+settlerUri.AbsolutePath+ "/revealpreimage, reconnecting");
                        //reconnect
                    }
                }
            });


            lock (monitoringThreads)
                monitoringThreads.Add(preimthread);
            preimthread.Start();
        }

        public void AttachMonitorSymmetricKey(Uri settlerUri)
        {
            var symkeythread = new Thread(async () =>
            {
                while (true)
                {
                    try
                    {
                        await foreach (var symkeyupd in this.gigGossipNode.GetSymmetricKeyRevealClient(settlerUri).StreamAsync(this.gigGossipNode.MakeSettlerAuthTokenAsync(settlerUri).Result, CancellationTokenSource.Token))
                        {
                            var pp = symkeyupd.Split('|');
                            var gigId = Guid.Parse(pp[0]);
                            var replierid = pp[1];
                            var symkey = pp[2];
                            var kToMon = (from i in gigGossipNode.nodeContext.Value.MonitoredSymmetricKeys
                                          where i.PublicKey == this.gigGossipNode.PublicKey
                                          && i.PayloadId == gigId
                                          && i.ReplierPublicKey == replierid
                                          && i.SymmetricKey == null
                                          select i).FirstOrDefault();

                            if (kToMon != null)
                            {
                                FlowLogger.NewMessage(Encoding.Default.GetBytes(settlerUri.AbsoluteUri).AsHex(), gigId.ToString() + "_" + replierid, "reveal");
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
                    catch (TimeoutException)
                    {
                        Trace.TraceWarning("Timeout on " + settlerUri.AbsolutePath + "/revealsymmetrickey, reconnecting");
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
}

