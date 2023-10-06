using CryptoToolkit;
using System.Windows.Input;

namespace GigMobile.ViewModels.Profile
{
	public class RecoverProfileViewModel : BaseViewModel
    {
        public string[] Mnemonic { get; set; } = new string[12];

        private ICommand _recoveryCommand;
        public ICommand RecoveryCommand => _recoveryCommand ??= new Command(async () => await RecoveryAsync());

        private async Task RecoveryAsync()
        {
            if (Mnemonic.All(x => !string.IsNullOrEmpty(x)))
            {
                try
                {
                    var key = Crypto.DeriveECPrivKeyFromMnemonic(string.Join(' ', Mnemonic));
                    if (key != null)
                    {
                        await NavigationService.NavigateBackAsync();
                        await NavigationService.NavigateAsync<ViewModels.Profile.LoginPrKeyViewModel, string>(key.AsHex(), animated: true);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }
    }
}

