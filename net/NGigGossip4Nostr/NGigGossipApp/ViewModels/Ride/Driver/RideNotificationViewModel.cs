using System.Windows.Input;

namespace GigMobile.ViewModels.Ride.Driver
{
	public class RideNotificationViewModel : BaseViewModel
    {
        private ICommand _acceptCommand;
        public ICommand AcceptCommand => _acceptCommand ??= new Command(() => NavigationService.NavigateAsync<WaitingApprovalViewModel>());
    }
}

