using System.Windows.Input;

namespace GigMobile.ViewModels.Wallet
{
	public class WithdrawBitcoinViewModel : BaseViewModel
    {
        private readonly GigGossipNode _gigGossipNode;

        public decimal BitcoinBallance { get; set; }

        private ICommand _cancelWalletCommand;
        public ICommand CancelWalletCommand => _cancelWalletCommand ??= new Command(() => { NavigationService.NavigateBackAsync(); });

        private ICommand _scanWalletCommand;
        public ICommand ScanWalletCommand => _scanWalletCommand ??= new Command(() => { NavigationService.NavigateAsync<ScanWalletCodeViewModel>(OnCodeDetected); });

        private ICommand _sendCommand;
        public ICommand SendCommand => _sendCommand ??= new Command(async () => await SendAsync());

        public string OnchainAddress { get; set; }

        public long Amount { get; set; }

        public override async Task Initialize()
        {
            await base.Initialize();

            var token = _gigGossipNode.MakeWalletAuthToken();
            BitcoinBallance = await _gigGossipNode.LNDWalletClient.GetBalanceAsync(token);
        }

        private void OnCodeDetected(object code)
        {
            OnchainAddress = code.ToString();
        }

        public WithdrawBitcoinViewModel(GigGossipNode gigGossipNode)
        {
            _gigGossipNode = gigGossipNode;
        }

        private async Task SendAsync()
        {
            var token = _gigGossipNode.MakeWalletAuthToken();
            var payoutId = await _gigGossipNode.LNDWalletClient.RegisterPayoutAsync(token, Amount, OnchainAddress, 100);
            await NavigationService.NavigateBackAsync();
        }
    }
}

