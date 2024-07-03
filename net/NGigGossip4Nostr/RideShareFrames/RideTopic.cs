using System.Text.Json.Serialization;

namespace RideShareFrames;

[Serializable]
public class RideTopic
{
    public required string FromGeohash { get; set; }
    public required string ToGeohash { get; set; }
    public required DateTime PickupAfter { get; set; }
    public required DateTime PickupBefore { get; set; }
    public required double Distance { get; set; }
}

[Serializable]
public class ConnectionReply
{
    public required string PublicKey { get; set; }
    public required string[] Relays { get; set; }
    public required string Secret { get; set; }
    public required GeoLocation Location { get; set; }
    public required string Message { get; set; }
}

[Serializable]
public enum RideState
{
    Started = 0,
    DriverWaitingForRider = 1,
    RiderPickedUp = 2,
    RiderDroppedOff = 3,
    Disputed = 4,
    Failed = 5,
}

[Serializable]
public class LocationFrame
{
    public required Guid SignedRequestPayloadId { get; set; }
    public required string Secret { get; set; }
    public required GeoLocation FromLocation { get; set; }
    public required GeoLocation ToLocation { get; set; }
    public required string FromAddress { get; set; }
    public required string ToAddress { get; set; }
    public required GeoLocation Location { get; set; }
    public required string Message { get; set; }
    public required RideState RideStatus { get; set; }
}
