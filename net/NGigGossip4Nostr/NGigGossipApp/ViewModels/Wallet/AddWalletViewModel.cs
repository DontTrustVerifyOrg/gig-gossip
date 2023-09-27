using System;
using System.Windows.Input;

namespace GigMobile.ViewModels.Wallet
{
	public class AddWalletViewModel : BaseViewModel
    {
        private ICommand _addWalletCommand;
        public ICommand AddWalletCommand => _addWalletCommand ??= new Command(() => { NavigationService.NavigateAsync<WalletDetailsViewModel>(); });
    }
}

