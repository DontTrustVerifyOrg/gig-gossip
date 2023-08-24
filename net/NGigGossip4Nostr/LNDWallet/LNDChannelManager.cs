using System;
using System.Diagnostics;
using Grpc.Core;
using LNDClient;
using Lnrpc;

namespace LNDWallet;

public class LNDChannelManager
{
	LNDWalletManager walletManager;
	List<string> nearbyNodes;
	long maxSatoshisPerChannel;
	long estimatedTxFee;


    public LNDChannelManager(LNDWalletManager walletManager, List<string> nearbyNodes, long maxSatoshisPerChannel, long estimatedTxFee)
	{
		this.walletManager = walletManager;
		this.nearbyNodes = nearbyNodes;
		this.maxSatoshisPerChannel = maxSatoshisPerChannel;
		this.estimatedTxFee = estimatedTxFee;
	}

    Thread mainThread;
    private long _mainThreadStop;

    public void Start()
	{
		_mainThreadStop = 0;
        mainThread = new Thread(() =>
		{
			var peersof2 = new HashSet<string>(from p in walletManager.ListPeers().Peers select p.PubKey + "@" + p.Address.Replace("127.0.0.1", "localhost"));

			foreach (var friend in nearbyNodes)
			{
				try
				{
					if (!peersof2.Contains(friend.Replace("127.0.0.1", "localhost")))
						walletManager.Connect(friend.Replace("127.0.0.1", "localhost"));
				}
				catch (Exception ex)
				{

				}
			}
			while (Interlocked.Read(ref _mainThreadStop) == 0)
			{
				foreach (var friend in nearbyNodes)
					GoForOpeningNewChannelsForNodeAsync(friend.Split("@")[0], maxSatoshisPerChannel, estimatedTxFee).Wait();
				GoForExecutingPayoutsAsync(estimatedTxFee).Wait();

				Thread.Sleep(1000);
			}
		});
		mainThread.Start();
	}


	public void Stop()
	{
        Interlocked.Add(ref _mainThreadStop, 1);
        mainThread.Join();
	}

	public async Task GoForOpeningNewChannelsForNodeAsync(string nodePubKey, long maxSatoshisPerChannel, long estimatedTxFee)
	{
        var activeFunding = walletManager.GetChannelFundingBalance(6);
        if (activeFunding > 0)
            try
            {
                var ocs=walletManager.OpenChannel(nodePubKey, activeFunding - estimatedTxFee > maxSatoshisPerChannel? maxSatoshisPerChannel - estimatedTxFee:-1);
				while(await ocs.ResponseStream.MoveNext())
				{
					if (ocs.ResponseStream.Current.UpdateCase == OpenStatusUpdate.UpdateOneofCase.ChanOpen)
						return;
				}
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
            }
	}

	public async Task GoForExecutingPayoutsAsync(long estimatedTxFee)
	{
		var pendingPayouts = walletManager.GetAllPendingPayouts();
		var totalAmountNeeded = (from pp in pendingPayouts select (long)pp.Satoshis - (long)pp.TxFee).Sum();
        var fundbal = walletManager.GetChannelFundingBalance(6);

		if (fundbal < totalAmountNeeded)
		{
			var sortedChannelsByLocalBalance = (walletManager.ListChannels(true).Channels).ToArray().OrderBy((c) => c.LocalBalance).ToArray();
			long freedAmount = fundbal;
			List<AsyncServerStreamingCall<CloseStatusUpdate>> updateStreams = new();

			foreach (var channel in sortedChannelsByLocalBalance)
			{
				updateStreams.Add(walletManager.CloseChannel(channel.ChannelPoint));
				freedAmount += channel.LocalBalance - estimatedTxFee;
				if (freedAmount > totalAmountNeeded)
					break;
			}

			foreach(var us in updateStreams)
			{
                while (await us.ResponseStream.MoveNext())
                {
                    if (us.ResponseStream.Current.UpdateCase == CloseStatusUpdate.UpdateOneofCase.ChanClose)
                        return;
                }
            }

        }

		foreach (var payout in pendingPayouts)
		{
            var tx = walletManager.SendCoins(payout.BitcoinAddress, (long)payout.Satoshis - (long)payout.TxFee);
            walletManager.MarkPayoutAsCompleted(payout.PayoutId, tx);
		}

	}

}

