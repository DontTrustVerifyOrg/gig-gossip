using System;
using System.Windows.Input;

namespace GigMobile.ViewModels.Wallet
{
	public class WithdrawBitcoinViewModel : BaseViewModel
    {
        public decimal BitcoinBallance { get; set; } = 0.00000025m;

        private ICommand _cancelWalletCommand;
        public ICommand CancelWalletCommand => _cancelWalletCommand ??= new Command(() => { NavigationService.NavigateBackAsync(); });

        private ICommand _scanWalletCommand;
        public ICommand ScanWalletCommand => _scanWalletCommand ??= new Command(() => { NavigationService.NavigateAsync<ScanWalletCodeViewModel>(); });
    }
}

