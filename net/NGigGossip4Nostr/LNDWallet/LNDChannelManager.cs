using System;
using System.Threading.Channels;
using Grpc.Core;
using LNDClient;
using Lnrpc;
using Walletrpc;
using TraceExColor;

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
					TraceEx.TraceException(ex);
				}
				if(!again)
					Thread.Sleep(10000);
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
                    TraceEx.TraceInformation($"Connecting to [[yellow]]{fr}[[/]] ...");
                    walletManager.Connect(fr);
                    TraceEx.TraceInformation($"... connected to [[yellow]]{fr}[[/]]");
                }
                else
                    TraceEx.TraceInformation($"Already connected to [[yellow]]{fr}[[/]]");
            }
            catch (Exception ex)
            {
                TraceEx.TraceException(ex);
            }
        }
    }

	public async Task<bool> GoForOpeningNewChannelsForNodeAsync(string nodePubKey, long maxSatoshisPerChannel, long estimatedTxFee, long dustlevel = 354)
	{
		var walletBallance = walletManager.GetWalletBalance();
        var confirmedWalletBalance = walletBallance.ConfirmedBalance;
        TraceEx.TraceInformation($"confirmedWalletBalance={confirmedWalletBalance}");
        var requiredReserve = walletManager.GetRequiredReserve(2);
        TraceEx.TraceInformation($"requiredReserve={requiredReserve}");

		var requestedReserve = walletManager.GetRequestedReserveAmount();
        TraceEx.TraceInformation($"requestedReserve={requestedReserve}");


        if (confirmedWalletBalance >= requiredReserve + requestedReserve + estimatedTxFee)
			try
			{
				var amount = confirmedWalletBalance - estimatedTxFee - requiredReserve - requestedReserve;
				if (amount > maxSatoshisPerChannel)
					amount = maxSatoshisPerChannel;

				if (amount >= dustlevel)
				{
					TraceEx.TraceInformation($"amount={amount}");

					TraceEx.TraceInformation($"Opening Channel to [{nodePubKey}] for {amount}");
					var ocs = walletManager.OpenChannel(nodePubKey, amount);
					while (await ocs.ResponseStream.MoveNext())
					{
						TraceEx.TraceInformation($"Channel state {ocs.ResponseStream.Current.UpdateCase.ToString()} to [{nodePubKey}] id {ocs.ResponseStream.Current.PendingChanId}");
						if (ocs.ResponseStream.Current.UpdateCase == OpenStatusUpdate.UpdateOneofCase.ChanOpen)
							return true;
					}
				}
				else
                    TraceEx.TraceWarning($"amount={amount} is on [[yellow]]dust level[[/]]l");
            }
            catch (Exception ex)
			{
				TraceEx.TraceException(ex);
			}
		else
			TraceEx.TraceInformation("Not enough [[yellow]]funds/reserve[[/]] to open channel");

        return false;
	}

	public async Task GoForExecutingPayoutsAsync(long estimatedTxFee)
	{
        var walletBallance = walletManager.GetWalletBalance();
        var confirmedWalletBalance = walletBallance.ConfirmedBalance;
        TraceEx.TraceInformation($"confirmedWalletBalance={confirmedWalletBalance}");

		var requestedReserves = walletManager.GetRequestedReserves();
		var reserveIds = (from r in requestedReserves select r.ReserveId).ToList();
		var pendingPayouts = walletManager.GetPendingPayouts(reserveIds);

		var requestedReserve = (from r in requestedReserves select r.Satoshis).Sum();
		TraceEx.TraceInformation($"requestedReserve={requestedReserve}");

		if (confirmedWalletBalance  < requestedReserve + estimatedTxFee)
		{
			var amoutToFree = requestedReserve + estimatedTxFee - confirmedWalletBalance;

			var sortedChannelsByLocalBalance = (walletManager.ListChannels(true).Channels).ToArray().OrderBy((c) => c.LocalBalance).ToArray();
			long freedAmount = 0;
			Dictionary<string, AsyncServerStreamingCall<CloseStatusUpdate>> updateStreams = new();

			foreach (var channel in sortedChannelsByLocalBalance)
			{
				TraceEx.TraceInformation($"Closing Channel [{channel.ChannelPoint}] ...");
				updateStreams.Add(channel.ChannelPoint, walletManager.CloseChannel(channel.ChannelPoint));
				freedAmount += channel.LocalBalance - estimatedTxFee;
				if (freedAmount >= amoutToFree)
					break;
			}

			foreach (var us in updateStreams)
			{
				while (await us.Value.ResponseStream.MoveNext())
				{
					TraceEx.TraceInformation($"... Channel {us.Value.ResponseStream.Current.UpdateCase} [{us.Key}");
					if (us.Value.ResponseStream.Current.UpdateCase == CloseStatusUpdate.UpdateOneofCase.ChanClose)
						break;
				}
			}
		}

		foreach (var payout in pendingPayouts)
		{
			try
			{
				if (walletManager.MarkPayoutAsSending(payout.PayoutId))
				{
					TraceEx.TraceInformation($"Sending Coins to [{payout.BitcoinAddress}] amount={(long)payout.Satoshis - (long)payout.TxFee}, txfee={(long)payout.TxFee}");
					var tx = walletManager.SendCoins(payout.BitcoinAddress, (long)payout.Satoshis - (long)payout.TxFee, 0, payout.PayoutId.ToString());
					walletManager.MarkPayoutAsSent(payout.PayoutId, tx);
				}
			}
			catch(Exception ex)
			{
				walletManager.MarkPayoutAsOpen(payout.PayoutId);
				throw;
			}
		}

	}

}

