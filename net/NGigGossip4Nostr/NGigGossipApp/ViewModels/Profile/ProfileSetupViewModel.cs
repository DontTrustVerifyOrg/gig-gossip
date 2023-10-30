using System.Windows.Input;

namespace GigMobile.ViewModels.Profile
{
    [BindedMvvm.Attributes.CleanHistory]
	public class ProfileSetupViewModel : BaseViewModel
	{
		private ICommand _loginCommand;
		public ICommand LoginCommand => _loginCommand ??= new Command(async () =>
        {
            IsBusy = true;
            await NavigationService.NavigateAsync<LoginPrKeyViewModel, string>(null, animated: true);
            IsBusy = false;
        });

        private ICommand _createCommand;
        public ICommand CreateCommand => _createCommand ??= new Command(async () =>
        {
            IsBusy = true;
            await NavigationService.NavigateAsync<CreateProfileViewModel>(animated: true);
            IsBusy = false;
        });

        private ICommand _recoverCommand;
        public ICommand RecoverCommand => _recoverCommand ??= new Command(async () =>
        {
            IsBusy = true;
            await NavigationService.NavigateAsync<RecoverProfileViewModel>(animated: true);
            IsBusy = false;
        });
    }
}

