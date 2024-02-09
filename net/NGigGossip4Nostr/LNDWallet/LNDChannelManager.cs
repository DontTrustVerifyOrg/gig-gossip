using System;
using System.Diagnostics;
using System.Threading.Channels;
using Grpc.Core;
using LNDClient;
using Lnrpc;
using Walletrpc;

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
        mainThread = new Thread(async () =>
		{

			while (Interlocked.Read(ref _mainThreadStop) == 0)
			{
                bool again = false;
                try
                {
					GoForConnectingToNodes();
                    foreach (var friend in nearbyNodes)
						again = again | await GoForOpeningNewChannelsForNodeAsync(friend.Split("@")[0], maxSatoshisPerChannel, estimatedTxFee);
					await GoForExecutingPayoutsAsync(estimatedTxFee);
				}
				catch(Exception ex)
				{
					Trace.TraceError(ex.ToString());
				}
				if(!again)
					Thread.Sleep(60000);
			}
		});
		mainThread.Start();
	}


	public void Stop()
	{
        Interlocked.Add(ref _mainThreadStop, 1);
        mainThread.Join();
	}

	public void GoForConnectingToNodes()
	{
        var peersof2 = new HashSet<string>(from p in walletManager.ListPeers().Peers select p.PubKey);

        foreach (var friend in nearbyNodes)
        {
            try
            {
                var fr = friend.Replace("127.0.0.1", "localhost");
                if (!peersof2.Contains(friend.Split("@")[0]))
                {
                    Trace.TraceInformation($"Connecting to [[yellow]]{fr}[[/]] ...");
                    walletManager.Connect(fr);
                    Trace.TraceInformation($"... connected to [[yellow]]{fr}[[/]]");
                }
                else
                    Trace.TraceInformation($"Already connected to [[yellow]]{fr}[[/]]");
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
            }
        }
    }

	public async Task<bool> GoForOpeningNewChannelsForNodeAsync(string nodePubKey, long maxSatoshisPerChannel, long estimatedTxFee, long dustlevel = 354)
	{
		var confirmedWalletBalance = walletManager.GetConfirmedWalletBalance();
        Trace.TraceInformation($"confirmedWalletBalance={confirmedWalletBalance}");
        var requiredReserve = walletManager.GetRequiredReserve(2);
        Trace.TraceInformation($"requiredReserve={requiredReserve}");

		var requestedReserve = walletManager.GetRequestedReserveAmount();
        Trace.TraceInformation($"requestedReserve={requestedReserve}");

        if (confirmedWalletBalance >= requiredReserve + requestedReserve + estimatedTxFee)
			try
			{
				var amount = confirmedWalletBalance - estimatedTxFee - requiredReserve - requestedReserve;
				if (amount > maxSatoshisPerChannel)
					amount = maxSatoshisPerChannel;

				if (amount >= dustlevel)
				{
					Trace.TraceInformation($"amount={amount}");

					Trace.TraceInformation($"Opening Channel to [{nodePubKey}] for {amount}");
					var ocs = walletManager.OpenChannel(nodePubKey, amount);
					while (await ocs.ResponseStream.MoveNext())
					{
						Trace.TraceInformation($"Channel state {ocs.ResponseStream.Current.UpdateCase.ToString()} to [{nodePubKey}] id {ocs.ResponseStream.Current.PendingChanId}");
						if (ocs.ResponseStream.Current.UpdateCase == OpenStatusUpdate.UpdateOneofCase.ChanOpen)
							return true;
					}
				}
				else
                    Trace.TraceWarning($"amount={amount} is on [[yellow]]dust level[[/]]l");
            }
            catch (Exception ex)
			{
				Trace.TraceError(ex.Message);
			}
		else
			Trace.TraceInformation("Not enough [[yellow]]funds/reserve[[/]] to open channel");

        return false;
	}

	public async Task GoForExecutingPayoutsAsync(long estimatedTxFee)
	{
		var confirmedWalletBalance = walletManager.GetConfirmedWalletBalance();
		Trace.TraceInformation($"confirmedWalletBalance={confirmedWalletBalance}");
		var requiredReserve = walletManager.GetRequiredReserve(2);
		Trace.TraceInformation($"requiredReserve={requiredReserve}");

		var requestedReserves = walletManager.GetRequestedReserves();
		var reserveIds = (from r in requestedReserves select r.ReserveId).ToList();
		var pendingPayouts = walletManager.GetPendingPayouts(reserveIds);

		var requestedReserve = (from r in requestedReserves select r.Satoshis).Sum();
		Trace.TraceInformation($"requestedReserve={requestedReserve}");

		if (confirmedWalletBalance < requiredReserve + requestedReserve + estimatedTxFee)
		{
			var amoutToFree = requiredReserve + requestedReserve + estimatedTxFee - confirmedWalletBalance;

			var sortedChannelsByLocalBalance = (walletManager.ListChannels(true).Channels).ToArray().OrderBy((c) => c.LocalBalance).ToArray();
			long freedAmount = 0;
			Dictionary<string, AsyncServerStreamingCall<CloseStatusUpdate>> updateStreams = new();

			foreach (var channel in sortedChannelsByLocalBalance)
			{
				Trace.TraceInformation($"Closing Channel [{channel.ChannelPoint}] ...");
				updateStreams.Add(channel.ChannelPoint, walletManager.CloseChannel(channel.ChannelPoint));
				freedAmount += channel.LocalBalance - estimatedTxFee;
				if (freedAmount >= amoutToFree)
					break;
			}

			foreach (var us in updateStreams)
			{
				while (await us.Value.ResponseStream.MoveNext())
				{
					Trace.TraceInformation($"... Channel {us.Value.ResponseStream.Current.UpdateCase} [{us.Key}");
					if (us.Value.ResponseStream.Current.UpdateCase == CloseStatusUpdate.UpdateOneofCase.ChanClose)
						break;
				}
			}
		}

		foreach (var payout in pendingPayouts)
		{
			Trace.TraceInformation($"Sending Coins to [{payout.BitcoinAddress}] amount={(long)payout.Satoshis - (long)payout.TxFee}, txfee={(long)payout.TxFee}");
            walletManager.MarkPayoutAsSending(payout.PayoutId);
            var tx = walletManager.SendCoins(payout.BitcoinAddress, (long)payout.Satoshis - (long)payout.TxFee);
			walletManager.MarkPayoutAsSent(payout.PayoutId, tx);
		}

	}

}

