using System.Windows.Input;
using Nominatim.API.Geocoders;

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

        public override void Prepare(Location data) => InitCoordinate = data;

        public string Address { get; private set; }

        private ICommand _pickCommand;
        public ICommand PickCommand => _pickCommand ??= new Command(() => NavigationService.NavigateBackAsync(_targetCoordinate));

        public async void PickCoordinate(Location coord)
        {
            _targetCoordinate = coord;

            var geocodeResponse = await _reverseGeocoder.ReverseGeocode(new Nominatim.API.Models.ReverseGeocodeRequest
            {
                Latitude = _targetCoordinate.Latitude,
                Longitude = _targetCoordinate.Longitude
            });

            Address = geocodeResponse.DisplayName;
        }
    }
}

