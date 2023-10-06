using System.Windows.Input;
using GigMobile.Services;

namespace GigMobile.ViewModels.TrustEnforcers
{
	public class AddTrEnfViewModel : BaseViewModel<bool>
    {
        private ICommand _addTrEnfCommand;
        public ICommand AddTrEnfCommand => _addTrEnfCommand ??= new Command(async () => await OpenAddTrEnfAsync());

        private ICommand _skipCommand;
        public ICommand SkipCommand => _skipCommand ??= new Command(async () => await SkipAsync());

        public bool FromSetup { get; private set; }

        public string Url { get; set; }
        public string PhoneCode { get; set; }
        public string PhoneNumber { get; set; }

        public override void Prepare(bool data)
        {
            FromSetup = data;
        }

        private async Task SkipAsync()
        {
            if (FromSetup)
            {
                await SecureDatabase.SetSetSetupStatusAsync(SecureDatabase.SetupStatus.Wallet);
                await NavigationService.NavigateAsync<Wallet.AddWalletViewModel>();
            }
            else
                await NavigationService.NavigateBackAsync();
        }
        private async Task OpenAddTrEnfAsync()
        {
            if (!string.IsNullOrEmpty(Url) && !string.IsNullOrEmpty(PhoneCode) && !string.IsNullOrEmpty(PhoneNumber))
                await NavigationService.NavigateAsync<VerifyNumberViewModel, NewTrustEnforcer>(new NewTrustEnforcer { Url = Url, PhoneNumber = $"+{PhoneCode} {PhoneNumber}" }, onClosed: async (x) => await OnAddedClosed());
        }

        private async Task OnAddedClosed()
        {
            if (FromSetup)
            {
                await SecureDatabase.SetSetSetupStatusAsync(SecureDatabase.SetupStatus.Wallet);
                await NavigationService.NavigateAsync<Wallet.AddWalletViewModel>();
            }
        }
    }
}

