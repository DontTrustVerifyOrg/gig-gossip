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

namespace RideShareCLIApp;

public partial class RideShareCLIApp
{
    BroadcastTopicResponse requestedRide = null;
    Dictionary<string,List<NewResponseEventArgs>> receivedResponsesForPaymentHashes = new();
    Dictionary<string, int> receivedResponseIdxesForPaymentHashes = new();
    Dictionary<string, Guid> driverIdxesForPaymentHashes = new();
    DataTable receivedResponsesTable = null;

    bool driverApproached;
    bool riderDroppedOff;

    async Task<BroadcastTopicResponse> RequestRide(Location fromLocation, Location toLocation, int precision, int waitingTimeForPickupMinutes)
    {
        var fromGh = GeoHash.Encode(latitude: fromLocation.Latitude, longitude: fromLocation.Longitude, numberOfChars: precision);
        var toGh = GeoHash.Encode(latitude: toLocation.Latitude, longitude: toLocation.Longitude, numberOfChars: precision);

        return await gigGossipNode.BroadcastTopicAsync(
            topic: new RideTopic
            {
                FromGeohash = fromGh,
                ToGeohash = toGh,
                PickupAfter = DateTime.Now,
                PickupBefore = DateTime.Now.AddMinutes(waitingTimeForPickupMinutes),
            },
            settings.NodeSettings.SettlerOpenApi,
            settings.NodeSettings.GetRiderProperties());

    }

    private async Task AcceptDriverAsync(int idx)
    {
        var paymentHash = receivedResponsesTable.GetCell(idx, 0);

        var evs = receivedResponsesForPaymentHashes[paymentHash];
        var e = evs.Aggregate((curMin, x) => (curMin == null || x.DecodedNetworkInvoice.NumSatoshis < curMin.DecodedNetworkInvoice.NumSatoshis) ? x : curMin);

        var paymentResult = await e.GigGossipNode.AcceptResponseAsync(e.ReplyPayloadCert, e.ReplyInvoice, e.DecodedReplyInvoice, e.NetworkInvoice, e.DecodedNetworkInvoice);
        if (paymentResult != GigLNDWalletAPIErrorCode.Ok)
        {
            AnsiConsole.MarkupLine($"[red]{paymentResult}[/]");
            return;
        }
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
                receivedResponseIdxesForPaymentHashes[paymentHash] = receivedResponsesForPaymentHashes.Count - 1;
                var fee = e.DecodedReplyInvoice.NumSatoshis;
                var netfee = e.DecodedNetworkInvoice.NumSatoshis;

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
                var minNetPr = (from ev in receivedResponsesForPaymentHashes[paymentHash] select ev.DecodedNetworkInvoice.NumSatoshis).Min();
                receivedResponsesTable.UpdateCell(receivedResponseIdxesForPaymentHashes[paymentHash], 7, minNetPr.ToString());
            }
        }

    }

    private async void GigGossipNodeEventSource_OnResponseReady(object? sender, ResponseReadyEventArgs e)
    {
        if (e.RequestPayloadId == requestedRide.SignedRequestPayload.Id)
        {
            await e.GigGossipNode.CancelBroadcastAsync(requestedRide.SignedCancelRequestPayload);
            await directCom.StartAsync(e.Reply.Relays);
            directPubkeys[e.RequestPayloadId] = e.Reply.PublicKey;
            new Thread(() => RiderJourneyAsync(e.RequestPayloadId, e.Reply.Secret)).Start();
        }
    }

    private void GigGossipNodeEventSource_OnInvoiceCancelled(object? sender, InvoiceCancelledEventArgs e)
    {
        lock (receivedResponsesForPaymentHashes)
        {
            if (!receivedResponseIdxesForPaymentHashes.ContainsKey(e.InvoiceData.PaymentHash))
                return;
            if (!e.InvoiceData.IsNetworkInvoice)
            {
                var idx = receivedResponseIdxesForPaymentHashes[e.InvoiceData.PaymentHash];
                receivedResponsesTable.InactivateRow(idx);
            }
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
            SignedRequestPayloadId = requestPayloadId,
            Location = new Location(),
            Message = "Ok,here I am",
            Secret = secret,
        }, false, DateTime.UtcNow+ this.gigGossipNode.InvoicePaymentTimeout);

        while(!driverApproached)
        {
            AnsiConsole.MarkupLine("I am [orange1]waiting[/] for the driver");
            await directCom.SendMessageAsync(pubkey, new LocationFrame
            {
                SignedRequestPayloadId = requestPayloadId,
                Location = new Location(),
                Message = "I am waiting",
                RideState = RideState.RiderWaitingForDriver
            }, false, DateTime.UtcNow + this.gigGossipNode.InvoicePaymentTimeout);
            Thread.Sleep(1000);
        }
        while (!riderDroppedOff)
        {
            AnsiConsole.MarkupLine("I am [orange1]in the car[/]");
            await directCom.SendMessageAsync(pubkey, new LocationFrame
            {
                SignedRequestPayloadId = requestPayloadId,
                Location = new Location(),
                Message = "I am in the car",
                RideState = RideState.RidingTogheter
            }, false, DateTime.UtcNow + this.gigGossipNode.InvoicePaymentTimeout);
            Thread.Sleep(1000);
        }
        AnsiConsole.MarkupLine("I have reached the [orange1]destination[/]");
        requestedRide = null;
    }

    private async Task OnDriverLocation(string senderPublicKey, LocationFrame locationFrame)
    {
        if (requestedRide == null)
            return;
        if (locationFrame.SignedRequestPayloadId == requestedRide.SignedRequestPayload.Id)
        {
            AnsiConsole.WriteLine("driver location:" + senderPublicKey + "|" + locationFrame.RideState.ToString() + "|" + locationFrame.Message + "|" + locationFrame.Location.ToString());
            if (locationFrame.RideState == RideState.DriverWaitingForRider)
            {
                driverApproached = true;
            }
            else if (locationFrame.RideState == RideState.RiderDroppedOff)
            {
                riderDroppedOff = true;
            }
        }
    }

}

