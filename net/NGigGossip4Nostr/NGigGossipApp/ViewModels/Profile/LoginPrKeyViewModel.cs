using System.Windows.Input;
using CryptoToolkit;
using GigMobile.Services;

namespace GigMobile.ViewModels.Profile
{
	public class LoginPrKeyViewModel : BaseViewModel<string>
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IGigGossipNodeEventSource _gigGossipNodeEventSource;
        private readonly ISecureDatabase _secureDatabase;
        private ICommand _loginCommand;
        public ICommand LoginCommand => _loginCommand ??= new Command(async () => await LoginAsync());

        public string PrivateKey { get; set; }
        public bool IsKeyInStorage { get; set; }

        public LoginPrKeyViewModel(IServiceProvider serviceProvider,
            IGigGossipNodeEventSource gigGossipNodeEventSource,
            ISecureDatabase secureDatabase)
        {
            _serviceProvider = serviceProvider;
            _gigGossipNodeEventSource = gigGossipNodeEventSource;
            _secureDatabase = secureDatabase;
        }

        public override void Prepare(string data)
        {
            if (!string.IsNullOrEmpty(data))
                PrivateKey = data;
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
                        await _secureDatabase.SetPrivateKeyAsync(PrivateKey);
                        var node = _serviceProvider.GetService<GigGossipNode>();
                        try
                        {
#if DEBUG
                            await node.StartAsync(
                                GigGossipNodeConfig.NostrRelays,
                                _gigGossipNodeEventSource.GetGigGossipNodeEvents(),
                                HttpsClientHandlerService.GetPlatformMessageHandler());
#else
                            await node.StartAsync(
                                GigGossipNodeConfig.NostrRelays,
                                _gigGossipNodeEventSource.GetGigGossipNodeEvents());
#endif
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                        }

                        var usBiometric = await _secureDatabase.GetUseBiometricAsync();
                        if (!usBiometric)
                            await NavigationService.NavigateAsync<Profile.AllowBiometricViewModel>();
                        else
                        {
                            var status = await _secureDatabase.GetGetSetupStatusAsync();
                            switch (status)
                            {
                                case SetupStatus.Enforcer: await NavigationService.NavigateAsync<TrustEnforcers.AddTrEnfViewModel, bool>(true); break;
                                case SetupStatus.Wallet: await NavigationService.NavigateAsync<Wallet.AddWalletViewModel, bool>(true); break;
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

