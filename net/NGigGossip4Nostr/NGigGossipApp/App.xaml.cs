using BindedMvvm;
using GigMobile.Services;

namespace GigMobile;

public partial class App : Application
{
	public App(INavigationService navigationService, IServiceProvider serviceProvider)
	{
		InitializeComponent();

		MainPage = new NavigationPage(new LaunchPage(navigationService, serviceProvider));
	}
}

