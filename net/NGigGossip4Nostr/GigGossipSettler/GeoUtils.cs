using NGeoHash;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GigGossipSettler;

public static class GeoHashUtils
{
    //geospheric distance in kilometers
    //https://stackoverflow.com/a/51839058
    public static double HaversineDistance(double longitude, double latitude, double otherLongitude, double otherLatitude)
    {
        var d1 = latitude * (Math.PI / 180.0);
        var num1 = longitude * (Math.PI / 180.0);
        var d2 = otherLatitude * (Math.PI / 180.0);
        var num2 = otherLongitude * (Math.PI / 180.0) - num1;
        var d3 = Math.Pow(Math.Sin((d2 - d1) / 2.0), 2.0) + Math.Cos(d1) * Math.Cos(d2) * Math.Pow(Math.Sin(num2 / 2.0), 2.0);

        return 6376500.0 * (2.0 * Math.Atan2(Math.Sqrt(d3), Math.Sqrt(1.0 - d3))) / 1000.0;
    }


    public static double GeohashHaversineDistance(string geoHash1, string geoHash2)
    {
        var (lon1, lat1) = DecodeGeoHash(geoHash1);
        var (lon2, lat2) = DecodeGeoHash(geoHash2);

        return HaversineDistance(lon1, lat1, lon2, lat2);
    }

    private static (double, double) DecodeGeoHash(string geoHash)
    {
        var dec = GeoHash.Decode(geoHash);
        return (dec.Coordinates.Lon, dec.Coordinates.Lat);
    }

}
