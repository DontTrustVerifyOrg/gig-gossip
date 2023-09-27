using System.Windows.Input;

namespace GigMobile.ViewModels.Profile
{
	public class ProfileSetupViewModel : BaseViewModel
	{
		private ICommand _loginCommand;
		public ICommand LoginCommand => _loginCommand ??= new Command(() => { NavigationService.NavigateAsync<LoginPrKeyViewModel>(animated: true); });

        private ICommand _createCommand;
        public ICommand CreateCommand => _createCommand ??= new Command(() => { NavigationService.NavigateAsync<CreateProfileViewModel>(animated: true); });

        private ICommand _recoverCommand;
        public ICommand RecoverCommand => _recoverCommand ??= new Command(() => { NavigationService.NavigateAsync<RecoverProfileViewModel>(animated: true); });
    }
}

