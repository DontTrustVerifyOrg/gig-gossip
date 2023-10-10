using System.Windows.Input;
using GigLNDWalletAPIClient;

namespace GigMobile.ViewModels.Wallet
{
	public class WalletDetailsViewModel : BaseViewModel
    {
        private readonly GigGossipNode _gigGossipNode;
        private readonly swaggerClient _walletClient;

        public decimal BitcoinBallance { get; private set; }
        public string WalletAddress { get; private set; }

        private ICommand _withdrawBitcoinCommand;
        public ICommand WithdrawBitcoinCommand => _withdrawBitcoinCommand ??= new Command(() => { NavigationService.NavigateAsync<WithdrawBitcoinViewModel, decimal>(BitcoinBallance); });

        public WalletDetailsViewModel(GigGossipNode gigGossipNode, GigLNDWalletAPIClient.swaggerClient walletClient)
        {
            _gigGossipNode = gigGossipNode;
            _walletClient = walletClient;
        }

        public override async Task Initialize()
        {
            await base.Initialize();

            var token = _gigGossipNode.MakeWalletAuthToken();
            BitcoinBallance = await _walletClient.GetBalanceAsync(token);
        }
    }
}

