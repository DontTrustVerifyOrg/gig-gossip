using System.Windows.Input;
using CryptoToolkit;
using GigMobile.Services;
using Plugin.Fingerprint;
using Plugin.Fingerprint.Abstractions;

namespace GigMobile.ViewModels.Profile
{
	public class LoginPrKeyViewModel : BaseViewModel<string>
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IGigGossipNodeEventSource _gigGossipNodeEventSource;

        private ICommand _loginCommand;
        public ICommand LoginCommand => _loginCommand ??= new Command(async () => await LoginAsync());

        private ICommand _loadKeyCommand;
        public ICommand LoadKeyCommand => _loadKeyCommand ??= new Command(async () => await LoadKeyFromStorage());

        public string PrivateKey { get; set; }
        public bool IsKeyInStorage { get; set; }

        public LoginPrKeyViewModel(IServiceProvider serviceProvider,
            IGigGossipNodeEventSource gigGossipNodeEventSource)
        {
            _serviceProvider = serviceProvider;
            _gigGossipNodeEventSource = gigGossipNodeEventSource;
        }

        public override void Prepare(string data)
        {
            if (!string.IsNullOrEmpty(data))
                PrivateKey = data;
        }

        public async override void OnAppearing()
        {
            base.OnAppearing();

            if (string.IsNullOrEmpty(PrivateKey))
                IsKeyInStorage = await SecureDatabase.GetUseBiometricAsync();
        }

        public async Task LoadKeyFromStorage()
        {
            if (IsKeyInStorage)
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
                            PrivateKey = key;
                    }
                }
            }
        }

        private async Task LoginAsync()
        {
            if (!string.IsNullOrEmpty(PrivateKey))
            {
                IsBusy = true;
                try
                {
                    var key = await PrivateKey.AsECPrivKeyAsync();
                    if (key != null)
                    {
                        await SecureDatabase.SetPrivateKeyAsync(PrivateKey);
                        var node = _serviceProvider.GetService<GigGossipNode>();
                        try
                        {
#if DEBUG
                            await node.StartAsync(_gigGossipNodeEventSource.GetGigGossipNodeEvents(), HttpsClientHandlerService.GetPlatformMessageHandler());
#else
                            await node.StartAsync(_gigGossipNodeEventSource.GetGigGossipNodeEvents());
#endif
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                        }

                        var usBiometric = await SecureDatabase.GetUseBiometricAsync();
                        if (!usBiometric)
                            await NavigationService.NavigateAsync<Profile.AllowBiometricViewModel>();
                        else
                        {
                            var status = await SecureDatabase.GetGetSetupStatusAsync();
                            switch (status)
                            {
                                case SecureDatabase.SetupStatus.Enforcer: await NavigationService.NavigateAsync<TrustEnforcers.AddTrEnfViewModel, bool>(true); break;
                                case SecureDatabase.SetupStatus.Wallet: await NavigationService.NavigateAsync<Wallet.AddWalletViewModel, bool>(true); break;
                                default:
                                    await NavigationService.NavigateAsync<MainViewModel>(); break;
                            }
                            
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }
    }
}

