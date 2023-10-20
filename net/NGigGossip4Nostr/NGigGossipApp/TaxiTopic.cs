using System;
using GigGossipFrames;

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
public class TaxiReply : DirectMessage
{
    public required string PublicKey;
    public required string Secret;
}
