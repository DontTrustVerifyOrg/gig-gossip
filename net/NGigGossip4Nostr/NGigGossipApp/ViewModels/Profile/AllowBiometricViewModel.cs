using System;
using System.Windows.Input;
using GigMobile.Services;
using GigMobile.ViewModels.TrustEnforcers;

namespace GigMobile.ViewModels.Profile
{
    public class AllowBiometricViewModel : BaseViewModel
    {
        private ICommand _answerCommand;
        public ICommand AnswerCommand => _answerCommand ??= new Command<bool>(async (result) => { await ProcessAnswerAsync(result); });

        private async Task ProcessAnswerAsync(bool useBiometric)
        {
            if (useBiometric)
                await SecureDatabase.SetUseBiometricAsync(true);

            await NavigationService.NavigateAsync<TrustEnforcersViewModel, bool>(true, animated: true);
        }
    }
}

