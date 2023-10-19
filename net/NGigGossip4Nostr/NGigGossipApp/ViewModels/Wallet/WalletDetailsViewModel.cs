namespace GigMobile.ViewModels.Wallet
{
    public class WalletDetailsViewModel : BaseViewModel<string>
    {
        public string WalletAddress { get; private set; }

        public override void Prepare(string data)
        {
            WalletAddress = data;
        }
    }
}

