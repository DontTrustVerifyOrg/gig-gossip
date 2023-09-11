using System;
using System.IO;
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
		public void OnPreimageRevealed(string preimage);
		public void OnSymmetricKeyRevealed(byte[] data, string key);
    }

    public class SettlerMonitor
	{
		GigGossipNode gigGossipNode;

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

            gigGossipNode.nodeContext.Value.AddObject(
                new MonitoredPreimageRow()
                {
                    PublicKey = this.gigGossipNode.PublicKey,
                    ServiceUri = serviceUri,
                    PaymentHash = phash,
					Preimage =null
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
            this.gigGossipNode.GetSymmetricKeyRevealClient(serviceUri).Monitor(this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri).Result, tid);
            return true; 
        }

        CancellationTokenSource CancellationTokenSource = new();

        List<Thread> monitoringThreads = new List<Thread>();

        public void Attach(Uri settlerUri)
        {
            var preimthread = new Thread(async () =>
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
                            gigGossipNode.OnPreimageRevealed(preimage);
                            pToMon.Preimage = preimage;
                            gigGossipNode.nodeContext.Value.SaveObject(pToMon);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    //stream closed
                }
            });

            var symkeythread = new Thread(async () =>
            {

                try
                {
                    await foreach (var symkeyupd in this.gigGossipNode.GetSymmetricKeyRevealClient(settlerUri).StreamAsync(this.gigGossipNode.MakeSettlerAuthTokenAsync(settlerUri).Result, CancellationTokenSource.Token))
                    {
                        var pp = symkeyupd.Split('|');
                        var gigId = Guid.Parse(pp[0]);
                        var symkey = pp[1];
                        var kToMon = (from i in gigGossipNode.nodeContext.Value.MonitoredSymmetricKeys
                                      where i.PublicKey == this.gigGossipNode.PublicKey
                                      && i.PayloadId == gigId
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
                }

            });

            lock (monitoringThreads)
            {
                monitoringThreads.Add(preimthread);
                monitoringThreads.Add(symkeythread);
            }
            preimthread.Start();
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

