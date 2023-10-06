using System.Windows.Input;

namespace GigMobile.ViewModels.Wallet
{
	public class WithdrawBitcoinViewModel : BaseViewModel<decimal>
    {
        public decimal BitcoinBallance { get; set; }

        private ICommand _cancelWalletCommand;
        public ICommand CancelWalletCommand => _cancelWalletCommand ??= new Command(() => { NavigationService.NavigateBackAsync(); });

        private ICommand _scanWalletCommand;
        public ICommand ScanWalletCommand => _scanWalletCommand ??= new Command(() => { NavigationService.NavigateAsync<ScanWalletCodeViewModel>(OnCodeDetected); });

        private ICommand _sendCommand;
        public ICommand SendCommand => _sendCommand ??= new Command(async () => await SendAsync());

        public string SenderAddress { get; set; }

        public decimal Amount { get; set; }

        public override void Prepare(decimal data)
        {
            BitcoinBallance = data;
        }

        private void OnCodeDetected(object code)
        {
            SenderAddress = code.ToString();
        }

        private async Task SendAsync()
        {
            //TODO 
            /* var privateKey = await SecureDatabase.GetPrivateKeyAsync();
             * await PAWEL_API.SendMoney(privateKey, SenderAddress, Amount);*/
            await NavigationService.NavigateBackAsync();
        }
    }
}

