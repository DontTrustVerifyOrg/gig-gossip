using CryptoToolkit;

namespace GigMobile.ViewModels.Profile
{
	public class CreateProfileViewModel : BaseViewModel
	{
		public string[] Mnemonic { get; set; } = Crypto.GenerateMnemonic().Split(" ");
    }
}