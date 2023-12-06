using System;
using CryptoToolkit;
using GigLNDWalletAPIClient;
using NGigGossip4Nostr;
using Spectre.Console;

namespace RideShareCLIApp;

public partial class RideShareCLIApp
{
    List<AcceptBroadcastEventArgs> receivedBroadcasts = new();
    List<long> receivedBroadcastsFees = new();
    DataTable receivedBroadcastsTable = null;
    Dictionary<Guid, int> receivedBroadcastIdxesForPayloadIds = new();
    Dictionary<Guid, string> directSecrets = new();

    const string DATE_FORMAT = "dd MMM HH:mm";

    Dictionary<Guid, long> feesPerBroadcastId = new();

    Guid ActiveSignedRequestPayloadId = Guid.Empty;
    bool riderInTheCar;

    private async void GigGossipNodeEventSource_OnAcceptBroadcast(object? sender, AcceptBroadcastEventArgs e)
    {
        var taxiTopic = Crypto.DeserializeObject<RideTopic>(e.BroadcastFrame.SignedRequestPayload.Value.Topic);
        if (taxiTopic == null)
            return;

        if (inDriverMode)
        {
            if (!feesPerBroadcastId.ContainsKey(e.BroadcastFrame.SignedRequestPayload.Id))
                feesPerBroadcastId[e.BroadcastFrame.SignedRequestPayload.Id] = Random.Shared.NextInt64(1000, 2000);

            long fee = feesPerBroadcastId[e.BroadcastFrame.SignedRequestPayload.Id];

            var from = taxiTopic.FromGeohash;
            var tim = "(" + taxiTopic.PickupAfter.ToString(DATE_FORMAT) + "+" + ((int)(taxiTopic.PickupBefore - taxiTopic.PickupAfter).TotalMinutes).ToString() + ")";
            var to = taxiTopic.ToGeohash;

            receivedBroadcastsTable.AddRow(new string[] { "", from, tim, to, fee.ToString(), e.BroadcastFrame.SignedRequestPayload.Id.ToString() });
            receivedBroadcasts.Add(e);
            receivedBroadcastsFees.Add(fee);
            receivedBroadcastIdxesForPayloadIds[e.BroadcastFrame.SignedRequestPayload.Id] = receivedBroadcasts.Count - 1;
            return;
        }
        else
        {
            if (taxiTopic.FromGeohash.Length <= settings.NodeSettings.GeohashPrecision &&
                   taxiTopic.ToGeohash.Length <= settings.NodeSettings.GeohashPrecision)
                await e.GigGossipNode.BroadcastToPeersAsync(e.PeerPublicKey, e.BroadcastFrame);
        }
    }

    private async Task AcceptRideAsync(int idx)
    {
        var e = receivedBroadcasts[idx];
        var fee = receivedBroadcastsFees[idx];

        var secret = Crypto.GenerateRandomPreimage().AsHex();
        directSecrets[e.BroadcastFrame.SignedRequestPayload.Id] = secret;

        var reply = new ConnectionReply()
        {
            PublicKey = e.GigGossipNode.PublicKey,
            Relays = e.GigGossipNode.NostrRelays,
            Secret = secret,
        };

        await e.GigGossipNode.AcceptBroadcastAsync(e.PeerPublicKey, e.BroadcastFrame,
                        new AcceptBroadcastResponse()
                        {
                            Properties = settings.NodeSettings.GetDriverProperties(),
                            Message = Crypto.SerializeObject(reply),
                            Fee = fee,
                            SettlerServiceUri = settings.NodeSettings.SettlerOpenApi,
                        });

        receivedBroadcastIdxesForPayloadIds[e.BroadcastFrame.SignedRequestPayload.Id] = idx;

    }


    private async void GigGossipNodeEventSource_OnInvoiceAccepted(object? sender, InvoiceAcceptedEventArgs e)
    {
        if (inDriverMode)
        {
            if (!e.InvoiceData.IsNetworkInvoice)
            {
                var broadcasts = e.GigGossipNode.GetAcceptedBroadcasts().ToList();
                var hashes = (from br in broadcasts
                              select Crypto.DeserializeObject<PayReq>(br.DecodedReplyInvoice).PaymentHash).ToList();

                var hash2br= hashes.Zip(broadcasts, (k, v) => new { k, v }).ToDictionary(x => x.k, x => x.v);

                if (hash2br.ContainsKey(e.InvoiceData.PaymentHash))
                {
                    ActiveSignedRequestPayloadId = hash2br[e.InvoiceData.PaymentHash].SignedRequestPayloadId;

                    foreach (var bbr in (from hash in hashes where hash != e.InvoiceData.PaymentHash select hash))
                        WalletAPIResult.Check(await e.GigGossipNode.LNDWalletClient.CancelInvoiceAsync(e.GigGossipNode.MakeWalletAuthToken(), bbr));

                    await directCom.StartAsync(e.GigGossipNode.NostrRelays);
                }
            }
        }
    }



