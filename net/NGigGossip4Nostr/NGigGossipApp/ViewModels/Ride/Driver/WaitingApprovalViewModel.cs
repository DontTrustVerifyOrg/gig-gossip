using System.Windows.Input;

namespace GigMobile.ViewModels.Ride.Driver
{
	public class WaitingApprovalViewModel : BaseViewModel
    {
        private ICommand _cancelRequestCommand;
        public ICommand CancelRequestCommand => _cancelRequestCommand ??= new Command(() => NavigationService.NavigateAsync<RideProcessingViewModel>());
    }
}

