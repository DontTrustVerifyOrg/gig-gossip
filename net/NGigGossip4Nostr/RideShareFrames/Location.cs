using ProtoBuf;

namespace RideShareFrames;

[ProtoContract]
public class GeoLocation
{
    [ProtoMember(1)]
    public double Latitude { get; set; }
    [ProtoMember(2)]
    public double Longitude { get; set; }

    public GeoLocation()
    {
    }
    public GeoLocation(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public GeoLocation(GeoLocation point)
    {
        Latitude = point.Latitude;
        Longitude = point.Longitude;
    }

    public override string ToString()
    {
        return $"GeoLocation({Longitude},{Latitude})";
    }
}
