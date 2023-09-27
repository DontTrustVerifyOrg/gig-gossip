using System;
using System.Windows.Input;

namespace GigMobile.ViewModels.Wallet
{
	public class WalletDetailsViewModel : BaseViewModel
	{
		public decimal BitcoinBallance { get; set; } = 0.00000025m;
        public string WalletAddress { get; set; } = "bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh";

        private ICommand _withdrawBitcoinCommand;
        public ICommand WithdrawBitcoinCommand => _withdrawBitcoinCommand ??= new Command(() => { NavigationService.NavigateAsync<WithdrawBitcoinViewModel>(); });
    }
}

