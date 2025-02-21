using System;
using System.Net.NetworkInformation;
using GigGossip;

namespace RideShareCLIApp;

public static class MockData
{
    public static Dictionary<string, Dictionary<string, GeoLocation>> FakeAddresses = new()
        {
        {"AU", new (){
                { "Bennelong Point, Sydney NSW 2000", new GeoLocation{ Latitude= -33.8275368, Longitude = 151.0820211 } },
                { "Keith Smith Ave, Mascot NSW 2020", new GeoLocation{ Latitude= -33.9343924,Longitude = 151.1843317 } },
                { "Driver Ave, Moore Park NSW 2021", new GeoLocation{ Latitude= -33.8903894,Longitude = 151.2234007 } },
                { "632 King St, Erskineville NSW 2043", new GeoLocation{ Latitude= -33.9052309,Longitude = 151.1808693 } },
                { "44 Stuart St, Manly NSW 2095", new GeoLocation{ Latitude= -33.8062998,Longitude = 151.287438 } }
            } },
        {"PL", new (){
                { "Al. Ujazdowskie 4, 00-478 Warszawa", new GeoLocation{ Latitude=52.2175317,Longitude =21.0266559 } },
                { "Zabraniecka 61, 03-787 Warszawa", new GeoLocation{ Latitude=52.2617225,Longitude = 21.0622232 } },
                { "Komitetu Obrony Robotników 39, 02-148 Warszawa", new GeoLocation{ Latitude=52.1798317, Longitude =20.9685254 } },
                { "Włościańska 52, 01-710 Warszawa", new GeoLocation{ Latitude=52.269780, Longitude =20.973313 } },
                { "Kawcza 36/2, 04-167 Warszawa", new GeoLocation{ Latitude=52.2383138,Longitude =21.1041696 } }
            } }
        };

    public static GeoLocation RandomLocation(string country)
    {
        if (!FakeAddresses.ContainsKey(country))
        {
            return null;
        }
        var i1 = FakeAddresses[country].Values.ElementAt((int)Random.Shared.NextInt64(FakeAddresses.Count - 1));
        var i2 = FakeAddresses[country].Values.ElementAt((int)Random.Shared.NextInt64(FakeAddresses.Count - 1));
        var p = Random.Shared.NextDouble();
        return new GeoLocation { Latitude = i1.Latitude + (i2.Latitude - i1.Latitude) * p, Longitude = i1.Longitude + (i2.Longitude - i1.Longitude) * p };
    }

    public static string[] Countries => FakeAddresses.Keys.ToArray();
}

