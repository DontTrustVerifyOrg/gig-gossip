using BindedMvvm;
using GigMobile.ViewModels.Ride.Customer;

namespace GigMobile;

public partial class App : Application
{
	public App(INavigationService navigationService)
	{
		InitializeComponent();

		MainPage = new NavigationPage(new MainPage());

		navigationService.NavigateAsync<CreateRideViewModel>(animated: true);
	}
}

