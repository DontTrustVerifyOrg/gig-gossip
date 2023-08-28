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
		Dictionary<Tuple<Uri, string>, string> monitoredPreimages = new();
		Dictionary<Tuple<Uri, Guid>, string> monitoredKeys = new();
		Dictionary<Guid, byte[]> monitoredKeysActions = new();
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
			return true; 
        }


		Thread monitorThread;
        long _monitorThreadStop;

        public void Start()
		{
            _monitorThreadStop = 0;
            monitorThread = new Thread(async () =>
			{
				while (Interlocked.Read(ref _monitorThreadStop) == 0)
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
							var preimage = await gigGossipNode.SettlerSelector.GetSettlerClient(serviceUri).RevealPreimageAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), phash);
                            if (!string.IsNullOrWhiteSpace(preimage))
                            {
                                gigGossipNode.OnPreimageRevealed(preimage);
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
							var key = await gigGossipNode.SettlerSelector.GetSettlerClient(serviceUri).RevealSymmetricKeyAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), tid.ToString(), kv.ReplierPublicKey);
							if (!string.IsNullOrWhiteSpace(key))
							{
                                gigGossipNode.OnSymmetricKeyRevealed(kv.Data, key);
                                kv.SymmetricKey = key;
								gigGossipNode.nodeContext.Value.SaveObject(kv);
							}
						}
					}

					Thread.Sleep(1000);
				}
			});
			monitorThread.Start();
		}

        public void Stop()
        {
            Interlocked.Add(ref _monitorThreadStop, 1);
            monitorThread.Join();
        }
    }
}

