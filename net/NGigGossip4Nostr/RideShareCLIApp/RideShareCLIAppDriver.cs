using System;
using System.Threading;
using CryptoToolkit;
using GigGossipSettlerAPIClient;
using GigLNDWalletAPIClient;
using NBitcoin.Secp256k1;
using NGeoHash;
using NGigGossip4Nostr;
using RideShareFrames;
using Spectre.Console;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace RideShareCLIApp;

public partial class RideShareCLIApp
{
    Dictionary<Guid, List<AcceptBroadcastEventArgs>> receivedBroadcastsForPayloadId = new();
    Dictionary<Guid, AcceptBroadcastReturnValue> payReqsForPayloadId = new();
    Dictionary<Guid, int> receivedBroadcastIdxesForPayloadIds = new();
    Dictionary<Guid, long> receivedBroadcastsFees = new();
    Dictionary<Guid, string> directSecrets = new();
    DataTable receivedBroadcastsTable = null;

    const string DATE_FORMAT = "dd MMM HH:mm";

    Dictionary<Guid, long> feesPerBroadcastId = new();

    Guid ActiveSignedRequestPayloadId = Guid.Empty;

    private async void GigGossipNodeEventSource_OnAcceptBroadcast(object? sender, AcceptBroadcastEventArgs e)
    {
        var taxiTopic = Crypto.DeserializeObject<RideTopic>(e.BroadcastFrame.SignedRequestPayload.Value.Topic);
        if (taxiTopic == null)
            return;

        if (inDriverMode)
        {
            Guid id = e.BroadcastFrame.SignedRequestPayload.Id;

            if (!feesPerBroadcastId.ContainsKey(id))
            {
                long fee = Random.Shared.NextInt64(1000, 2000);
                feesPerBroadcastId[id] = fee;

                var from = taxiTopic.FromGeohash;
                var tim = "(" + taxiTopic.PickupAfter.ToString(DATE_FORMAT) + "+" + ((int)(taxiTopic.PickupBefore - taxiTopic.PickupAfter).TotalMinutes).ToString() + ")";
                var to = taxiTopic.ToGeohash;
                receivedBroadcastsForPayloadId[id] = new List<AcceptBroadcastEventArgs> { e };
                receivedBroadcastsFees[id] = fee;
                receivedBroadcastIdxesForPayloadIds[id] = receivedBroadcastsForPayloadId.Count - 1;
                receivedBroadcastsTable.AddRow(new string[] { "", id.ToString(), "1", from, tim, to, fee.ToString() });
            }
            else
            {
                receivedBroadcastsForPayloadId[id].Add(e);
                receivedBroadcastsTable.UpdateCell(receivedBroadcastIdxesForPayloadIds[id], 2, receivedBroadcastsForPayloadId[id].Count.ToString());
            }
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
        var id = Guid.Parse(receivedBroadcastsTable.GetCell(idx, 1));

        var evs = receivedBroadcastsForPayloadId[id];
        var fee = receivedBroadcastsFees[id];

        var secret = Crypto.GenerateRandomPreimage().AsHex();
        directSecrets[id] = secret;

        foreach (var e in evs)
        {
            var taxiTopic = Crypto.DeserializeObject<RideTopic>(
                e.BroadcastFrame.SignedRequestPayload.Value.Topic);

            var reply = new ConnectionReply()
            {
                PublicKey = e.GigGossipNode.PublicKey,
                Relays = e.GigGossipNode.NostrRelays,
                Secret = secret
            };

            var payReq = await e.GigGossipNode.AcceptBroadcastAsync(e.PeerPublicKey, e.BroadcastFrame,
                        new AcceptBroadcastResponse()
                        {
                            Properties = settings.NodeSettings.GetDriverProperties(),
                            Message = Crypto.SerializeObject(reply),
                            Fee = fee,
                            SettlerServiceUri = settings.NodeSettings.SettlerOpenApi,
                        },
                        CancellationTokenSource.Token);

            if (!payReqsForPayloadId.ContainsKey(id))
                payReqsForPayloadId[id] = payReq;
        }
    }

    private async Task CancelRideAsync(int idx)
    {
        var id = Guid.Parse(receivedBroadcastsTable.GetCell(idx, 1));

        var evs = receivedBroadcastsForPayloadId[id];

        foreach (var e in evs)
        {
            var settlerClient = e.GigGossipNode.SettlerSelector.GetSettlerClient(settings.NodeSettings.SettlerOpenApi);
            SettlerAPIResult.Check(await settlerClient.CancelGigAsync(await e.GigGossipNode.MakeSettlerAuthTokenAsync(settings.NodeSettings.SettlerOpenApi), e.BroadcastFrame.SignedRequestPayload.Id.ToString(), payReqsForPayloadId[id].ReplierCertificateId.ToString(), CancellationTokenSource.Token));
            WalletAPIResult.Check(await e.GigGossipNode.GetWalletClient().CancelInvoiceAsync(await e.GigGossipNode.MakeWalletAuthToken(), payReqsForPayloadId[id].DecodedReplyInvoice.PaymentHash, CancellationTokenSource.Token));
            break;
        }
    }


    private async void GigGossipNodeEventSource_OnInvoiceAccepted(object? sender, InvoiceAcceptedEventArgs e)
    {
        if (inDriverMode)
        {
            if (!e.InvoiceData.IsNetworkInvoice)
            {
                var thisBroadcast = e.GigGossipNode.GetAcceptedBroadcastsByReplyInvoiceHash(e.InvoiceData.PaymentHash);

                if (thisBroadcast == null)
                    return;

                ActiveSignedRequestPayloadId = thisBroadcast.SignedRequestPayloadId;

                var broadcasts = e.GigGossipNode.GetAcceptedNotCancelledBroadcasts().ToList();
                if (broadcasts.Count > 0)
                {
                    var settlerClient = e.GigGossipNode.SettlerSelector.GetSettlerClient(settings.NodeSettings.SettlerOpenApi);
                    var token = await e.GigGossipNode.MakeSettlerAuthTokenAsync(settings.NodeSettings.SettlerOpenApi);
                    foreach (var broadcast in broadcasts)
                    {
                        if (broadcast.ReplyInvoiceHash != e.InvoiceData.PaymentHash)
                        {
                            try
                            {
                                SettlerAPIResult.Check(await settlerClient.CancelGigAsync(token, broadcast.SignedRequestPayloadId.ToString(), broadcast.ReplierCertificateId.ToString(), CancellationTokenSource.Token));
                                WalletAPIResult.Check(await e.GigGossipNode.GetWalletClient().CancelInvoiceAsync(await e.GigGossipNode.MakeWalletAuthToken(), broadcast.ReplyInvoiceHash, CancellationTokenSource.Token));
                            }
                            catch (Exception ex)
                            {
                                await e.GigGossipNode.FlowLogger.TraceExceptionAsync(ex);
                                //if already cancelled or settled
                            }
                        }
                        e.GigGossipNode.MarkBroadcastAsCancelled(broadcast);
                    }
                }
                await directCom.StartAsync(e.GigGossipNode.NostrRelays);
            }
        }
    }


    private async void GigGossipNodeEventSource_OnCancelBroadcast(object? sender, CancelBroadcastEventArgs e)
    {
        if (!receivedBroadcastIdxesForPayloadIds.ContainsKey(e.CancelBroadcastFrame.SignedCancelRequestPayload.Id))
            return;
        var idx = receivedBroadcastIdxesForPayloadIds[e.CancelBroadcastFrame.SignedCancelRequestPayload.Id];
        receivedBroadcastsTable.InactivateRow(idx);
    }

    private static IEnumerable<(int idx, GeoLocation location)> GeoSteps(GeoLocation from, GeoLocation to, int steps)
    {
        for (int i = 0; i <= steps; i++)
        {
            var delta = i / (double)steps;
            yield return (steps - i, new GeoLocation(from.Latitude + (to.Latitude - from.Latitude) * delta, from.Longitude + (to.Longitude - from.Longitude) * delta));
        }
    }

    private static IEnumerable<(int idx, GeolocationRet location)> GeoSteps(ICollection<GeolocationRet> geometr, int steps)
    {
        var geometry = geometr.ToArray();
        var jump = (int)(geometry.Count() / steps) + 1;
        int z = steps;
        for (int i = 0; i < geometry.Count(); i += jump)
        {
            z--;
            yield return (z, geometry[i]);
        }
        yield return (0, geometry.Last());
    }

    private async Task DriverJourneyAsync(DetailedParameters detparams)
    {
        var keys = new List<string>(MockData.FakeAddresses.Keys);
        var myAddress = keys[(int)Random.Shared.NextInt64(MockData.FakeAddresses.Count)];
        var myStartLocation = new GeoLocation(MockData.FakeAddresses[myAddress].Latitude, MockData.FakeAddresses[myAddress].Longitude);

        var requestPayloadId = detparams.SignedRequestPayloadId;
        var pubkey = directPubkeys[requestPayloadId];

        {
            var route = SettlerAPIResult.Get<RouteRet>(await gigGossipNode.SettlerSelector.GetSettlerClient(settings.NodeSettings.SettlerOpenApi)
                .GetRouteAsync(await gigGossipNode.MakeSettlerAuthTokenAsync(settings.NodeSettings.SettlerOpenApi),
                myStartLocation.Latitude, myStartLocation.Longitude,
                detparams.FromLocation.Latitude, detparams.FromLocation.Longitude,
                CancellationTokenSource.Token));

            foreach (var (idx,location) in GeoSteps(route.Geometry,10))
            {
                AnsiConsole.MarkupLine($"({idx},{location.Lat},{location.Lon}) I am [orange1]driving[/] to meet rider");
                await directCom.SendMessageAsync(pubkey, new LocationFrame
                {
                    SignedRequestPayloadId = requestPayloadId,
                    Location = new GeoLocation { Latitude = location.Lat, Longitude = location.Lon },
                    Message = "I am going",
                    RideStatus = RideState.Started,
                    Direction = 0,
                }, false, DateTime.UtcNow + this.gigGossipNode.InvoicePaymentTimeout);
                Thread.Sleep(1000);
            }
        }
        AnsiConsole.MarkupLine("I have [orange1]arrived[/]");
        for (int i = 3; i > 0; i--)
        {
            AnsiConsole.MarkupLine($"({i}) I am [orange1]waiting[/] for rider");
            await directCom.SendMessageAsync(pubkey, new LocationFrame
            {
                SignedRequestPayloadId = requestPayloadId,
                Location = detparams.FromLocation,
                Message = "I am waiting",
                RideStatus = RideState.DriverWaitingForRider,
                Direction = 0,
            }, false, DateTime.UtcNow + this.gigGossipNode.InvoicePaymentTimeout);
            Thread.Sleep(1000);
        }

        AnsiConsole.MarkupLine("Rider [orange1]in the car[/]");

        {
            var route = SettlerAPIResult.Get<RouteRet>(await gigGossipNode.SettlerSelector.GetSettlerClient(settings.NodeSettings.SettlerOpenApi)
                .GetRouteAsync(await gigGossipNode.MakeSettlerAuthTokenAsync(settings.NodeSettings.SettlerOpenApi),
                detparams.FromLocation.Latitude, detparams.FromLocation.Longitude,
                detparams.ToLocation.Latitude, detparams.ToLocation.Longitude,
                CancellationTokenSource.Token));
            foreach (var (idx, location) in GeoSteps(route.Geometry, 10))
            {
                AnsiConsole.MarkupLine($"({idx},{location.Lat},{location.Lon}) We are going [orange1]togheter[/]");
                await directCom.SendMessageAsync(pubkey, new LocationFrame
                {
                    SignedRequestPayloadId = requestPayloadId,
                    Location = new GeoLocation { Latitude = location.Lat, Longitude = location.Lon },
                    Message = "We are driving",
                    RideStatus = RideState.RiderPickedUp,
                    Direction = 0,
                }, false, DateTime.UtcNow + this.gigGossipNode.InvoicePaymentTimeout);
                Thread.Sleep(1000);
            }
        }
        AnsiConsole.MarkupLine("We have [orange1]reached[/] the destination");
        await directCom.SendMessageAsync(pubkey, new LocationFrame
        {
            SignedRequestPayloadId = requestPayloadId,
            Location = detparams.ToLocation,
            Message = "Thank you",
            RideStatus = RideState.RiderDroppedOff,
            Direction = 0,
        }, false, DateTime.UtcNow + this.gigGossipNode.InvoicePaymentTimeout);
        AnsiConsole.MarkupLine("Good [orange1]bye[/]");
        ActiveSignedRequestPayloadId = Guid.Empty;
        inDriverMode = false;
    }

    private async Task OnAckFrame(string senderPublicKey, AckFrame ackframe)
    {
        if (ActiveSignedRequestPayloadId == Guid.Empty)
            return;
        if (ackframe.Parameters.SignedRequestPayloadId == ActiveSignedRequestPayloadId)
        {
            if (directSecrets.ContainsKey(ackframe.Parameters.SignedRequestPayloadId))
            {
                if (directSecrets[ackframe.Parameters.SignedRequestPayloadId] == ackframe.Secret)
                {
                    directPubkeys[ackframe.Parameters.SignedRequestPayloadId] = senderPublicKey;
                    AnsiConsole.WriteLine("rider ack:" + senderPublicKey);
                    receivedBroadcastsTable.Exit();
                    new Thread(async () => await DriverJourneyAsync(ackframe.Parameters)).Start();
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
            AnsiConsole.WriteLine("rider location:" + senderPublicKey + "|" + locationFrame.RideStatus.ToString() + "|" + locationFrame.Message + "|" + locationFrame.Location.ToString());
        }
    }
}