    private void GigGossipNodeEventSource_OnCancelBroadcast(object? sender, CancelBroadcastEventArgs e)
    {
        if (!receivedBroadcastIdxesForPayloadIds.ContainsKey(e.CancelBroadcastFrame.SignedCancelRequestPayload.Id))
            return;
        var idx = receivedBroadcastIdxesForPayloadIds[e.CancelBroadcastFrame.SignedCancelRequestPayload.Id];
        receivedBroadcasts.RemoveAt(idx);
        receivedBroadcastsFees.RemoveAt(idx);
        receivedBroadcastIdxesForPayloadIds.Remove(e.CancelBroadcastFrame.SignedCancelRequestPayload.Id);
        receivedBroadcastsTable.InactivateRow(idx);
    }


    private async Task DriverJourneyAsync(Guid requestPayloadId)
    {
        riderInTheCar = false;
        var pubkey = directPubkeys[requestPayloadId];
        for (int i =5;i>0;i--)
        {
            AnsiConsole.MarkupLine($"({i}) I am [orange1]driving[/] to meet rider");
            await directCom.SendMessageAsync(pubkey, new LocationFrame
            {
                SignedRequestPayloadId = requestPayloadId,
                Location = new Location(),
                Message = "I am going",
                RideState = RideState.DriverApproaching,
            },  false, DateTime.UtcNow + this.gigGossipNode.InvoicePaymentTimeout);
            Thread.Sleep(1000);
        }
        AnsiConsole.MarkupLine("I have [orange1]arrived[/]");
        while (!riderInTheCar)
        {
            AnsiConsole.MarkupLine("I am [orange1]waiting[/] for rider");
            await directCom.SendMessageAsync(pubkey, new LocationFrame
            {
                SignedRequestPayloadId = requestPayloadId,
                Location = new Location(),
                Message = "I am waiting",
                RideState = RideState.DriverWaitingForRider,
            }, false, DateTime.UtcNow + this.gigGossipNode.InvoicePaymentTimeout);
            Thread.Sleep(1000);
        }
        AnsiConsole.MarkupLine("Rider [orange1]in the car[/]");
        for(int i = 5; i > 0; i--)
        {
            AnsiConsole.MarkupLine($"({i}) We are going [orange1]togheter[/]");
            await directCom.SendMessageAsync(pubkey, new LocationFrame
            {
                SignedRequestPayloadId = requestPayloadId,
                Location = new Location(),
                Message = "We are driving",
                RideState = RideState.RidingTogheter,
            }, false, DateTime.UtcNow + this.gigGossipNode.InvoicePaymentTimeout);
            Thread.Sleep(1000);
        }
        AnsiConsole.MarkupLine("We have [orange1]reached[/] the destination");
        await directCom.SendMessageAsync(pubkey, new LocationFrame
        {
            SignedRequestPayloadId = requestPayloadId,
            Location = new Location(),
            Message = "Thank you",
            RideState = RideState.RiderDroppedOff,
        }, false, DateTime.UtcNow + this.gigGossipNode.InvoicePaymentTimeout);
        AnsiConsole.MarkupLine("Good [orange1]bye[/]");
        ActiveSignedRequestPayloadId = Guid.Empty;
        inDriverMode = false;
    }

    private async Task OnAckFrame(string senderPublicKey, AckFrame ackframe)
    {
        if (ActiveSignedRequestPayloadId == Guid.Empty)
            return;
        if (ackframe.SignedRequestPayloadId == ActiveSignedRequestPayloadId)
        {
            if (directSecrets.ContainsKey(ackframe.SignedRequestPayloadId))
            {
                if (directSecrets[ackframe.SignedRequestPayloadId] == ackframe.Secret)
                {
                    directPubkeys[ackframe.SignedRequestPayloadId] = senderPublicKey;
                    AnsiConsole.WriteLine("rider ack:" + senderPublicKey);
                    receivedBroadcastsTable.Exit();
                    new Thread(() => DriverJourneyAsync(ackframe.SignedRequestPayloadId)).Start();
                }
            }
        }
    }

    private async Task OnRiderLocation(string senderPublicKey, LocationFrame locationFrame)
    {
        if (ActiveSignedRequestPayloadId == Guid.Empty)
            return;
        if (locationFrame.SignedRequestPayloadId == ActiveSignedRequestPayloadId)
        {
            var pubkey = directPubkeys[locationFrame.SignedRequestPayloadId];
            AnsiConsole.WriteLine("rider location:" + senderPublicKey + "|" + locationFrame.RideState.ToString() + "|" + locationFrame.Message + "|" + locationFrame.Location.ToString());
            if (locationFrame.RideState == RideState.RidingTogheter)
            {
                riderInTheCar = true;
            }
        }
    }
}

