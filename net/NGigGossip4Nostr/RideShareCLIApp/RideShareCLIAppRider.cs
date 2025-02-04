using System;
using Sharprompt;
using System.Globalization;
using GigGossip;
using NGeoHash;
using Spectre.Console;
using NBitcoin;
using System.Diagnostics;

using GigLNDWalletAPIClient;

namespace RideShareCLIApp;

public partial class RideShareCLIApp
{
    BroadcastRequest requestedRide = null;
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

    async Task<(BroadcastRequest Request, List<string> Fails)> RequestRide(string fromAddress, GeoLocation fromLocation, string toAddress, GeoLocation toLocation, int precision, DateTime pickupAfter, DateTime pickupBefore,
        string country, string currency, long suggestedPrice,
        Func<BroadcastRequest, Task> pre)
    {
        var fromGh = GeoHash.Encode(latitude: fromLocation.Latitude, longitude: fromLocation.Longitude, numberOfChars: precision);
        var toGh = GeoHash.Encode(latitude: toLocation.Latitude, longitude: toLocation.Longitude, numberOfChars: precision);
        this.fromLocation = fromLocation;
        this.toLocation = toLocation;
        this.fromAddress = fromAddress;
        this.toAddress = toAddress;

        return (await gigGossipNode.BroadcastTopicAsync(
            topic: new JobTopic
            {
                Country = country,
                Currency = currency,
                SuggestedPrice = suggestedPrice,
                Geohash = fromGh,
                RideShare = new RideShareTopic
                {
                    FromGeohash = fromGh,
                    ToGeohash = toGh,
                    PickupAfter = pickupAfter.AsUnixTimestamp(),
                    PickupBefore = pickupBefore.AsUnixTimestamp(),
                    Distance = fromLocation.Distance(toLocation),
                }
            },
            settings.NodeSettings.GetRiderProperties(),
            pre));
    }

    async Task<(BroadcastRequest Request, List<string> Fails)> RequestBlockDelivery(string senderName, string blockDescription, string fromAddress, GeoLocation fromLocation, int precision, DateTime pickupAfter, DateTime pickupBefore, DateTime finishBefore, string country, string currency, long suggestedPrice, Func<BroadcastRequest, Task> pre)
    {
        this.fromLocation = fromLocation;
        this.fromAddress = fromAddress;
        var fromGh = GeoHash.Encode(latitude: fromLocation.Latitude, longitude: fromLocation.Longitude, numberOfChars: precision);

        return (await gigGossipNode.BroadcastTopicAsync(
            topic: new JobTopic
            {
                Country = country,
                Currency = currency,
                SuggestedPrice = suggestedPrice,
                Geohash = fromGh,
                BlockDelivery = new BlockDeliveryTopic
                {
                    FromAddress = fromAddress,
                    FromLocation = fromLocation,
                    PickupAfter = pickupAfter.AsUnixTimestamp(),
                    PickupBefore = pickupBefore.AsUnixTimestamp(),
                    FinishBefore = finishBefore.AsUnixTimestamp(),
                }
            },
            settings.NodeSettings.GetRiderProperties(),
            pre
        ));
    }

    private async Task AcceptDriverAsync(int idx)
    {
        var paymentHash = receivedResponsesTable.GetCell(idx, 0);

        var evs = receivedResponsesForPaymentHashes[paymentHash];
        var e = evs.Aggregate((curMin, x) => (curMin == null || x.DecodedNetworkInvoice.Amount < curMin.DecodedNetworkInvoice.Amount) ? x : curMin);

        var balance = WalletAPIResult.Get<AccountBalanceDetails>(await gigGossipNode.GetWalletClient().GetBalanceAsync(await gigGossipNode.MakeWalletAuthToken(), CancellationTokenSource.Token)).AvailableAmount;


        LNDWalletErrorCode paymentResult = LNDWalletErrorCode.Ok;

        if (balance < e.DecodedReplyInvoice.Amount + e.DecodedNetworkInvoice.Amount + settings.NodeSettings.FeeLimitSat * 2)
        {
            paymentResult = LNDWalletErrorCode.NotEnoughFunds;
        }
        else
        {

            var networkPayState = await e.GigGossipNode.PayInvoiceAsync(e.NetworkPaymentRequest, e.DecodedNetworkInvoice.PaymentHash, settings.NodeSettings.FeeLimitSat, CancellationToken.None);
            if (networkPayState != LNDWalletErrorCode.Ok)
                paymentResult = networkPayState;
            else
            {
                var replyPayState = await e.GigGossipNode.PayInvoiceAsync(e.ReplyInvoice, e.DecodedReplyInvoice.PaymentHash, settings.NodeSettings.FeeLimitSat, CancellationToken.None);
                if (replyPayState != LNDWalletErrorCode.Ok)
                    paymentResult = replyPayState;
            }
        }

        if (paymentResult != LNDWalletErrorCode.Ok)
        {
            AnsiConsole.MarkupLine($"[red]{paymentResult}[/]");
            return;
        }
    }

