using System;
using System.Windows.Input;

namespace GigMobile.ViewModels.Ride.Driver
{
	public class DriverParametersViewModel : BaseViewModel
    {
        private ICommand _saveCommand;
        public ICommand SaveCommand => _saveCommand ??= new Command(() => NavigationService.NavigateAsync<RideNotificationViewModel>());
    }
}

