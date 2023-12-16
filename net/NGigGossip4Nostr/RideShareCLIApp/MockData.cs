using System;
using System.Net.NetworkInformation;
using RideShareFrames;

namespace RideShareCLIApp;

public static class MockData
{
    public static Dictionary<string, GeoLocation> FakeAddresses = new()
        {
            { "Bennelong Point, Sydney NSW 2000", new GeoLocation(-33.8275368,151.0820211) },
            { "Keith Smith Ave, Mascot NSW 2020", new GeoLocation(-33.9343924,151.1843317) },
            { "Driver Ave, Moore Park NSW 2021", new GeoLocation(-33.8903894,151.2234007) },
            { "632 King St, Erskineville NSW 2043", new GeoLocation(-33.9052309,151.1808693) },
            { "44 Stuart St, Manly NSW 2095", new GeoLocation(-33.8062998,151.287438) },
        };
}

