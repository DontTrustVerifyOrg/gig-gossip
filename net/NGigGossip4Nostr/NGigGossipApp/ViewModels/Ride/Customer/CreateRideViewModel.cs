using System.Windows.Input;
using GigMobile.Services;

namespace GigMobile.ViewModels.Ride.Customer
{
    public class CreateRideViewModel : DriverArrivedViewModel
    {
        private Location _fromLocation;
        private Location _toLocation;

        private ICommand _requestCommand;
        public ICommand RequestCommand => _requestCommand ??= new Command(() => NavigationService.NavigateAsync<LookingDriverViewModel>());

        private ICommand _pickFromCommand;
        public ICommand PickFromCommand => _pickFromCommand ??= new Command(async() => await PickLocationAsync(true));

        private ICommand _pickToCommand;
        public ICommand PickToCommand => _pickToCommand ??= new Command(async () => await PickLocationAsync(false));

        private async Task PickLocationAsync(bool isFromLocation)
        {
            //var location = await GeolocationService.GetCachedLocation();
            await NavigationService.NavigateAsync<PickLocationViewModel>((location) => OnLocationPicked(location, true));
        }

        private void OnLocationPicked(object location, bool isFromLocation)
        {
            if (location == null)
                return;

            if (isFromLocation)
                _fromLocation = (Location)location;
            else
                _toLocation = (Location)location;
        }
    }
}

