using System.Text.Json.Serialization;
using ProtoBuf;

namespace RideShareFrames;

[ProtoContract]
public class RideTopic
{
    [ProtoMember(1)]
    public required string FromGeohash { get; set; }
    [ProtoMember(2)]
    public required string ToGeohash { get; set; }
    [ProtoMember(3)]
    public required DateTime PickupAfter { get; set; }
    [ProtoMember(4)]
    public required DateTime PickupBefore { get; set; }
    [ProtoMember(5)]
    public required double Distance { get; set; }
}

[ProtoContract]
public class ConnectionReply
{
    [ProtoMember(1)]
    public required string PublicKey { get; set; }
    [ProtoMember(2)]
    public required string[] Relays { get; set; }
    [ProtoMember(3)]
    public required string Secret { get; set; }
    [ProtoMember(4)]
    public required GeoLocation Location { get; set; }
    [ProtoMember(5)]
    public required string Message { get; set; }
}

public enum RideState
{
    Started = 0,
    DriverGoingForRider = 1,
    DriverWaitingForRider = 2,
    RiderPickedUp = 3,
    DriverGoingWithRider = 4,
    Completed = 5,
    Disputed = 6,
    Failed = 7,
    Cancelled = 8,
}

[ProtoContract]
public class LocationFrame
{
    [ProtoMember(1)]
    public required Guid SignedRequestPayloadId { get; set; }
    [ProtoMember(2)]
    public required Guid ReplierCertificateId { get; set; }
    [ProtoMember(3)]
    public required Uri SettlerServiceUri { get; set; }
    [ProtoMember(4)]
    public required string Secret { get; set; }
    [ProtoMember(5)]
    public required GeoLocation FromLocation { get; set; }
    [ProtoMember(6)]
    public required GeoLocation ToLocation { get; set; }
    [ProtoMember(7)]
    public required string FromAddress { get; set; }
    [ProtoMember(8)]
    public required string ToAddress { get; set; }
    [ProtoMember(9)]
    public required GeoLocation Location { get; set; }
    [ProtoMember(10)]
    public required string Message { get; set; }
    [ProtoMember(11)]
    public required RideState RideStatus { get; set; }
}
