using System.Windows.Input;

namespace GigMobile.ViewModels.Ride.Customer
{
	public class ConfirmDriverViewModel : BaseViewModel
    {
        private ICommand _confirmCommand;
        public ICommand ConfirmCommand => _confirmCommand ??= new Command(() => NavigationService.NavigateAsync<RateDriverViewModel>());
    }
}

