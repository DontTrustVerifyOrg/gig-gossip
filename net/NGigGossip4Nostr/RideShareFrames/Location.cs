namespace RideShareFrames;

public class GeoLocation
{
    public double Latitude { get; set; }

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