    private async Task CancelBroadcast()
    {
        await gigGossipNode.CancelBroadcastAsync(requestedRide.CancelJobRequest);
    }


    private async void GigGossipNodeEventSource_OnNewResponse(object? sender, NewResponseEventArgs e)
    {
        if (receivedResponsesTable == null)
            return;

        lock (receivedResponsesForPaymentHashes)
        {
            if (e.ReplyPayloadCert.Header.JobRequest.Header.Topic.ValueCase != JobTopic.ValueOneofCase.RideShare)
                return;

            var taxiTopic = e.ReplyPayloadCert.Header.JobRequest.Header.Topic.RideShare;

            Dictionary<string, byte[]> certprops = new(from x in e.ReplyPayloadCert.Header.Header.Properties select KeyValuePair.Create(x.Name, x.Value.ToArray()));

            if (!new HashSet<string>(certprops.Keys)
                    .IsSupersetOf(settings.NodeSettings.GetRequiredDriverProperties()))
            {
                Console.WriteLine("DriverProperties are not satisfied");
                return;//ignore drivers without all required properties
            }

            string paymentHash = e.DecodedReplyInvoice.PaymentHash;

            if (!receivedResponsesForPaymentHashes.ContainsKey(paymentHash))
            {
                receivedResponsesForPaymentHashes[paymentHash] = new List<NewResponseEventArgs> { e };
                receivedResponseIdxesForReplyPayloadId[e.ReplyPayloadCert.Header.JobReplyId.AsGuid()] = receivedResponsesForPaymentHashes.Count - 1;
                receivedResponseIdxesForPaymentHashes[paymentHash] = receivedResponsesForPaymentHashes.Count - 1;
                var fee = e.DecodedReplyInvoice.Amount;
                var netfee = e.DecodedNetworkInvoice.Amount;
                var from = taxiTopic.FromGeohash;
                var tim = "(" + taxiTopic.PickupAfter.AsUtcDateTime().ToString(DATE_FORMAT) + "+" + ((int)(taxiTopic.PickupBefore.AsUtcDateTime() - taxiTopic.PickupAfter.AsUtcDateTime()).TotalMinutes).ToString() + ")";
                var to = taxiTopic.ToGeohash;

                receivedResponsesTable.AddRow(new string[] { paymentHash, e.ReplyPayloadCert.Header.JobReplyId.AsGuid().ToString(), "1", from, tim, to, fee.ToString(), netfee.ToString() });

            }
            else
            {
                receivedResponsesForPaymentHashes[paymentHash].Add(e);
                receivedResponsesTable.UpdateCell(receivedResponseIdxesForPaymentHashes[paymentHash], 2, receivedResponsesForPaymentHashes[paymentHash].Count.ToString());
                var minNetPr = (from ev in receivedResponsesForPaymentHashes[paymentHash] select ev.DecodedNetworkInvoice.Amount).Min();
                receivedResponsesTable.UpdateCell(receivedResponseIdxesForPaymentHashes[paymentHash], 7, minNetPr.ToString());
            }
        }

    }

    private async void GigGossipNodeEventSource_OnResponseReady(object? sender, ResponseReadyEventArgs e)
    {
        try
        {
            AnsiConsole.WriteLine("GigGossipNodeEventSource_OnResponseReady");
            if (requestedRide == null)
            {
                AnsiConsole.WriteLine("requestedRide is empty 2");
                return;
            }
            if (e.RequestPayloadId == requestedRide.JobRequest.Header.JobRequestId.AsGuid())
            {
                await e.GigGossipNode.CancelBroadcastAsync(requestedRide.CancelJobRequest);
                await gigGossipNode.AddTempRelaysAsync((from u in e.Reply.RideShare.Relays select u.AsUri().AbsoluteUri).ToArray());
                directTimer.Start();
                directPubkeys[e.RequestPayloadId] = e.Reply.RideShare.PublicKey.AsHex();
                new Thread(async () => await RiderJourneyAsync(e.RequestPayloadId, e.ReplierCertificateId, e.Reply.RideShare.Secret, settings.NodeSettings.SettlerOpenApi)).Start();
            }
            else
                AnsiConsole.WriteLine("SignedRequestPayloadId mismatch 2");
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
        }
    }

