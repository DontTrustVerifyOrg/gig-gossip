using System;
using System.Windows.Input;
using GigMobile.ViewModels.Wallet;

namespace GigMobile.ViewModels.TrustEnforcers
{
	public class AddTrEnfViewModel : BaseViewModel
    {
        private ICommand _addTrEnfCommand;
        public ICommand AddTrEnfCommand => _addTrEnfCommand ??= new Command(() => { NavigationService.NavigateBackAsync(); });

        private ICommand _skipCommand;
        public ICommand SkipCommand => _skipCommand ??= new Command(() => { NavigationService.NavigateAsync<VerifyNumberViewModel>(); });
    }
}

