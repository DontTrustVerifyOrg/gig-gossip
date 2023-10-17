using System;
using System.Windows.Input;
using GigMobile.Services;
using CryptoToolkit;

namespace GigMobile.ViewModels.Wallet
{
	public class AddWalletViewModel : BaseViewModel<bool>
    {
        private ICommand _addWalletCommand;
        public ICommand AddWalletCommand => _addWalletCommand ??= new Command(async () => await AddWalletAsync());

        public string WalletDomain { get; set; }
        public bool FromSetup { get; private set; }

        public override void Prepare(bool data)
        {
            FromSetup = data;
        }

        private async Task AddWalletAsync()
        {
#if DEBUG
            WalletDomain = GigGossipNodeConfig.GigWalletOpenApi;
#endif
            if (!string.IsNullOrEmpty(WalletDomain))
            {
                var privateKeyString = await SecureDatabase.GetPrivateKeyAsync();
                var privateKey = privateKeyString.AsECPrivKey();

                await SecureDatabase.SetSetSetupStatusAsync(SecureDatabase.SetupStatus.Finished);

                if (FromSetup)
                    await NavigationService.NavigateAsync<MainViewModel>();
                else
                    await NavigationService.NavigateBackAsync();
            }
        }
    }
}

