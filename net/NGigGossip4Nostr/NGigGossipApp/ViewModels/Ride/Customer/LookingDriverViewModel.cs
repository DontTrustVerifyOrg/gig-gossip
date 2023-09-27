using System;
using System.Windows.Input;

namespace GigMobile.ViewModels.Ride.Customer
{
	public class LookingDriverViewModel : BaseViewModel
    {
        private ICommand _cancelRequestCommand;
        public ICommand CancelRequestCommand => _cancelRequestCommand ??= new Command(() => NavigationService.NavigateAsync<ChooseDriverViewModel>());
    }
}

