using System.Windows.Input;
using GigMobile.Services;

namespace GigMobile.ViewModels.TrustEnforcers
{
	public class TrustEnforcersViewModel : BaseViewModel
    {
        public string[] TrustEnforcers { get; set; }

        private ICommand _addTrEnfCommand;
        public ICommand AddTrEnfCommand => _addTrEnfCommand ??= new Command(() => { NavigationService.NavigateAsync<AddTrEnfViewModel>(animated: true); });

        public async override Task Initialize()
        {
            await base.Initialize();
            TrustEnforcers = await SecureDatabase.GetTrustEnforcersAsync();
        }
    }
}

