using System;
using Sharprompt;
using System.Globalization;
using GigGossipFrames;
using NGeoHash;
using Spectre.Console;
using NBitcoin;
using System.Diagnostics;
using CryptoToolkit;
using GigLNDWalletAPIClient;
using RideShareFrames;

namespace RideShareCLIApp;

public partial class RideShareCLIApp
{
    BroadcastTopicResponse requestedRide = null;
    GeoLocation fromLocation;
    GeoLocation toLocation;
    string fromAddress;
    string toAddress;
    Dictionary<string,List<NewResponseEventArgs>> receivedResponsesForPaymentHashes = new();
    Dictionary<string, int> receivedResponseIdxesForPaymentHashes = new();
    Dictionary<Guid, int> receivedResponseIdxesForReplyPayloadId = new();
    DataTable receivedResponsesTable = null;

    bool driverApproached;
    bool riderDroppedOff;

    async Task<BroadcastTopicResponse> RequestRide(string fromAddress, GeoLocation fromLocation, string toAddress, GeoLocation toLocation, int precision, int waitingTimeForPickupMinutes)
    {
        var fromGh = GeoHash.Encode(latitude: fromLocation.Latitude, longitude: fromLocation.Longitude, numberOfChars: precision);
        var toGh = GeoHash.Encode(latitude: toLocation.Latitude, longitude: toLocation.Longitude, numberOfChars: precision);
        this.fromLocation = fromLocation;
        this.toLocation = toLocation;
        this.fromAddress = fromAddress;
        this.toAddress = toAddress;

        return await gigGossipNode.BroadcastTopicAsync(
            topic: new RideTopic
            {
                FromGeohash = fromGh,
                ToGeohash = toGh,
                PickupAfter = DateTime.Now,
                PickupBefore = DateTime.Now.AddMinutes(waitingTimeForPickupMinutes),
                Distance = fromLocation.Distance(toLocation),
            },
            settings.NodeSettings.GetRiderProperties());

    }

    private async Task AcceptDriverAsync(int idx)
    {
        var paymentHash = receivedResponsesTable.GetCell(idx, 0);

        var evs = receivedResponsesForPaymentHashes[paymentHash];
        var e = evs.Aggregate((curMin, x) => (curMin == null || x.DecodedNetworkInvoice.ValueSat < curMin.DecodedNetworkInvoice.ValueSat) ? x : curMin);

        var paymentResult = await e.GigGossipNode.AcceptResponseAsync(e.ReplyPayloadCert, e.ReplyInvoice, e.DecodedReplyInvoice, e.NetworkInvoice, e.DecodedNetworkInvoice, settings.NodeSettings.FeeLimitSat, CancellationTokenSource.Token);
        if (paymentResult != GigLNDWalletAPIErrorCode.Ok)
        {
            AnsiConsole.MarkupLine($"[red]{paymentResult}[/]");
            return;
        }
    }

    private async Task CancelBroadcast()
    {
        await gigGossipNode.CancelBroadcastAsync(requestedRide.SignedCancelRequestPayload);
    }


    private async void GigGossipNodeEventSource_OnNewResponse(object? sender, NewResponseEventArgs e)
    {
        if (receivedResponsesTable == null)
            return;

        lock (receivedResponsesForPaymentHashes)
        {
            string paymentHash = e.DecodedReplyInvoice.PaymentHash;

            if (!receivedResponsesForPaymentHashes.ContainsKey(paymentHash))
            {
                receivedResponsesForPaymentHashes[paymentHash] = new List<NewResponseEventArgs> { e };
                receivedResponseIdxesForReplyPayloadId[e.ReplyPayloadCert.Id] = receivedResponsesForPaymentHashes.Count - 1;
                receivedResponseIdxesForPaymentHashes[paymentHash] = receivedResponsesForPaymentHashes.Count - 1;
                var fee = e.DecodedReplyInvoice.ValueSat;
                var netfee = e.DecodedNetworkInvoice.ValueSat;

                var taxiTopic = Crypto.DeserializeObject<RideTopic>(e.ReplyPayloadCert.Value.SignedRequestPayload.Value.Topic);
                var from = taxiTopic.FromGeohash;
                var tim = "(" + taxiTopic.PickupAfter.ToString(DATE_FORMAT) + "+" + ((int)(taxiTopic.PickupBefore - taxiTopic.PickupAfter).TotalMinutes).ToString() + ")";
                var to = taxiTopic.ToGeohash;

                receivedResponsesTable.AddRow(new string[] { paymentHash, e.ReplyPayloadCert.Id.ToString(), "1", from, tim, to, fee.ToString(), netfee.ToString() });

            }
            else
            {
                receivedResponsesForPaymentHashes[paymentHash].Add(e);
                receivedResponsesTable.UpdateCell(receivedResponseIdxesForPaymentHashes[paymentHash], 2, receivedResponsesForPaymentHashes[paymentHash].Count.ToString());
                var minNetPr = (from ev in receivedResponsesForPaymentHashes[paymentHash] select ev.DecodedNetworkInvoice.ValueSat).Min();
                receivedResponsesTable.UpdateCell(receivedResponseIdxesForPaymentHashes[paymentHash], 7, minNetPr.ToString());
            }
        }

    }

