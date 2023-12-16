// See https://aka.ms/new-console-template for more information


using Osrm.Client;
using Osrm.Client.Models;
using Osrm.Client.Models.Requests;
using Nominatim.API.Geocoders;
using Nominatim.API.Models;
using Nominatim.API.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Nominatim.API.Web;
using Nominatim.API.Address;

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


var serviceCollection = new ServiceCollection();
serviceCollection.AddScoped<INominatimWebInterface, NominatimWebInterface>();
serviceCollection.AddScoped<ForwardGeocoder>();
serviceCollection.AddScoped<ReverseGeocoder>();
serviceCollection.AddScoped<QuerySearcher>();
serviceCollection.AddHttpClient();
var _serviceProvider = serviceCollection.BuildServiceProvider();

var reverseGeocoder = _serviceProvider.GetService<ReverseGeocoder>();


var reverseGeocodeRequest = new ReverseGeocodeRequest
{
    Longitude = -77.0365298,
    Latitude = 38.8976763,

    BreakdownAddressElements = true,
    ShowExtraTags = true,
    ShowAlternativeNames = true,
    ShowGeoJSON = true
};

var r=await reverseGeocoder.ReverseGeocode(reverseGeocodeRequest);
Console.WriteLine(r.DisplayName);


var querySearcher = _serviceProvider.GetService<QuerySearcher>();
var r3 = await querySearcher.Search(new SearchQueryRequest
{
    queryString = "Bennelong Point, Sydney NSW 2000",
    CountryCodeSearch = "AU",
    BreakdownAddressElements = true,
    ShowAlternativeNames = true,
    ShowExtraTags = true
});

Console.WriteLine(r3[0].DisplayName);


string[] addresses = new string[]
{
    "Bennelong Point, Sydney NSW 2000",
    "Keith Smith Ave, Mascot NSW 2020",
    "Driver Ave, Moore Park NSW 2021",
    "632 King St, Erskineville NSW 2043",
    "44 Stuart St, Manly NSW 2095",
};

foreach(var addr in addresses)
{
    var rx = await querySearcher.Search(new SearchQueryRequest
    {
        queryString = addr,
        CountryCodeSearch = "AU",
        BreakdownAddressElements = true,
        ShowAlternativeNames = true,
        ShowExtraTags = true
    });
    Console.WriteLine(addr);
    Console.WriteLine(rx[0].Latitude);
    Console.WriteLine(rx[0].Longitude);
    Console.WriteLine();
}