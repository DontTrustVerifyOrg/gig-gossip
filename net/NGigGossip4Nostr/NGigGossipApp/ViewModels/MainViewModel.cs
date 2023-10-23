using System.Windows.Input;
using BindedMvvm.Attributes;
using GigMobile.Services;

namespace GigMobile.ViewModels
{
    [CleanHistory]
	public class MainViewModel : BaseViewModel
    {
        private readonly GigGossipNode _gigGossipNode;

        public MainViewModel(GigGossipNode gigGossipNode)
        {
            _gigGossipNode = gigGossipNode;
        }

        private ICommand _editTrustEnforcersCommand;
        public ICommand EditTrustEnforcersCommand => _editTrustEnforcersCommand ??= new Command(() => { NavigationService.NavigateAsync<TrustEnforcers.TrustEnforcersViewModel>(); });

        private ICommand _editWalletDomainCommand;
        public ICommand EditWalletDomainCommand => _editWalletDomainCommand ??= new Command(() => { NavigationService.NavigateAsync<Wallet.AddWalletViewModel>(animated: true); });

        private ICommand _walletDetailsCommand;
        public ICommand WalletDetailsCommand => _walletDetailsCommand ??= new Command(() => { NavigationService.NavigateAsync<Wallet.WalletDetailsViewModel, string>(WalletAddress, animated: true); });

        private ICommand _requestRideCommand;
        public ICommand RequestRideCommand => _requestRideCommand ??= new Command(async () =>
        {
            var location = await GeolocationService.GetCachedLocation();
            await NavigationService.NavigateAsync<Ride.Customer.CreateRideViewModel, Location>(location, animated: true);
        });

        private ICommand _driverParametersCommand;
        public ICommand DriverParametersCommand => _driverParametersCommand ??= new Command(() => { NavigationService.NavigateAsync<Ride.Driver.DriverParametersViewModel>(animated: true); });

        private ICommand _withdrawBitcoinCommand;
        public ICommand WithdrawBitcoinCommand => _withdrawBitcoinCommand ??= new Command(() => { NavigationService.NavigateAsync<Wallet.WithdrawBitcoinViewModel>(); });

        public string WalletAddress { get; private set; }
        public long BitcoinBallance { get; private set; }

        public override async Task Initialize()
        {
            await base.Initialize();
            var token = _gigGossipNode.MakeWalletAuthToken();
            WalletAddress = await _gigGossipNode.LNDWalletClient.NewAddressAsync(token);
            BitcoinBallance = await _gigGossipNode.LNDWalletClient.GetBalanceAsync(token);
        }
    }
}

