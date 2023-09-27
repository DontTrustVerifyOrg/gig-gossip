using System;
namespace GigMobile.ViewModels.Profile
{
	public class RecoverProfileViewModel : BaseViewModel
    {
        public string[] Mnemonic { get; set; } = new string[12];
    }
}

