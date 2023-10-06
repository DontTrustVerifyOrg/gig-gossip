using System.Windows.Input;
using CryptoToolkit;
using GigMobile.Services;

namespace GigMobile.ViewModels.Profile
{
	public class LoginPrKeyViewModel : BaseViewModel<string>
    {
        private ICommand _loginCommand;
        public ICommand LoginCommand => _loginCommand ??= new Command(async () => await LoginAsync());

        public string PrivateKey { get; set; }

        public override void Prepare(string data)
        {
            if (!string.IsNullOrEmpty(data))
                PrivateKey = data;
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
                                    await NavigationService.NavigateAsync<Ride.Customer.CreateRideViewModel>(); break;
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

