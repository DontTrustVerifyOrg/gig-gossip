using System;
using System.Windows.Input;
using GigMobile.Services;
using CryptoToolkit;

namespace GigMobile.ViewModels.Wallet
{
	public class AddWalletViewModel : BaseViewModel
    {
        private ICommand _addWalletCommand;
        public ICommand AddWalletCommand => _addWalletCommand ??= new Command(async () => await AddWalletAsync());

        public string WalletDomain { get; set; }

        private async Task AddWalletAsync()
        {
            if (!string.IsNullOrEmpty(WalletDomain))
            {
                var privateKeyString = await SecureDatabase.GetPrivateKeyAsync();
                var privateKey = privateKeyString.AsECPrivKey();
                //TODO
                /*
                 * var wallet = PAWEL_API.CreateWallet(privateKey);
                 * await SecureDatabase.SetSetSetupStatusAsync(SecureDatabase.SetupStatus.Finished);
                 */
                await NavigationService.NavigateAsync<WalletDetailsViewModel>();
            }
        }
    }
}

