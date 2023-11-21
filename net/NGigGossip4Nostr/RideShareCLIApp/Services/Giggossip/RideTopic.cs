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
    public required string Secret { get; set; }
}

[Serializable]
public class LocationFrame
{
    public required Location Location { get; set; }
    public required string Message { get; set; }
}