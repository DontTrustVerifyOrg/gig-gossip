using System.Text.Json.Serialization;

namespace RideShareCLIApp;



[Serializable]
public class RideTopic
{
    public required string FromGeohash { get; set; }
    public required string ToGeohash { get; set; }
    public required DateTime PickupAfter { get; set; }
    public required DateTime PickupBefore { get; set; }
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
    public required Guid RequestPayloadId { get; set; }
    public required string Secret { get; set; }
    public required Location Location { get; set; }
    public required string Message { get; set; }
}

[Serializable]
public enum RideState
{
    Unknown = 0,
    DriverApproaching = 1,
    RiderApproaching = 2,
    DriverWaitingForRider = 3,
    RiderWaitingForDriver = 4,
    RiderPickedUp = 5,
    RidingTogheter = 6,
    RiderDroppedOff = 7,
}

[Serializable]
public class LocationFrame
{
    public required Guid RequestPayloadId { get; set; }
    public required Location Location { get; set; }
    public required string Message { get; set; }
    public required RideState RideState { get; set; }
}