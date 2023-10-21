using System;

namespace GigMobile;


[Serializable]
public class TaxiTopic
{
    public required string FromGeohash { get; set; }
    public required string ToGeohash { get; set; }
    public required DateTime PickupAfter { get; set; }
    public required DateTime DropoffBefore { get; set; }
}

[Serializable]
public class TaxiReply
{
    public required string PublicKey { get; set; }
    public required string[] Relays { get; set; }
    public required string Secret { get; set; }
}

[Serializable]
public class TaxiAckFrame
{
    public required string Secret { get; set; }
}

[Serializable]
public class TaxiLocationFrame
{
    public required Location Location { get; set; }
    public required string Message { get; set; }
}