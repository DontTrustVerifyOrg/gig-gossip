using System;
using System.Threading.Channels;
using Grpc.Core;
using LNDClient;
using Lnrpc;
using Walletrpc;
using TraceExColor;
using GigGossip;

namespace LNDWallet;

public class LNDChannelManager
{
    public GigDebugLoggerAPIClient.LogWrapper<LNDChannelManager> TRACE = GigDebugLoggerAPIClient.ConsoleLoggerFactory.Trace<LNDChannelManager>();

    LNDWalletManager walletManager;
	List<string> nearbyNodes;
	long maxSatoshisPerChannel;
	long estimatedTxFee;
	long maxChannelCloseFeePerVByte;


    public LNDChannelManager(LNDWalletManager walletManager, List<string> nearbyNodes, long maxSatoshisPerChannel, long estimatedTxFee, long maxChannelCloseFeePerVByte)
	{
		this.walletManager = walletManager;
		this.nearbyNodes = nearbyNodes;
		this.maxSatoshisPerChannel = maxSatoshisPerChannel;
		this.maxChannelCloseFeePerVByte = maxChannelCloseFeePerVByte;
		this.estimatedTxFee = estimatedTxFee;

	}

    Thread mainThread;
    private long _mainThreadStop;

    public void Start()
	{
        TraceEx.TraceInformation("Main Monitoring Thread Starting");

		_mainThreadStop = 0;
        mainThread = new Thread(async () =>
		{

			while (Interlocked.Read(ref _mainThreadStop) == 0)
			{
                bool again = false;
                try
                {
                    walletManager.GoForCancellingInternalInvoices();
                    GoForConnectingToNodes();
					foreach (var friend in nearbyNodes)
					{
                        again = again | await GoForOpeningNewChannelsForNodeAsync(friend.Split("@")[0], maxSatoshisPerChannel, estimatedTxFee);
					}
                    await GoForExecutingPayoutsAsync(estimatedTxFee, maxChannelCloseFeePerVByte);
				}
				catch(Exception ex)
				{
					TraceEx.TraceException(ex);
				}
				if(!again)
					Thread.Sleep(10000);
			}
            TraceEx.TraceInformation("Main Monitoring Thread Joining");
        });
		mainThread.Start();
	}


	public void Stop()
	{
        TraceEx.TraceInformation("Main Monitoring Thread Stopping ...");
        Interlocked.Add(ref _mainThreadStop, 1);
        mainThread.Join();
        TraceEx.TraceInformation("Main Monitoring Thread ... Stopped");
    }



	public void GoForConnectingToNodes()
	{
		var peersof2 = new HashSet<string>(from p in walletManager.ListPeers().Peers select p.PubKey);
		using var TL = TRACE.Log().Args(peersof2);
		try
		{

			foreach (var friend in nearbyNodes)
			{
				try
				{
					var fr = friend.Replace("127.0.0.1", "localhost");
					if (!peersof2.Contains(friend.Split("@")[0]))
					{
						TL.Info($"Connecting to [[yellow]]{fr}[[/]] ...");
						walletManager.Connect(fr);
                        TL.Info($"... connected to [[yellow]]{fr}[[/]]");
					}
					else
                        TL.Info($"Already connected to [[yellow]]{fr}[[/]]");
				}
				catch (Exception ex)
				{
                    TL.Exception(ex);
				}
			}
		}
		catch (Exception ex)
		{
			TL.Exception(ex);
			throw;
		}
	}

