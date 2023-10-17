using System.Windows.Input;
using BindedMvvm.Attributes;

namespace GigMobile.ViewModels
{
    [CleanHistory]
	public class MainViewModel : BaseViewModel
    {
        private ICommand _editTrustEnforcersCommand;
        public ICommand EditTrustEnforcersCommand => _editTrustEnforcersCommand ??= new Command(() => { NavigationService.NavigateAsync<TrustEnforcers.TrustEnforcersViewModel>(); });

        private ICommand _editWalletDomainCommand;
        public ICommand EditWalletDomainCommand => _editWalletDomainCommand ??= new Command(() => { NavigationService.NavigateAsync<Wallet.AddWalletViewModel>(animated: true); });

        private ICommand _walletDetailsCommand;
        public ICommand WalletDetailsCommand => _walletDetailsCommand ??= new Command(() => { NavigationService.NavigateAsync<Wallet.WalletDetailsViewModel>(animated: true); });

        private ICommand _requestRideCommand;
        public ICommand RequestRideCommand => _requestRideCommand ??= new Command(() => { NavigationService.NavigateAsync<Ride.Customer.CreateRideViewModel>(animated: true); });

        private ICommand _driverParametersCommand;
        public ICommand DriverParametersCommand => _driverParametersCommand ??= new Command(() => { NavigationService.NavigateAsync<Ride.Driver.DriverParametersViewModel>(animated: true); });
    }
}

