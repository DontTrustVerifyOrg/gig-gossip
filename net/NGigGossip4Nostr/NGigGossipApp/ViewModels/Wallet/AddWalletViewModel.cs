using System.Windows.Input;
using GigMobile.Services;

namespace GigMobile.ViewModels.Wallet
{
    public class AddWalletViewModel : BaseViewModel<bool>
    {
        private ICommand _addWalletCommand;
        private readonly ISecureDatabase _secureDatabase;

        public ICommand AddWalletCommand => _addWalletCommand ??= new Command(async () => await AddWalletAsync());

        public string WalletDomain { get; set; }
        public bool FromSetup { get; private set; }

        public AddWalletViewModel(ISecureDatabase secureDatabase)
        {
            _secureDatabase = secureDatabase;
        }

        public async override Task Initialize()
        {
            await base.Initialize();

            WalletDomain = await _secureDatabase.GetWalletDomain();
        }

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
                await _secureDatabase.SetWalletDomain(WalletDomain);

                if (FromSetup)

                    await _secureDatabase.SetSetSetupStatusAsync(SetupStatus.Finished);
                await NavigationService.NavigateAsync<MainViewModel>();
            }
            else
                await NavigationService.NavigateBackAsync();
        }
    }
}
