using BindedMvvm;
using GigMobile.Services;
using GigMobile.ViewModels.Profile;
using Plugin.Fingerprint;
using Plugin.Fingerprint.Abstractions;

namespace GigMobile;

public partial class LaunchPage : ContentPage
{
    private readonly INavigationService _navigationService;
    private readonly ISecureDatabase _secureStorage;

    public LaunchPage(INavigationService navigationService, IServiceProvider serviceProvider)
	{
		InitializeComponent();

        _navigationService = navigationService;
        _secureStorage = serviceProvider.GetService<ISecureDatabase>();
    }

    protected async override void OnAppearing()
    {
        //TODO Work Progress ViewModel
        /*await _secureStorage.GetPrivateKeyAsync();
        await _navigationService.NavigateAsync<ViewModels.Ride.Customer.ChooseDriverViewModel, Tuple<Location, Location>>(
            new Tuple<Location, Location>(new Location(-33.932878, 151.183251), new Location(-33.93332011307658, 151.1838875775517)));

        return;*/

        base.OnAppearing();

        await _secureStorage.GetPrivateKeyAsync();

        var useBiometric = false;

        if (!string.IsNullOrEmpty(_secureStorage.PrivateKey))
            useBiometric = await _secureStorage.GetUseBiometricAsync();

        if (useBiometric)
        {
            var key = _secureStorage.PrivateKey;

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
                        await _navigationService.NavigateAsync<ProfileSetupViewModel>();
                        await _navigationService.NavigateAsync<LoginPrKeyViewModel, string>(key);
                        var lgVm = _navigationService.CurrentViewModel as LoginPrKeyViewModel;
                        Dispatcher.Dispatch(() => lgVm.LoginCommand.Execute(null));
                        return;
                    }
                }
            }
        }

        await _navigationService.NavigateAsync<ProfileSetupViewModel>();
    }
}


