using System.Windows.Input;
using GigMobile.Services;
using Osrm.Client;

namespace GigMobile.ViewModels.Ride.Customer
{
    public class CreateRideViewModel : BaseViewModel
    {
        private readonly Osrm5x _osrm5X;
        private readonly IGeocoder _geocoder;
        private readonly IAddressSearcher _addressSearcher;

        private ICommand _requestCommand;
        public ICommand RequestCommand => _requestCommand ??= new Command(async () =>
        {
            IsBusy = true;
            if (FromLocation != null && ToLocation != null)
                await NavigationService.NavigateAsync<ChooseDriverViewModel, Tuple<Location, Location>>(new Tuple<Location, Location>(FromLocation, ToLocation));
            IsBusy = false;
        });

        private ICommand _pickFromCommand;
        public ICommand PickFromCommand => _pickFromCommand ??= new Command(async () => await PickLocationAsync(true));

        private ICommand _pickToCommand;
        public ICommand PickToCommand => _pickToCommand ??= new Command(async () => await PickLocationAsync(false));

        private ICommand _pickFromSelectedCommand;
        public ICommand PickFromSelectedCommand => _pickFromSelectedCommand ??= new Command<KeyValuePair<string, object>>((value) => SelectAddress(value, true));
        private ICommand _pickToSelectedCommand;
        public ICommand PickToSelectedCommand => _pickToSelectedCommand ??= new Command<KeyValuePair<string, object>>((value) => SelectAddress(value, false));

        public string FromAddress { get; set; }
        public string ToAddress { get; set; }

        public Location FromLocation { get; set; }
        public Location ToLocation { get; set; }

        public CreateRideViewModel(IAddressSearcher addressSearcher, Osrm5x osrm5X, IGeocoder geocoder)
        {
            _osrm5X = osrm5X;
            _geocoder = geocoder;
            _addressSearcher = addressSearcher;
        }

        public override async Task Initialize()
        {
            var location = await Geolocation.GetLastKnownLocationAsync();
            var currentAddress = await _geocoder.ReverseGeolocation(location);
            FromLocation = location;
            FromAddress = currentAddress.DisplayName;

            SearchAddressFunc = async (string query, CancellationToken cancellationToken) =>
            {
                var result = await _addressSearcher.GetAddressAsync(query, currentAddress.Address.City, currentAddress.Address.Country, cancellationToken);
                if (result != null)
                    return result.Select(x => KeyValuePair.Create(x.ToString(), (object)x)).ToArray();
                return Array.Empty<KeyValuePair<string, object>>();
            };

            await base.Initialize();
        }

        private async Task PickLocationAsync(bool isFromLocation)
        {
            await NavigationService.NavigateAsync<PickLocationViewModel, Location>(isFromLocation ? FromLocation : (ToLocation ?? FromLocation),
                (location) => OnLocationPicked(location, isFromLocation));
        }

        private void SelectAddress(KeyValuePair<string, object> value, bool isFromLocation)
        {
            var place = value.Value as Place;
            if (isFromLocation)
            {
                FromAddress = value.Key;
                FromLocation = new Location(double.Parse(place.Lat), double.Parse(place.Lon));
            }
            else
            {
                ToAddress = value.Key;
                ToLocation = new Location(double.Parse(place.Lat), double.Parse(place.Lon));
            }
        }

        public Func<string, CancellationToken, Task<KeyValuePair<string, object>[]>> SearchAddressFunc { get; set; }

        public async Task<IEnumerable<Location>> GetRouteAsync()
        {
            if (FromLocation != null && ToLocation != null)
            {
                try
                {
                    var result = await _osrm5X.Route(new Osrm.Client.Models.Requests.RouteRequest
                    {
                        Coordinates = new Osrm.Client.Models.Location[]
                        {
                            new Osrm.Client.Models.Location(FromLocation.Latitude, FromLocation.Longitude),
                            new Osrm.Client.Models.Location(ToLocation.Latitude, ToLocation.Longitude)
                        },
                        SendCoordinatesAsPolyline = true
                    });
                    return result.Routes[0].Geometry.Select(x => new Location(x.Latitude, x.Longitude));
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }

            return null;
        }


        private async void OnLocationPicked(object location, bool isFromLocation)
        {
            if (location == null)
                return;

            if (isFromLocation)
            {
                FromLocation = (Location)location;
                FromAddress = await _geocoder.GetLocationAddress(FromLocation);
            }
            else
            {
                ToLocation = (Location)location;
                ToAddress = await _geocoder.GetLocationAddress(ToLocation);
            }
        }
    }
}

