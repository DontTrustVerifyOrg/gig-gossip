using System;
namespace GigMobile.ViewModels.Ride;


[Serializable]
public class TaxiTopic
{
    public required string FromGeohash { get; set; }
    public required string ToGeohash { get; set; }
    public required DateTime PickupAfter { get; set; }
    public required DateTime DropoffBefore { get; set; }
}
