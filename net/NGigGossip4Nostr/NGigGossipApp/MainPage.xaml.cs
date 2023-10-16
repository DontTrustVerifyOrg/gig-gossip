using BindedMvvm;
using GigMobile.Services;
using Plugin.Fingerprint;
using Plugin.Fingerprint.Abstractions;

namespace GigMobile;

public partial class MainPage : ContentPage
{
    private readonly INavigationService _navigationService;

    public MainPage(INavigationService navigationService)
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


