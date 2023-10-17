using BindedMvvm;

namespace GigMobile;

public partial class App : Application
{
	public App(INavigationService navigationService)
	{
		InitializeComponent();

		MainPage = new NavigationPage(new LaunchPage(navigationService));
	}
}