    private async void GigGossipNodeEventSource_OnResponseReady(object? sender, ResponseReadyEventArgs e)
    {
        if (requestedRide == null)
        {
            AnsiConsole.WriteLine("requestedRide is empty 2");
            return;
        }
        if (e.RequestPayloadId == requestedRide.SignedRequestPayload.Id)
        {
            await e.GigGossipNode.CancelBroadcastAsync(requestedRide.SignedCancelRequestPayload);
            await directCom.StartAsync(e.Reply.Relays);
            directPubkeys[e.RequestPayloadId] = e.Reply.PublicKey;
            new Thread(async () => await RiderJourneyAsync(e.RequestPayloadId, e.Reply.Secret)).Start();
        }
        else
            AnsiConsole.WriteLine("SignedRequestPayloadId mismatch 2");
    }

    private void GigGossipNodeEventSource_OnInvoiceCancelled(object? sender, InvoiceCancelledEventArgs e)
    {
        if (e.InvoiceData.IsNetworkInvoice)
            return;
        lock (receivedResponsesForPaymentHashes)
        {
            if (!receivedResponseIdxesForPaymentHashes.ContainsKey(e.InvoiceData.PaymentHash))
                return;

            var idx = receivedResponseIdxesForPaymentHashes[e.InvoiceData.PaymentHash];
            receivedResponsesTable.InactivateRow(idx);
        }
    }

    private void GigGossipNodeEventSource_OnResponseCancelled(object? sender, ResponseCancelledEventArgs e)
    {
        lock (receivedResponsesForPaymentHashes)
        {
            if (!receivedResponseIdxesForReplyPayloadId.ContainsKey(e.ReplierCertificateId))
                return;

            var idx = receivedResponseIdxesForReplyPayloadId[e.ReplierCertificateId];
            receivedResponsesTable.InactivateRow(idx);
        }
    }



    private async Task RiderJourneyAsync(Guid requestPayloadId,string secret)
    {
        driverApproached = false;
        riderDroppedOff = false;
        var pubkey = directPubkeys[requestPayloadId];
        AnsiConsole.MarkupLine("I am [orange1]sending[/] my location to the driver");
        await directCom.SendMessageAsync(pubkey, new AckFrame()
        {
            Secret = secret,
            Parameters = new DetailedParameters { SignedRequestPayloadId = requestPayloadId, FromAddress = fromAddress, FromLocation = fromLocation, ToAddress = toAddress, ToLocation = toLocation }
        }, false, DateTime.UtcNow + this.gigGossipNode.InvoicePaymentTimeout);

        lastDriverLocation = fromLocation;

        while (!driverApproached)
        {
            AnsiConsole.MarkupLine("I am [orange1]waiting[/] for the driver");
            await directCom.SendMessageAsync(pubkey, new LocationFrame
            {
                SignedRequestPayloadId = requestPayloadId,
                Location = fromLocation,
                Message = "I am waiting",
                RideStatus = RideState.Started,
                Direction = 0,
            }, false, DateTime.UtcNow + this.gigGossipNode.InvoicePaymentTimeout) ;
            Thread.Sleep(1000);
        }
        while (!riderDroppedOff)
        {
            AnsiConsole.MarkupLine("I am [orange1]in the car[/]");
            await directCom.SendMessageAsync(pubkey, new LocationFrame
            {
                SignedRequestPayloadId = requestPayloadId,
                Location = lastDriverLocation,
                Message = "I am in the car",
                RideStatus = RideState.RiderPickedUp,
                Direction = 0,
            }, false, DateTime.UtcNow + this.gigGossipNode.InvoicePaymentTimeout);
            Thread.Sleep(1000);
        }
        AnsiConsole.MarkupLine("I have reached the [orange1]destination[/]");
        requestedRide = null;
    }

    GeoLocation lastDriverLocation;

    private async Task OnDriverLocation(string senderPublicKey, LocationFrame locationFrame)
    {
        if (requestedRide == null)
        {
            AnsiConsole.WriteLine("requestedRide is empty");
            return;
        }
        if (locationFrame.SignedRequestPayloadId == requestedRide.SignedRequestPayload.Id)
        {
            AnsiConsole.WriteLine("driver location:" + senderPublicKey + "|" + locationFrame.RideStatus.ToString() + "|" + locationFrame.Message + "|" + locationFrame.Location.ToString());
            if (locationFrame.RideStatus >= RideState.DriverWaitingForRider)
                driverApproached = true;
            if (locationFrame.RideStatus >= RideState.RiderDroppedOff)
                riderDroppedOff = true;
            lastDriverLocation = locationFrame.Location;
        }
        else
            AnsiConsole.WriteLine("SignedRequestPayloadId mismatch");
    }
}

