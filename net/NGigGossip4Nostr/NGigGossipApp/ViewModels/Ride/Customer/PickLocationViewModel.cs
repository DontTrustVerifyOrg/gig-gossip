using System.Windows.Input;

namespace GigMobile.ViewModels.Ride.Customer
{
	public class PickLocationViewModel : BaseViewModel
    {
        public PickLocationViewModel()
		{
        }

        //public override void Prepare(Location data) => UserCoordinate = data;

        //public Location UserCoordinate { get; private set; }
        public Location TargetCoordinate { get; private set; }

        private ICommand _pickCommand;
        public ICommand PickCommand => _pickCommand ??= new Command(() => NavigationService.NavigateBackAsync(TargetCoordinate));

        public void OnMapCenterChanged(Location coord) => TargetCoordinate = coord;
    }
}

