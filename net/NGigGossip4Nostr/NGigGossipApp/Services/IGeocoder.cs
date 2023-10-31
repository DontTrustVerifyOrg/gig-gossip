using Nominatim.API.Models;

namespace GigMobile.Services
{
    public interface IGeocoder
    {
        Task<string> GetLocationAddress(Location location);
        Task<GeocodeResponse> ReverseGeolocation(Location location);
    }
}