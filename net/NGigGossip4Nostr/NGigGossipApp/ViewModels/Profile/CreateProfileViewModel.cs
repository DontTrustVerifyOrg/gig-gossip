using System.Windows.Input;
using CryptoToolkit;

namespace GigMobile.ViewModels.Profile
{
	public class CreateProfileViewModel : BaseViewModel
	{
		public string[] Mnemonic { get; private set; } = Crypto.GenerateMnemonic().Split(" ");

        private ICommand _nextCommand;
        public ICommand NextCommand => _nextCommand ??= new Command(async () =>
        {
            IsBusy = true;
            await GoNextAsync();
            IsBusy = false;
        });

        public override Task Initialize()
        {
            Mnemonic = Crypto.GenerateMnemonic().Split(" ");

            return base.Initialize();
        }

        private async Task GoNextAsync()
        {
            var privateKey = Crypto.DeriveECPrivKeyFromMnemonic(string.Join(" ", Mnemonic));
            await NavigationService.NavigateAsync<LoginPrKeyViewModel, string>(privateKey.AsHex(), animated: true);
        }
    }
}