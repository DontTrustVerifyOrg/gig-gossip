using System;
using System.Windows.Input;
using GigMobile.ViewModels.TrustEnforcers;

namespace GigMobile.ViewModels.Profile
{
	public class AllowBiometricViewModel : BaseViewModel
    {
        private ICommand _answerCommand;
        public ICommand AnswerCommand => _answerCommand ??= new Command<bool>((result) => { NavigationService.NavigateAsync<TrustEnforcersViewModel>(animated: true); });
    }
}

