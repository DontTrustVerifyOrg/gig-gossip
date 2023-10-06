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

        var useBiometric = await SecureDatabase.GetUseBiometricAsync();
        if (useBiometric)
        {
            var key = await SecureDatabase.GetPrivateKeyAsync();

            if (key != null)
            {
                var isAvailable = await CrossFingerprint.Current.IsAvailableAsync(allowAlternativeAuthentication: true);

                if (isAvailable)
                {
                    var request = new AuthenticationRequestConfiguration("Login using biometrics", "Confirm login with your biometrics")
                    {
                        FallbackTitle = "Use PIN",
                        AllowAlternativeAuthentication = true,
                    };

                    var result = await CrossFingerprint.Current.AuthenticateAsync(request);

                    if (result.Authenticated)
                    {
                        await _navigationService.NavigateAsync<ViewModels.Profile.ProfileSetupViewModel>();
                        await _navigationService.NavigateAsync<ViewModels.Profile.LoginPrKeyViewModel, string>(key, animated: true);
                        return;
                    }
                }
            }
        }
        await _navigationService.NavigateAsync<ViewModels.Profile.ProfileSetupViewModel>(animated: true);
    }
}


