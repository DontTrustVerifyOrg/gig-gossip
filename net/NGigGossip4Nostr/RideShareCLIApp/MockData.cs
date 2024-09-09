using System;
using System.Net.NetworkInformation;
using RideShareFrames;

namespace RideShareCLIApp;

public static class MockData
{
    public static Dictionary<string, GeoLocation> FakeAddresses = new()
        {
            { "Bennelong Point, Sydney NSW 2000", new GeoLocation{ Latitude= -33.8275368, Longitude = 151.0820211 } },
            { "Keith Smith Ave, Mascot NSW 2020", new GeoLocation{ Latitude= -33.9343924,Longitude = 151.1843317 } },
            { "Driver Ave, Moore Park NSW 2021", new GeoLocation{ Latitude= -33.8903894,Longitude = 151.2234007 } },
            { "632 King St, Erskineville NSW 2043", new GeoLocation{ Latitude= -33.9052309,Longitude = 151.1808693 } },
            { "44 Stuart St, Manly NSW 2095", new GeoLocation{ Latitude= -33.8062998,Longitude = 151.287438 } },
        };

    public static GeoLocation RandomLocation()
    {
        var i1 = FakeAddresses.Values.ElementAt((int)Random.Shared.NextInt64(FakeAddresses.Count - 1));
        var i2 = FakeAddresses.Values.ElementAt((int)Random.Shared.NextInt64(FakeAddresses.Count - 1));
        var p = Random.Shared.NextDouble();
        return new GeoLocation { Latitude = i1.Latitude + (i2.Latitude - i1.Latitude) * p, Longitude = i1.Longitude + (i2.Longitude - i1.Longitude) * p };
    }
}