    private void GigGossipNodeEventSource_OnInvoiceCancelled(object? sender, JobInvoiceCancelledEventArgs e)
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



    private async Task RiderJourneyAsync(Guid signedRequestPayloadId, Guid replierCertificateId, string secret, Uri settlerServiceUri)
    {
        try
        {
            driverApproached = false;
            riderDroppedOff = false;
            var pubkey = directPubkeys[signedRequestPayloadId];
            AnsiConsole.MarkupLine("I am [orange1]sending[/] my location to the driver");
            await gigGossipNode.SendMessageAsync(pubkey, new Frame
            {
                Location = new LocationFrame
                {
                    Secret = secret,
                    JobRequestId = signedRequestPayloadId.AsUUID(),
                    JobReplyId = replierCertificateId.AsUUID(),
                    SecurityCenterUri = settlerServiceUri.AsURI(),
                    Location = fromLocation,
                    Message = "Hello From Rider!",
                    RideStatus = RideState.Started,
                    FromAddress = fromAddress,
                    FromLocation = fromLocation,
                    ToAddress = toAddress,
                    ToLocation = toLocation,
                }
            }, false, DateTime.UtcNow + this.gigGossipNode.InvoicePaymentTimeout);

            lastDriverLocation = fromLocation;

            while (!driverApproached)
            {
                AnsiConsole.MarkupLine("I am [orange1]waiting[/] for the driver");
                await gigGossipNode.SendMessageAsync(pubkey, new Frame
                {
                    Location = new LocationFrame
                    {
                        Secret = secret,
                        JobRequestId = signedRequestPayloadId.AsUUID(),
                        JobReplyId = replierCertificateId.AsUUID(),
                        SecurityCenterUri = settlerServiceUri.AsURI(),
                        Location = fromLocation,
                        Message = "I am waiting",
                        RideStatus = RideState.Started,
                        FromAddress = fromAddress,
                        FromLocation = fromLocation,
                        ToAddress = toAddress,
                        ToLocation = toLocation,
                    }
                }, false, DateTime.UtcNow + this.gigGossipNode.InvoicePaymentTimeout);
                Thread.Sleep(SIMULT_STEP_TIME);
            }
            while (!riderDroppedOff)
            {
                AnsiConsole.MarkupLine("I am [orange1]in the car[/]");
                await gigGossipNode.SendMessageAsync(pubkey, new Frame
                {
                    Location = new LocationFrame
                    {
                        Secret = secret,
                        JobRequestId = signedRequestPayloadId.AsUUID(),
                        JobReplyId = replierCertificateId.AsUUID(),
                        SecurityCenterUri = settlerServiceUri.AsURI(),
                        Location = lastDriverLocation,
                        Message = "I am in the car",
                        RideStatus = RideState.RiderPickedUp,
                        FromAddress = fromAddress,
                        FromLocation = fromLocation,
                        ToAddress = toAddress,
                        ToLocation = toLocation,
                    }
                }, false, DateTime.UtcNow + this.gigGossipNode.InvoicePaymentTimeout);
                Thread.Sleep(SIMULT_STEP_TIME);
            }
            AnsiConsole.MarkupLine("I have reached the [orange1]destination[/]");
            requestedRide = null;
            directTimer.Stop();
        }
        catch(Exception ex)
        {
            AnsiConsole.WriteException(ex);
        }
    }

    GeoLocation lastDriverLocation;
    DateTime lastDriverSeenAt = DateTime.MinValue;

    private async Task OnDriverLocation(string senderPublicKey, LocationFrame locationFrame)
    {
        if (requestedRide == null)
        {
            AnsiConsole.WriteLine("requestedRide is empty");
            return;
        }
        if (locationFrame.JobRequestId.AsGuid() == requestedRide.JobRequest.Header.JobRequestId.AsGuid())
        {
            AnsiConsole.WriteLine("driver location:" + senderPublicKey + "|" + locationFrame.RideStatus.ToString() + "|" + locationFrame.Message + "|" + locationFrame.Location.ToString());
            if (locationFrame.RideStatus == RideState.DriverWaitingForRider)
                driverApproached = true;
            if (locationFrame.RideStatus == RideState.Completed)
                riderDroppedOff = true;
            lastDriverLocation = locationFrame.Location;
            lastDriverSeenAt = DateTime.UtcNow;
        }
        else
            AnsiConsole.WriteLine("SignedRequestPayloadId mismatch");
    }

}

