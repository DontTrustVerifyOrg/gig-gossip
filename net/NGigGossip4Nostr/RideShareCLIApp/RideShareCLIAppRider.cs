using System;
using Sharprompt;
using System.Globalization;
using GigGossipFrames;
using NGeoHash;
using Spectre.Console;
using NBitcoin;
using System.Diagnostics;
using CryptoToolkit;

namespace RideShareCLIApp;

public partial class RideShareCLIApp
{
    BroadcastTopicResponse requestedRide = null;
    List<NewResponseEventArgs> receivedResponses = new();
    DataTable receivedResponsesTable = null;
    Dictionary<string, int> receivedResponsesIdxesForPaymentHashes = new();

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
        var e = receivedResponses[idx];
        await e.GigGossipNode.AcceptResponseAsync(e.ReplyPayloadCert, e.ReplyInvoice, e.DecodedReplyInvoice, e.NetworkInvoice, e.DecodedNetworkInvoice);
        await e.GigGossipNode.CancelBroadcastAsync(requestedRide.SignedCancelRequestPayload);
    }

    private async void GigGossipNodeEventSource_OnNewResponse(object? sender, NewResponseEventArgs e)
    {
        if (receivedResponsesTable == null)
            return;
        receivedResponses.Add(e);
        receivedResponsesIdxesForPaymentHashes[e.DecodedReplyInvoice.PaymentHash] = receivedResponses.Count - 1;
        var fee = e.DecodedReplyInvoice.NumSatoshis;
        var netfee = e.DecodedNetworkInvoice.NumSatoshis;

        var taxiTopic = Crypto.DeserializeObject<RideTopic>(e.ReplyPayloadCert.Value.SignedRequestPayload.Value.Topic);
        var from = taxiTopic.FromGeohash;
        var tim = "(" + taxiTopic.PickupAfter.ToString(DATE_FORMAT) + "+" + ((int)(taxiTopic.PickupBefore - taxiTopic.PickupAfter).TotalMinutes).ToString() + ")";
        var to = taxiTopic.ToGeohash;

        receivedResponsesTable.AddRow(new string[] { from,tim,to, fee.ToString(), netfee.ToString(), e.ReplyPayloadCert.Value.SignedRequestPayload.Id.ToString() });
    }

    private async void GigGossipNodeEventSource_OnResponseReady(object? sender, ResponseReadyEventArgs e)
    {
        await directCom.StartAsync(e.Reply.Relays);
        directPubkeys[e.RequestPayloadId] = e.Reply.PublicKey;
        new Thread(() => RiderJourneyAsync(e.RequestPayloadId,e.Reply.Secret)).Start();
    }

    private void GigGossipNodeEventSource_OnInvoiceCancelled(object? sender, InvoiceCancelledEventArgs e)
    {
        if (!receivedResponsesIdxesForPaymentHashes.ContainsKey(e.InvoiceData.PaymentHash))
            return;
        if (!e.InvoiceData.IsNetworkInvoice)
        {
            var idx = receivedResponsesIdxesForPaymentHashes[e.InvoiceData.PaymentHash];
            receivedResponses.RemoveAt(idx);
            receivedResponsesTable.RemoveRow(idx);
            receivedResponsesIdxesForPaymentHashes.Remove(e.InvoiceData.PaymentHash);
        }
    }


    bool driverApproached;
    bool riderDroppedOff;
    private async Task RiderJourneyAsync(Guid requestPayloadId,string secret)
    {
        receivedResponsesTable.Exit();
        driverApproached = false;
        riderDroppedOff = false;
        var pubkey = directPubkeys[requestPayloadId];
        AnsiConsole.MarkupLine("I am [orange1]sending[/] my location to the driver");
        await directCom.SendMessageAsync(pubkey, new AckFrame()
        {
            RequestPayloadId = requestPayloadId,
            Location = new Location(),
            Message = "Ok,here I am",
            Secret = secret,
        }, true);
        while(!driverApproached)
        {
            AnsiConsole.MarkupLine("I am [orange1]waiting[/] for the driver");
            await directCom.SendMessageAsync(pubkey, new LocationFrame
            {
                RequestPayloadId = requestPayloadId,
                Location = new Location(),
                Message = "I am waiting",
                RideState = RideState.RiderWaitingForDriver
            }, true);
            Thread.Sleep(1000);
        }
        while (!riderDroppedOff)
        {
            AnsiConsole.MarkupLine("I am [orange1]in the car[/]");
            await directCom.SendMessageAsync(pubkey, new LocationFrame
            {
                RequestPayloadId = requestPayloadId,
                Location = new Location(),
                Message = "I am in the car",
                RideState = RideState.RidingTogheter
            }, true);
            Thread.Sleep(1000);
        }
        AnsiConsole.MarkupLine("I have reached the [orange1]destination[/]");
    }

    private async Task OnDriverLocation(string senderPublicKey, LocationFrame locationFrame)
    {
        var pubkey = directPubkeys[locationFrame.RequestPayloadId];
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

