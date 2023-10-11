using System.Windows.Input;
using GigLNDWalletAPIClient;

namespace GigMobile.ViewModels.Wallet
{
    public class WalletDetailsViewModel : BaseViewModel
    {
        private readonly GigGossipNode _gigGossipNode;

        public long BitcoinBallance { get; private set; }
        public string WalletAddress { get; private set; }

        private ICommand _withdrawBitcoinCommand;
        public ICommand WithdrawBitcoinCommand => _withdrawBitcoinCommand ??= new Command(() => { NavigationService.NavigateAsync<WithdrawBitcoinViewModel>(); });

        public WalletDetailsViewModel(GigGossipNode gigGossipNode)
        {
            _gigGossipNode = gigGossipNode;
        }

        public override async Task Initialize()
        {
            await base.Initialize();
            var token = _gigGossipNode.MakeWalletAuthToken();
            BitcoinBallance = await _gigGossipNode.LNDWalletClient.GetBalanceAsync(token);
            WalletAddress = await _gigGossipNode.LNDWalletClient.NewAddressAsync(token);
        }
    }
}

