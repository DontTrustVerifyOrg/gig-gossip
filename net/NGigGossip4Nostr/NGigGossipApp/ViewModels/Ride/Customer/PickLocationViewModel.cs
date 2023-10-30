using System.Windows.Input;
using Nominatim.API.Geocoders;
using Nominatim.API.Models;

namespace GigMobile.ViewModels.Ride.Customer
{
	public class PickLocationViewModel : BaseViewModel<Location>
    {
        private readonly ReverseGeocoder _reverseGeocoder;
        public Location _targetCoordinate;

        public PickLocationViewModel(ReverseGeocoder reverseGeocoder)
		{
            _reverseGeocoder = reverseGeocoder;
        }

        public Location InitCoordinate { get; private set; }

        public override void Prepare(Location data)
        {
            InitCoordinate = data;
        }

        public override async Task Initialize()
        {
            var geocodeResponse = await ReverseGeolocation(InitCoordinate);

            Address = geocodeResponse.DisplayName;
        }

        public string Address { get; private set; }

        private ICommand _pickCommand;
        public ICommand PickCommand => _pickCommand ??= new Command(() => NavigationService.NavigateBackAsync(_targetCoordinate));

        public async void PickCoordinate(Location coord)
        {
            _targetCoordinate = coord;

            var geocodeResponse = await ReverseGeolocation(coord);

            Address = geocodeResponse.DisplayName;
        }

        private async Task<GeocodeResponse> ReverseGeolocation(Location location)
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

