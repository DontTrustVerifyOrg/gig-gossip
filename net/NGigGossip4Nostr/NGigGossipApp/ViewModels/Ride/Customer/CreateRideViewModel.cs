using System.Windows.Input;
using GigMobile.Services;
using Nominatim.API.Geocoders;
using Nominatim.API.Models;

namespace GigMobile.ViewModels.Ride.Customer
{
    public class CreateRideViewModel : DriverArrivedViewModel
    {
        private ReverseGeocoder _reverseGeocoder;

        private Location _fromLocation;
        private Location _toLocation;

        private ICommand _requestCommand;
        public ICommand RequestCommand => _requestCommand ??= new Command(() => NavigationService.NavigateAsync<LookingDriverViewModel>());

        private ICommand _pickFromCommand;
        public ICommand PickFromCommand => _pickFromCommand ??= new Command(async () => await PickLocationAsync(true));

        private ICommand _pickToCommand;
        public ICommand PickToCommand => _pickToCommand ??= new Command(async () => await PickLocationAsync(false));

        public string FromAddress { get; private set; }
        public string ToAddress { get; private set; }

        public CreateRideViewModel(ReverseGeocoder reverseGeocoder)
        {
            _reverseGeocoder = reverseGeocoder;
        }

        private async Task PickLocationAsync(bool isFromLocation)
        {
            //var location = await GeolocationService.GetCachedLocation();
            await NavigationService.NavigateAsync<PickLocationViewModel>((location) => OnLocationPicked(location, true));
        }

        private async Task<GeocodeResponse> ReverseGeolocation(Location location)
        {
            var reverseGeocodeRequest = new ReverseGeocodeRequest
            {
                Longitude = location.Longitude,
                Latitude = location.Latitude,

                BreakdownAddressElements = true,
                ShowExtraTags = true,
                ShowAlternativeNames = true,
                ShowGeoJSON = true
            };

            return await _reverseGeocoder.ReverseGeocode(reverseGeocodeRequest);
        }

        private async void OnLocationPicked(object location, bool isFromLocation)
        {
            if (location == null)
                return;

            if (isFromLocation)
            {
                _fromLocation = (Location)location;
                FromAddress = (await ReverseGeolocation(_fromLocation)).DisplayName;
            }
            else
            {
                _toLocation = (Location)location;
                ToAddress = (await ReverseGeolocation(_toLocation)).DisplayName;
            }
        }
    }
}

