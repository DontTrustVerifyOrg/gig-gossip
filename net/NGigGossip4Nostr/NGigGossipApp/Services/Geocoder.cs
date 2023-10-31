using System;
using Nominatim.API.Geocoders;
using Nominatim.API.Models;

namespace GigMobile.Services
{
    public class Geocoder : IGeocoder
    {
        private readonly ReverseGeocoder _reverseGeocoder;

        public Geocoder(ReverseGeocoder reverseGeocoder)
        {
            _reverseGeocoder = reverseGeocoder;
        }


        public async Task<string> GetLocationAddress(Location location)
        {
            try
            {
                var repsponse = await ReverseGeolocation(location);

                return repsponse.DisplayName;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return string.Empty;
            }
        }

        public async Task<GeocodeResponse> ReverseGeolocation(Location location)
        {
            var reverseGeocodeRequest = new ReverseGeocodeRequest
            {
                Longitude = location.Longitude,
                Latitude = location.Latitude,
                ZoomLevel = 18,

                BreakdownAddressElements = true,
                ShowExtraTags = true,
                ShowAlternativeNames = true,
                ShowGeoJSON = true
            };

            return await _reverseGeocoder.ReverseGeocode(reverseGeocodeRequest);
        }
    }
}

