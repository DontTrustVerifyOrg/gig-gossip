using System.Windows.Input;

namespace GigMobile.ViewModels.Wallet
{
    public class WalletDetailsViewModel : BaseViewModel<string>
    {
        public string WalletAddress { get; private set; }

        public override void Prepare(string data)
        {
            WalletAddress = data;
        }

        private ICommand _copyAddressCommand;
        public ICommand CopyAddressCommand => _copyAddressCommand ??= new Command(async () => await Clipboard.SetTextAsync(WalletAddress));
    }
}