	public async Task<bool> GoForOpeningNewChannelsForNodeAsync(string nodePubKey, long maxSatoshisPerChannel, long estimatedTxFee, long dustlevel = 354)
	{
		using var TL = TRACE.Log().Args(nodePubKey, maxSatoshisPerChannel, estimatedTxFee, dustlevel);
		try
		{
			var walletBallance = walletManager.GetWalletBalance();
			var confirmedWalletBalance = walletBallance.ConfirmedBalance;
            TL.Info($"confirmedWalletBalance={confirmedWalletBalance}");
			var requiredReserve = walletManager.GetRequiredReserve(2);
            TL.Info($"requiredReserve={requiredReserve}");
			var requestedReserve = walletManager.GetRequestedReserveAmount();
            TL.Info($"requestedReserve={requestedReserve}");

			if (confirmedWalletBalance >= requiredReserve + requestedReserve + estimatedTxFee)
				try
				{
					var amount = confirmedWalletBalance - estimatedTxFee - requiredReserve - requestedReserve;
					if (amount > maxSatoshisPerChannel)
						amount = maxSatoshisPerChannel;

					if (amount >= dustlevel)
					{
                        TL.Info($"amount={amount}");

                        TL.Info($"Opening Channel to [{nodePubKey}] for {amount}");
						var ocs = walletManager.OpenChannel(nodePubKey, amount);
						while (await ocs.ResponseStream.MoveNext())
						{
                            TL.Info($"Channel state {ocs.ResponseStream.Current.UpdateCase.ToString()} to [{nodePubKey}] id {ocs.ResponseStream.Current.PendingChanId}");
							if (ocs.ResponseStream.Current.UpdateCase == OpenStatusUpdate.UpdateOneofCase.ChanOpen)
								return true;
						}
					}
					else
                        TL.Warning($"amount={amount} is on [[yellow]]dust level[[/]]l");
				}
				catch (Exception ex)
				{
                    TL.Exception(ex);
				}
			else
                TL.Info("Not enough [[yellow]]funds/reserve[[/]] to open channel");

			return false;
		}
		catch (Exception ex)
		{
			TL.Exception(ex);
			throw;
		}
	}

	public async Task GoForExecutingPayoutsAsync(long estimatedTxFee, long maxChannelCloseFeePerVByte)
	{
		using var TL = TRACE.Log().Args(estimatedTxFee, maxChannelCloseFeePerVByte);
		try
		{
            walletManager.CompleteSendingPayouts();

			var walletBallance = walletManager.GetWalletBalance();
			var confirmedWalletBalance = walletBallance.ConfirmedBalance;
			TL.Info($"confirmedWalletBalance={confirmedWalletBalance}");

			var requestedReserves = walletManager.GetRequestedReserves();
			var pendingPayouts = walletManager.GetPendingPayouts();

			var requestedReserve = (from r in requestedReserves select r.Satoshis).Sum();
			TL.Info($"requestedReserve={requestedReserve}");

			if (confirmedWalletBalance < requestedReserve + estimatedTxFee)
			{
				var amoutToFree = requestedReserve + estimatedTxFee - confirmedWalletBalance;

				var sortedChannelsByLocalBalance = (walletManager.ListChannels(true).Channels).ToArray().OrderBy((c) => c.LocalBalance).ToArray();
				long freedAmount = 0;
				Dictionary<string, AsyncServerStreamingCall<CloseStatusUpdate>> updateStreams = new();

				foreach (var channel in sortedChannelsByLocalBalance)
				{
					TL.Info($"Closing Channel [{channel.ChannelPoint}] ...");
					updateStreams.Add(channel.ChannelPoint, walletManager.CloseChannel(channel.ChannelPoint, (ulong)maxChannelCloseFeePerVByte));
					freedAmount += channel.LocalBalance - estimatedTxFee;
					if (freedAmount >= amoutToFree)
						break;
				}

				foreach (var us in updateStreams)
				{
					while (await us.Value.ResponseStream.MoveNext())
					{
						TL.Info($"... Channel {us.Value.ResponseStream.Current.UpdateCase} [{us.Key}");
						if (us.Value.ResponseStream.Current.UpdateCase == CloseStatusUpdate.UpdateOneofCase.ChanClose)
							break;
					}
				}
			}

            foreach (var payout in pendingPayouts)
			{
				string tx = "";
				try
				{
					if (walletManager.MarkPayoutAsSending(payout.PayoutId))
					{
						var amount = payout.Satoshis - payout.PayoutFee;
						TL.Info($"Sending Coins to [{payout.BitcoinAddress}] amount={amount}, txfee={payout.PayoutFee}");

						var (feeSat, satsPerVByte) = walletManager.EstimateFee(payout.BitcoinAddress, amount);
						if (feeSat > payout.PayoutFee)
						{
							TL.Error($"Payout fee less than transaction fee {payout.PayoutId}");
							walletManager.MarkPayoutAsFailure(payout.PayoutId, tx);
						}
						else
						{
							tx = walletManager.SendCoins(payout.BitcoinAddress, payout.Satoshis - payout.PayoutFee, payout.PayoutId.ToString());
							walletManager.MarkPayoutAsSent(payout.PayoutId, tx);
							TL.Info($"Payout done");
						}
					}

				}
				catch (Exception ex)
				{
					walletManager.MarkPayoutAsFailure(payout.PayoutId, tx);
					TraceEx.TraceException(ex);
				}
			}
		}
		catch (Exception ex)
		{
			TL.Exception(ex);
			throw;
		}
	}

}

