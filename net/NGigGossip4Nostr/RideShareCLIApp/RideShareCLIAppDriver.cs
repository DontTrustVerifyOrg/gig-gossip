using System;
using System.Diagnostics;
using System.Threading;

using GigGossip;
using GigGossipSettlerAPIClient;
using GigLNDWalletAPIClient;
using NBitcoin.Secp256k1;
using NGeoHash;
using NGigGossip4Nostr;
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
        if (e.BroadcastFrame.JobRequest.Header.TopicCase !=  JobRequestHeader.TopicOneofCase.RideShare)
            return;

        var taxiTopic = e.BroadcastFrame.JobRequest.Header.RideShare;


        if (inDriverMode)
        {
            Guid id = e.BroadcastFrame.JobRequest.Header.JobRequestId.AsGuid();

            if (!feesPerBroadcastId.ContainsKey(id))
            {
                long fee = Random.Shared.NextInt64(1000, 2000);
                feesPerBroadcastId[id] = fee;

                var from = taxiTopic.FromGeohash;
                var tim = "(" + taxiTopic.PickupAfter.AsUtcDateTime().ToString(DATE_FORMAT) + "+" + ((int)(taxiTopic.PickupBefore.AsUtcDateTime() - taxiTopic.PickupAfter.AsUtcDateTime()).TotalMinutes).ToString() + ")";
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

    private async Task AcceptRideAsync(int idx, GeoLocation myLocation, string message)
    {
        var id = Guid.Parse(receivedBroadcastsTable.GetCell(idx, 1));

        var evs = receivedBroadcastsForPayloadId[id];
        var fee = receivedBroadcastsFees[id];

        var secret = Crypto.GenerateRandomPreimage().AsHex();
        directSecrets[id] = secret;

        foreach (var e in evs)
        {
            if (e.BroadcastFrame.JobRequest.Header.TopicCase !=  JobRequestHeader.TopicOneofCase.RideShare)
                continue;

            var taxiTopic = e.BroadcastFrame.JobRequest.Header.RideShare;

            var reply = new RideShareReply()
            {
                PublicKey = new PublicKey { Value = e.GigGossipNode.PublicKey.AsECXOnlyPubKey().ToBytes().AsByteString() },
                Secret = secret,
                Location = myLocation,
                Message = message,
            };
            reply.Relays.Add((from r in e.GigGossipNode.NostrRelays select new URI { Value = r}));

            var payReq = await e.GigGossipNode.AcceptBroadcastAsync(e.PeerPublicKey, e.BroadcastFrame,
                        new AcceptBroadcastResponse()
                        {
                            Properties = settings.NodeSettings.GetAllDriverProperties(),
                            RideShareReply = reply,
                            Fee = fee,
                            Country = "PL",
                            Currency = "BTC",
                            SettlerServiceUri = settings.NodeSettings.SettlerOpenApi,                             
                        },
                        async (payReq) =>
                        {
                            if (!payReqsForPayloadId.ContainsKey(id))
                                payReqsForPayloadId[id] = payReq;
                        },
                        CancellationTokenSource.Token);

        }
    }

    private async Task CancelRideAsync(int idx)
    {
        var id = Guid.Parse(receivedBroadcastsTable.GetCell(idx, 1));

        var evs = receivedBroadcastsForPayloadId[id];

        foreach (var e in evs)
        {
            var settlerClient = e.GigGossipNode.SettlerSelector.GetSettlerClient(settings.NodeSettings.SettlerOpenApi);
            SettlerAPIResult.Check(await settlerClient.CancelGigAsync(await e.GigGossipNode.MakeSettlerAuthTokenAsync(settings.NodeSettings.SettlerOpenApi), e.BroadcastFrame.JobRequest.Header.JobRequestId.AsGuid(), payReqsForPayloadId[id].ReplierCertificateId, CancellationTokenSource.Token));
            WalletAPIResult.Check(await e.GigGossipNode.GetWalletClient().CancelInvoiceAsync(await e.GigGossipNode.MakeWalletAuthToken(), payReqsForPayloadId[id].DecodedReplyInvoice.PaymentHash, CancellationTokenSource.Token));
            break;
        }
    }


    private async void GigGossipNodeEventSource_OnInvoiceAccepted(object? sender, JobInvoiceAcceptedEventArgs e)
    {
        using var TL = TRACE.Log();
        try
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
                                    SettlerAPIResult.Check(await settlerClient.CancelGigAsync(token, broadcast.SignedRequestPayloadId, broadcast.ReplierCertificateId, CancellationTokenSource.Token));
                                    WalletAPIResult.Check(await e.GigGossipNode.GetWalletClient().CancelInvoiceAsync(await e.GigGossipNode.MakeWalletAuthToken(), broadcast.ReplyInvoiceHash, CancellationTokenSource.Token));
                                }
                                catch (Exception ex)
                                {
                                    TL.Exception(ex);
                                    //if already cancelled or settled
                                }
                            }
                            e.GigGossipNode.MarkBroadcastAsCancelled(broadcast);
                        }
                    }
                    directTimer.Start();
                }
            }
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }


    private async void GigGossipNodeEventSource_OnCancelBroadcast(object? sender, CancelBroadcastEventArgs e)
    {
        if (!receivedBroadcastIdxesForPayloadIds.ContainsKey(e.CancelBroadcastFrame.CancelJobRequest.Header.JobRequestId.AsGuid()))
            return;
        var idx = receivedBroadcastIdxesForPayloadIds[e.CancelBroadcastFrame.CancelJobRequest.Header.JobRequestId.AsGuid()];
        receivedBroadcastsTable.InactivateRow(idx);
    }

    private static IEnumerable<(int idx, GeoLocation location)> GeoSteps(GeoLocation from, GeoLocation to, int steps)
    {
        for (int i = 0; i <= steps; i++)
        {
            var delta = i / (double)steps;
            yield return (steps - i, new GeoLocation { Latitude = from.Latitude + (to.Latitude - from.Latitude) * delta, Longitude = from.Longitude + (to.Longitude - from.Longitude) * delta });
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

    private void SupportPause()
    {
        return;
        if (Console.KeyAvailable)
        {
            var k = Console.ReadKey().Key;
            if (k == ConsoleKey.Spacebar)
            {
                AnsiConsole.WriteLine("PAUSED");
                Console.ReadLine();
            }
        }
    }

    private async Task DriverJourneyAsync(LocationFrame locationFrame)
    {
        try
        {
            var keys = new List<string>(MockData.FakeAddresses.Keys);
            var myAddress = keys[(int)Random.Shared.NextInt64(MockData.FakeAddresses.Count)];
            var myStartLocation = new GeoLocation { Latitude = MockData.FakeAddresses[myAddress].Latitude, Longitude= MockData.FakeAddresses[myAddress].Longitude };

            var requestPayloadId = locationFrame.JobRequestId.AsGuid();
            var pubkey = directPubkeys[requestPayloadId];

            {
                var route = SettlerAPIResult.Get<RouteRet>(await gigGossipNode.SettlerSelector.GetSettlerClient(settings.NodeSettings.SettlerOpenApi)
                    .GetRouteAsync(await gigGossipNode.MakeSettlerAuthTokenAsync(settings.NodeSettings.SettlerOpenApi),
                    myStartLocation.Latitude, myStartLocation.Longitude,
                    locationFrame.FromLocation.Latitude, locationFrame.FromLocation.Longitude,
                    CancellationTokenSource.Token));

                foreach (var (idx, location) in GeoSteps(route.Geometry, 5))
                {
                    SupportPause();
                    AnsiConsole.MarkupLine($"({idx},{location.Lat},{location.Lon}) I am [orange1]driving[/] to meet rider");
                    await gigGossipNode.SendMessageAsync(pubkey, new Frame
                    {
                        Location = new LocationFrame
                        {
                            JobRequestId = requestPayloadId.AsUUID(),
                            Location = new GeoLocation { Latitude = location.Lat, Longitude = location.Lon },
                            Message = "I am going",
                            RideStatus = RideState.Started,
                            FromAddress = locationFrame.FromAddress,
                            ToAddress = locationFrame.ToAddress,
                            FromLocation = locationFrame.FromLocation,
                            ToLocation = locationFrame.ToLocation,
                            Secret = locationFrame.Secret,
                            JobReplyId = locationFrame.JobReplyId,
                            SecurityCenterUri = locationFrame.SecurityCenterUri,
                        }
                    }, false, DateTime.UtcNow + this.gigGossipNode.InvoicePaymentTimeout);
                    Thread.Sleep(SIMULT_STEP_TIME);
                }
            }
            AnsiConsole.MarkupLine("I have [orange1]arrived[/]");
            for (int i = 3; i > 0; i--)
            {
                SupportPause();
                AnsiConsole.MarkupLine($"({i}) I am [orange1]waiting[/] for rider");
                await gigGossipNode.SendMessageAsync(pubkey, new Frame
                {
                    Location = new LocationFrame
                    {
                        JobRequestId = requestPayloadId.AsUUID(),
                        Location = locationFrame.FromLocation,
                        FromAddress = locationFrame.FromAddress,
                        ToAddress = locationFrame.ToAddress,
                        FromLocation = locationFrame.FromLocation,
                        ToLocation = locationFrame.ToLocation,
                        Secret = locationFrame.Secret,
                        Message = "I am waiting",
                        RideStatus = RideState.DriverWaitingForRider,
                        JobReplyId = locationFrame.JobReplyId,
                        SecurityCenterUri = locationFrame.SecurityCenterUri,
                    }
                }, false, DateTime.UtcNow + this.gigGossipNode.InvoicePaymentTimeout);
                Thread.Sleep(SIMULT_STEP_TIME);
            }

            AnsiConsole.MarkupLine("Rider [orange1]in the car[/]");

            {
                var route = SettlerAPIResult.Get<RouteRet>(await gigGossipNode.SettlerSelector.GetSettlerClient(settings.NodeSettings.SettlerOpenApi)
                    .GetRouteAsync(await gigGossipNode.MakeSettlerAuthTokenAsync(settings.NodeSettings.SettlerOpenApi),
                    locationFrame.FromLocation.Latitude, locationFrame.FromLocation.Longitude,
                    locationFrame.ToLocation.Latitude, locationFrame.ToLocation.Longitude,
                    CancellationTokenSource.Token));
                foreach (var (idx, location) in GeoSteps(route.Geometry, 5))
                {
                    SupportPause();
                    AnsiConsole.MarkupLine($"({idx},{location.Lat},{location.Lon}) We are going [orange1]togheter[/]");
                    await gigGossipNode.SendMessageAsync(pubkey, new Frame
                    {
                        Location = new LocationFrame
                        {
                            JobRequestId = requestPayloadId.AsUUID(),
                            Location = new GeoLocation { Latitude = location.Lat, Longitude = location.Lon },
                            Message = "We are driving",
                            RideStatus = RideState.RiderPickedUp,
                            FromAddress = locationFrame.FromAddress,
                            ToAddress = locationFrame.ToAddress,
                            FromLocation = locationFrame.FromLocation,
                            ToLocation = locationFrame.ToLocation,
                            Secret = locationFrame.Secret,
                            JobReplyId = locationFrame.JobReplyId,
                            SecurityCenterUri = locationFrame.SecurityCenterUri,
                        }
                    }, false, DateTime.UtcNow + this.gigGossipNode.InvoicePaymentTimeout);
                    Thread.Sleep(SIMULT_STEP_TIME);
                }
            }
            AnsiConsole.MarkupLine("We have [orange1]reached[/] the destination");
            await gigGossipNode.SendMessageAsync(pubkey, new Frame
            {
                Location = new LocationFrame
                {
                    JobRequestId = requestPayloadId.AsUUID(),
                    Location = locationFrame.ToLocation,
                    Message = "Thank you",
                    RideStatus = RideState.Completed,
                    FromAddress = locationFrame.FromAddress,
                    ToAddress = locationFrame.ToAddress,
                    FromLocation = locationFrame.FromLocation,
                    ToLocation = locationFrame.ToLocation,
                    Secret = locationFrame.Secret,
                    JobReplyId = locationFrame.JobReplyId,
                    SecurityCenterUri = locationFrame.SecurityCenterUri,
                }
            }, false, DateTime.UtcNow + this.gigGossipNode.InvoicePaymentTimeout);
            AnsiConsole.MarkupLine("Good [orange1]bye[/]");
            ActiveSignedRequestPayloadId = Guid.Empty;
            directTimer.Stop();
            inDriverMode = false;
        }
        catch(Exception ex)
        {
            AnsiConsole.WriteException(ex);
        }
    }


    DateTime lastRiderSeenAt = DateTime.MinValue;

    private async Task OnRiderLocation(string senderPublicKey, LocationFrame locationFrame)
    {
        if (ActiveSignedRequestPayloadId == Guid.Empty)
            return;
        if (locationFrame.JobRequestId.AsGuid() == ActiveSignedRequestPayloadId)
        {
            if (directSecrets.ContainsKey(locationFrame.JobRequestId.AsGuid()))
            {
                if (directSecrets[locationFrame.JobRequestId.AsGuid()] == locationFrame.Secret)
                {
                    if (!directPubkeys.ContainsKey(locationFrame.JobRequestId.AsGuid()))
                    {
                        directPubkeys[locationFrame.JobRequestId.AsGuid()] = senderPublicKey;
                        AnsiConsole.WriteLine("rider ack:" + senderPublicKey);
                        receivedBroadcastsTable.Exit();
                        new Thread(async () => await DriverJourneyAsync(locationFrame)).Start();
                    }
                    else
                    {
                        var pubkey = directPubkeys[locationFrame.JobRequestId.AsGuid()];
                        AnsiConsole.WriteLine("rider location:" + senderPublicKey + "|" + locationFrame.RideStatus.ToString() + "|" + locationFrame.Message + "|" + locationFrame.Location.ToString());
                        lastRiderSeenAt = DateTime.UtcNow;
                    }
                }
            }
        }

    }

}

