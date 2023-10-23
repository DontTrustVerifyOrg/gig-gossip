using System.Windows.Input;
using Nominatim.API.Geocoders;
using Nominatim.API.Models;
using Osrm.Client;

namespace GigMobile.ViewModels.Ride.Customer
{
    public class CreateRideViewModel : BaseViewModel<Location>
    {
        private readonly ReverseGeocoder _reverseGeocoder;
        private readonly Osrm5x _osrm5X;

        private ICommand _requestCommand;
        public ICommand RequestCommand => _requestCommand ??= new Command(() => NavigationService.NavigateAsync<LookingDriverViewModel>());

        private ICommand _pickFromCommand;
        public ICommand PickFromCommand => _pickFromCommand ??= new Command(async () => await PickLocationAsync(true));

        private ICommand _pickToCommand;
        public ICommand PickToCommand => _pickToCommand ??= new Command(async () => await PickLocationAsync(false));

        public string FromAddress { get; set; }
        public string ToAddress { get; set; }

        public Location FromLocation { get; set; }
        public Location ToLocation { get; set; }

        public Location InitUserCoordinate { get; private set; }

        public CreateRideViewModel(ReverseGeocoder reverseGeocoder, Osrm5x osrm5X)
        {
            _reverseGeocoder = reverseGeocoder;
            _osrm5X = osrm5X;
        }

        public override void Prepare(Location data)
        {
            InitUserCoordinate = data;
        }

        private async Task PickLocationAsync(bool isFromLocation)
        {
            await NavigationService.NavigateAsync<PickLocationViewModel>((location) => OnLocationPicked(location, isFromLocation));
        }

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
                        SendCoordinatesAsPolyline = true,
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

        private async void OnLocationPicked(object location, bool isFromLocation)
        {
            if (location == null)
                return;

            if (isFromLocation)
            {
                FromLocation = (Location)location;
                FromAddress = await GetLocationAddress(FromLocation);
            }
            else
            {
                ToLocation = (Location)location;
                ToAddress = await GetLocationAddress(ToLocation);
            }
        }

        private async Task<string> GetLocationAddress(Location location)
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
    }
}

