using System;
using System.Windows.Input;
using GigMobile.ViewModels.Ride.Driver;

namespace GigMobile.ViewModels.Ride.Customer
{
	public class RateDriverViewModel : BaseViewModel
    {
        private ICommand _submitCommand;
        public ICommand SubmitCommand => _submitCommand ??= new Command(() => NavigationService.NavigateAsync<DriverParametersViewModel>());
    }
}

