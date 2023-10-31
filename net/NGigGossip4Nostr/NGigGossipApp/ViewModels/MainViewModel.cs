using System.Windows.Input;
using BindedMvvm.Attributes;
using GigMobile.Services;

namespace GigMobile.ViewModels
{
    [CleanHistory]
	public class MainViewModel : BaseViewModel
    {
        private readonly GigGossipNode _gigGossipNode;
        private readonly ISecureDatabase _secureDatabase;

        public MainViewModel(GigGossipNode gigGossipNode, ISecureDatabase secureDatabase)
        {
            _gigGossipNode = gigGossipNode;
            _secureDatabase = secureDatabase;
        }

        private ICommand _editTrustEnforcersCommand;
        public ICommand EditTrustEnforcersCommand => _editTrustEnforcersCommand ??= new Command(async () =>
        {
            if (!string.IsNullOrEmpty(DefaultTrustEnforcer))
                await Application.Current.MainPage.DisplayAlert("Cann't request ride", "Firstly setup at least one trust enforcer", "Cancel");
            else
            {
                IsBusy = false;
                await NavigationService.NavigateAsync<TrustEnforcers.TrustEnforcersViewModel>();
                IsBusy = true;
            }
        });

        private ICommand _editWalletDomainCommand;
        public ICommand EditWalletDomainCommand => _editWalletDomainCommand ??= new Command(() => { NavigationService.NavigateAsync<Wallet.AddWalletViewModel>(animated: true); });

        private ICommand _walletDetailsCommand;
        public ICommand WalletDetailsCommand => _walletDetailsCommand ??= new Command(() => { NavigationService.NavigateAsync<Wallet.WalletDetailsViewModel, string>(WalletAddress, animated: true); });

        private ICommand _requestRideCommand;
        public ICommand RequestRideCommand => _requestRideCommand ??= new Command(async () =>
        {
            IsBusy = true;
            await NavigationService.NavigateAsync<Ride.Customer.CreateRideViewModel>(animated: true);
            IsBusy = false;
        });

        private ICommand _driverParametersCommand;
        public ICommand DriverParametersCommand => _driverParametersCommand ??= new Command(() => { NavigationService.NavigateAsync<Ride.Driver.DriverParametersViewModel>(animated: true); });

        private ICommand _withdrawBitcoinCommand;
        public ICommand WithdrawBitcoinCommand => _withdrawBitcoinCommand ??= new Command(() => { NavigationService.NavigateAsync<Wallet.WithdrawBitcoinViewModel>(); });

        public string WalletAddress { get; private set; }
        public long BitcoinBallance { get; private set; }
        public string DefaultTrustEnforcer { get; private set; }

        public async override void OnAppearing()
        {
            IsBusy = true;
            var token = _gigGossipNode.MakeWalletAuthToken();
            WalletAddress = await _gigGossipNode.LNDWalletClient.NewAddressAsync(token);
            BitcoinBallance = await _gigGossipNode.LNDWalletClient.GetBalanceAsync(token);
            var trustEnforcers = await _secureDatabase.GetTrustEnforcersAsync();
            DefaultTrustEnforcer = trustEnforcers?.Values?.Last()?.Name;
            IsBusy = false;

            base.OnAppearing();
        }
    }
}

