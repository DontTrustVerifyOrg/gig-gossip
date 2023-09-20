// See https://aka.ms/new-console-template for more information


using Osrm.Client;
using Osrm.Client.Models;
using Osrm.Client.Models.Requests;

using HttpClient client = new HttpClient();
var osrm5x = new Osrm5x(client, "http://router.project-osrm.org/");
var locations = new Location[] {
    new Location(52.503033, 13.420526),
    new Location(52.516582, 13.429290),
};

var result = await osrm5x.Route(new RouteRequest()
{
    Coordinates = locations,
    SendCoordinatesAsPolyline = true
});

foreach(var pt in result.Routes[0].Geometry)
    Console.WriteLine($"{pt.Latitude}, {pt.Longitude}");
