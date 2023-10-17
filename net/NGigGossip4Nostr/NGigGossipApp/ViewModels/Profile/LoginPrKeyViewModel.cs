using System.Windows.Input;
using CryptoToolkit;
using GigMobile.Services;
using Plugin.Fingerprint;
using Plugin.Fingerprint.Abstractions;

namespace GigMobile.ViewModels.Profile
{
	public class LoginPrKeyViewModel : BaseViewModel<string>
    {
        private ICommand _loginCommand;
        private readonly IServiceProvider _serviceProvider;
        private readonly IGigGossipNodeEvents _gigGossipNodeEvents;

        public ICommand LoginCommand => _loginCommand ??= new Command(async () => await LoginAsync());

        public string PrivateKey { get; set; }

        public LoginPrKeyViewModel(IServiceProvider serviceProvider,
            IGigGossipNodeEvents gigGossipNodeEvents)
        {
            _serviceProvider = serviceProvider;
            _gigGossipNodeEvents = gigGossipNodeEvents;
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
            {
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
                                PrivateKey = key;
                                return;
                            }
                        }
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
                            await node.StartAsync(_gigGossipNodeEvents, HttpsClientHandlerService.GetPlatformMessageHandler());
#else
                            await node.StartAsync(_gigGossipNodeEvents);;
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

