using System.Windows.Input;
using GigMobile.ViewModels.Wallet;

namespace GigMobile.ViewModels.TrustEnforcers
{
    public class VerifyNumberViewModel : BaseViewModel
    {
        private ICommand _submitCommand;
        public ICommand SubmitCommand => _submitCommand ??= new Command(() => { NavigationService.NavigateAsync<AddWalletViewModel>(); });
    }
}

