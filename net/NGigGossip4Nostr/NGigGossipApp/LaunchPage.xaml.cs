using BindedMvvm;

namespace GigMobile;

public partial class LaunchPage : ContentPage
{
    private readonly INavigationService _navigationService;

    public LaunchPage(INavigationService navigationService)
	{
		InitializeComponent();
        _navigationService = navigationService;
    }

    protected async override void OnAppearing()
    {
        base.OnAppearing();

        await _navigationService.NavigateAsync<ViewModels.Profile.ProfileSetupViewModel>();
    }
}


