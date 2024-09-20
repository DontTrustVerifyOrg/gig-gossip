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
    long minSatoshisPerChannel;
    long maxSatoshisPerChannel;
	long maxChannelCloseFeePerVByte;


    public LNDChannelManager(LNDWalletManager walletManager, List<string> nearbyNodes, long minSatoshisPerChannel, long maxSatoshisPerChannel, long maxChannelCloseFeePerVByte)
	{
		this.walletManager = walletManager;
		this.nearbyNodes = nearbyNodes;
		this.maxSatoshisPerChannel = maxSatoshisPerChannel;
		this.minSatoshisPerChannel = minSatoshisPerChannel;
		this.maxChannelCloseFeePerVByte = maxChannelCloseFeePerVByte;
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
                        again = again | await GoForOpeningNewChannelsForNodeAsync(friend.Split("@")[0], minSatoshisPerChannel, maxSatoshisPerChannel);
					}
                    await GoForExecutingPayoutsAsync(maxChannelCloseFeePerVByte);
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

	public async Task<bool> GoForOpeningNewChannelsForNodeAsync(string nodePubKey, long minSatoshisPerChannel, long maxSatoshisPerChannel)
	{
		using var TL = TRACE.Log().Args(nodePubKey, minSatoshisPerChannel, maxSatoshisPerChannel);

		try
		{
			var openChans = walletManager.ListChannels(true).Channels;
			foreach (var chan in openChans)
				TL.Info($"openChannel ChanId = {chan.ChanId}, ChannelPoint = {chan.ChannelPoint}, Capacity= {chan.Capacity}, ChanStatusFlags= {chan.ChanStatusFlags}");

			var walletBalance = walletManager.GetWalletBalance();
			var confirmedWalletBalance = walletBalance.ConfirmedBalance;
			var requiredReserve = walletManager.GetRequiredReserve(0);
			var requiredAdditionalReserveForChannelOpening = walletManager.GetRequiredReserve(1) - requiredReserve;
			var requestedReserve = walletManager.GetRequestedReserveAmount();

			var availableAmount = confirmedWalletBalance - requestedReserve - requiredReserve - requiredAdditionalReserveForChannelOpening;


			TL.Info($"confirmedWalletBalance={confirmedWalletBalance}");
			TL.Info($"requiredReserve={requiredReserve}");
			TL.Info($"requiredAdditionalReserveForChannelOpening={requiredAdditionalReserveForChannelOpening}");
			TL.Info($"requestedReserve={requestedReserve}");
			TL.Info($"minSatoshisPerChannel={minSatoshisPerChannel}");
			TL.Info($"maxSatoshisPerChannel={maxSatoshisPerChannel}");
			TL.Info($"availableAmount={availableAmount}");

			if (availableAmount < 0)
			{
                TL.Warning($"availableAmount less than zero");
                return false;
			}

			if (availableAmount < Math.Max(minSatoshisPerChannel, requiredAdditionalReserveForChannelOpening))
			{
                TL.Info($"availableAmount less max(minSatoshisPerChannel,requiredAdditionalReserveForChannelOpening)");
                return false;
			}

			var amount = availableAmount;

			if (amount > maxSatoshisPerChannel)
			{
                TL.Info($"availableAmount more than maxSatoshisPerChannel");
                amount = maxSatoshisPerChannel;
			}

			try
			{
				TL.Info($"Opening Channel to [{nodePubKey}] for {amount}");
				var ocs = walletManager.OpenChannel(nodePubKey, amount);
				while (await ocs.ResponseStream.MoveNext())
				{
					TL.Info($"Channel state {ocs.ResponseStream.Current.UpdateCase.ToString()} to [{nodePubKey}] id {ocs.ResponseStream.Current.PendingChanId}");
					if (ocs.ResponseStream.Current.UpdateCase == OpenStatusUpdate.UpdateOneofCase.ChanOpen)
						return true;
				}
			}
			catch (Exception ex)
			{
				TL.Exception(ex);
			}
		}
		catch (Exception ex)
		{
			TL.Exception(ex);
			throw;
		}
	}

	public async Task GoForExecutingPayoutsAsync(long maxChannelCloseFeePerVByte)
	{
		using var TL = TRACE.Log().Args(maxChannelCloseFeePerVByte);
		try
		{
            walletManager.CompleteSendingPayouts();

			var walletBalance = walletManager.GetWalletBalance();
			var confirmedWalletBalance = walletBalance.ConfirmedBalance;
			TL.Info($"confirmedWalletBalance={confirmedWalletBalance}");

			var requestedReserves = walletManager.GetRequestedReserves();
			var pendingPayouts = walletManager.GetPendingPayouts();

			var requestedReserve = (from r in requestedReserves select r.Satoshis).Sum();
			TL.Info($"requestedReserve={requestedReserve}");

			if (confirmedWalletBalance < requestedReserve)
			{
				var amoutToFree = requestedReserve - confirmedWalletBalance;

				var sortedChannelsByLocalBalance = (walletManager.ListChannels(true).Channels).ToArray().OrderBy((c) => c.LocalBalance).ToArray();
				long freedAmount = 0;
				Dictionary<string, AsyncServerStreamingCall<CloseStatusUpdate>> updateStreams = new();

				foreach (var channel in sortedChannelsByLocalBalance)
				{
					TL.Info($"Closing Channel [{channel.ChannelPoint}] ...");
					updateStreams.Add(channel.ChannelPoint, walletManager.CloseChannel(channel.ChannelPoint, (ulong)maxChannelCloseFeePerVByte));
					freedAmount += channel.LocalBalance;
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

			var estimatedChannelCloseFee = walletManager.EstimateChannelClosingFee();

            foreach (var payout in pendingPayouts)
			{
				string tx = "";
				try
				{
                    var (feeSat, satsPerVByte) = walletManager.EstimateFee(payout.BitcoinAddress, payout.Satoshis);
                    if (walletManager.MarkPayoutAsSending(payout.PayoutId, estimatedChannelCloseFee+ feeSat))
					{
						var amount = payout.Satoshis - estimatedChannelCloseFee- feeSat;
						TL.Info($"Sending Coins to [{payout.BitcoinAddress}] amount={amount} = payout:{payout.Satoshis}-txfee:{feeSat}-chanCloseFee:{estimatedChannelCloseFee}");
						tx = walletManager.SendCoins(payout.BitcoinAddress, amount, payout.PayoutId.ToString());
						walletManager.MarkPayoutAsSent(payout.PayoutId, tx);
						TL.Info($"Payout done");
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

