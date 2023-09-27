using System.Windows.Input;
using GigMobile.ViewModels.TrustEnforcers;

namespace GigMobile.ViewModels.Profile
{
	public class LoginPrKeyViewModel : BaseViewModel
    {
        private ICommand _loginCommand;
        public ICommand LoginCommand => _loginCommand ??= new Command(() => { NavigationService.NavigateAsync<AllowBiometricViewModel>(animated: true); });
    }
}

