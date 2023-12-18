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
}

[Serializable]
public class AckFrame
{
    public required string Secret { get; set; }
    public required DetailedParameters Parameters { get; set; }
}

[Serializable]
public class DetailedParameters
{
    public required Guid SignedRequestPayloadId { get; set; }
    public GeoLocation FromLocation { get; set; }
    public GeoLocation ToLocation { get; set; }
    public string FromAddress { get; set; }
    public string ToAddress { get; set; }
}

[Serializable]
public enum RideState
{
    Started = 0,
    DriverWaitingForRider = 1,
    RiderPickedUp = 2,
    RiderDroppedOff = 3,
}

[Serializable]
public class LocationFrame
{
    public required Guid SignedRequestPayloadId { get; set; }
    public required GeoLocation Location { get; set; }
    public required float Direction { get; set; }
    public required string Message { get; set; }
    public required RideState RideStatus { get; set; }
}