using System;
using System.Windows.Input;
using GigMobile.Services;
using CryptoToolkit;

namespace GigMobile.ViewModels.Wallet
{
	public class WalletDetailsViewModel : BaseViewModel
	{
        public decimal BitcoinBallance { get; private set; }
        public string WalletAddress { get; private set; }

        private ICommand _withdrawBitcoinCommand;
        public ICommand WithdrawBitcoinCommand => _withdrawBitcoinCommand ??= new Command(() => { NavigationService.NavigateAsync<WithdrawBitcoinViewModel, decimal>(BitcoinBallance); });

        public override async Task Initialize()
        {
            var privateKey = await SecureDatabase.GetPrivateKeyAsync();
            //TODO
            //BitcoinBallance = PAWEL_API.GetBallance(privateKey);
            //WalletAddress = PAWEL_API.GetWalletAddress(privateKey); //For QR Code
            await base.Initialize();
        }
    }
}

