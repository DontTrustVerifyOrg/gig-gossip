using System;
using System.IO;
using CryptoToolkit;
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

		public void MonitorPreimage(Uri serviceUri, string phash)
        {
            if ((from i in gigGossipNode.nodeContext.Value.MonitoredPreimages
                 where i.PaymentHash == phash && i.PublicKey == this.gigGossipNode.PublicKey
                 select i).FirstOrDefault() != null)
                return;

            gigGossipNode.nodeContext.Value.MonitoredPreimages.Add(
                new MonitoredPreimageRow()
                {
                    PublicKey = this.gigGossipNode.PublicKey,
                    ServiceUri = serviceUri,
                    PaymentHash = phash,
					Preimage =null
                });
            gigGossipNode.nodeContext.Value.SaveChanges();
		}

		public void MonitorSymmetricKey(Uri serviceUri, Guid tid, byte[] data)
		{
            if ((from i in gigGossipNode.nodeContext.Value.MonitoredSymmetricKeys
                 where i.PayloadId == tid && i.PublicKey == this.gigGossipNode.PublicKey
                 select i).FirstOrDefault() != null)
                return;

            gigGossipNode.nodeContext.Value.MonitoredSymmetricKeys.Add(
                new MonitoredSymmetricKeyRow
                {
                    PublicKey = this.gigGossipNode.PublicKey,
                    ServiceUri = serviceUri,
					PayloadId = tid,
					Data = data,
					SymmetricKey = null
                });
            gigGossipNode.nodeContext.Value.SaveChanges();
        }


		Thread monitorThread;


		public void Start()
		{
			monitorThread = new Thread(async () =>
			{
				while (true)
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
								gigGossipNode.nodeContext.Value.MonitoredPreimages.Update(kv);
								gigGossipNode.nodeContext.Value.SaveChanges();
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
							var key = await gigGossipNode.SettlerSelector.GetSettlerClient(serviceUri).RevealSymmetricKeyAsync(await this.gigGossipNode.MakeSettlerAuthTokenAsync(serviceUri), tid.ToString());
							if (!string.IsNullOrWhiteSpace(key))
							{
                                gigGossipNode.OnSymmetricKeyRevealed(kv.Data, key);
                                kv.SymmetricKey = key;
								gigGossipNode.nodeContext.Value.MonitoredSymmetricKeys.Update(kv);
								gigGossipNode.nodeContext.Value.SaveChanges();
							}
						}
					}

					Thread.Sleep(1000);
				}
			});
			monitorThread.Start();
		}
	}
}

