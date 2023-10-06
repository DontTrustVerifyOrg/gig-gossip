using System.Windows.Input;
using CryptoToolkit;

namespace GigMobile.ViewModels.Profile
{
	public class CreateProfileViewModel : BaseViewModel
	{
		public string[] Mnemonic { get; } = Crypto.GenerateMnemonic().Split(" ");

        private ICommand _nextCommand;
        public ICommand NextCommand => _nextCommand ??= new Command(async () => { await GoNextAsync(); });

        private async Task GoNextAsync()
        {
            var privateKey = Crypto.DeriveECPrivKeyFromMnemonic(string.Join(" ", Mnemonic));
            await NavigationService.NavigateAsync<LoginPrKeyViewModel, string>(privateKey.AsHex(), animated: true);
        }
    }
}