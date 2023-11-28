namespace RideShareCLIApp;

public class Location
{
    public double Latitude { get; set; }

    public double Longitude { get; set; }
    public Location()
    {
    }
    public Location(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public Location(Location point)
    {
        Latitude = point.Latitude;
        Longitude = point.Longitude;
    }

    public override string ToString()
    {
        return $"Geo({Longitude},{Latitude})";
    }
}
