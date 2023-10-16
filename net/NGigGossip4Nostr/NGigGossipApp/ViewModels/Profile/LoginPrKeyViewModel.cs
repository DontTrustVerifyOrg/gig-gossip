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

        public ICommand LoginCommand => _loginCommand ??= new Command(async () => await LoginAsync());

        public string PrivateKey { get; set; }

        public LoginPrKeyViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
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
                try
                {
                    var key = PrivateKey.AsECPrivKey();
                    if (key != null)
                    {
                        await SecureDatabase.SetPrivateKeyAsync(PrivateKey);
                        var node = _serviceProvider.GetService<GigGossipNode>();
                        try
                        {
#if DEBUG
                            await node.StartAsync(new GigGossipNodeEvents(), HttpsClientHandlerService.GetPlatformMessageHandler());
#else
                            await node.StartAsync(new GigGossipNodeEvents());;
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
                                case SecureDatabase.SetupStatus.Wallet: await NavigationService.NavigateAsync<Wallet.AddWalletViewModel>(); break;
                                default:
                                    await NavigationService.NavigateAsync<Wallet.WalletDetailsViewModel>(); break;
                            }
                            
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }
    }
}

