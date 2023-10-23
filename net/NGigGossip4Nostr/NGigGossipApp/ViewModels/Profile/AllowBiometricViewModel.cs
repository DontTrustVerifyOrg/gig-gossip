using System;
using System.Windows.Input;
using GigMobile.Services;
using GigMobile.ViewModels.TrustEnforcers;

namespace GigMobile.ViewModels.Profile
{
    public class AllowBiometricViewModel : BaseViewModel
    {
        private ICommand _answerCommand;
        private readonly ISecureDatabase _secureDatabase;

        public ICommand AnswerCommand => _answerCommand ??= new Command<bool>(async (result) => { await ProcessAnswerAsync(result); });

        public AllowBiometricViewModel(ISecureDatabase secureDatabase)
        {
            _secureDatabase = secureDatabase;
        }

        private async Task ProcessAnswerAsync(bool useBiometric)
        {
            if (useBiometric)
                await _secureDatabase.SetUseBiometricAsync(true);

            await NavigationService.NavigateAsync<TrustEnforcersViewModel, bool>(true, animated: true);
        }
    }
}

