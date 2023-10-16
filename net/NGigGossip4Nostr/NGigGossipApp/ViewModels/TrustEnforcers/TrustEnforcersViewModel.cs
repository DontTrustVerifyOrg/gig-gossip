using System.Windows.Input;
using GigMobile.Services;

namespace GigMobile.ViewModels.TrustEnforcers
{
	public class TrustEnforcersViewModel : BaseViewModel<bool>
    {
        public string[] TrustEnforcers { get; set; }

        private ICommand _addTrEnfCommand;
        public ICommand AddTrEnfCommand => _addTrEnfCommand ??= new Command(() => { NavigationService.NavigateAsync<AddTrEnfViewModel, bool>(FromSetup, animated: true); });

        public bool FromSetup { get; private set; }

        public async override Task Initialize()
        {
            await base.Initialize();
            TrustEnforcers = await SecureDatabase.GetTrustEnforcersAsync();
        }

        public override void Prepare(bool data)
        {
            FromSetup = data;
        }
    }
}

