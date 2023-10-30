using System.Windows.Input;
using GigMobile.Models;
using GigMobile.Services;

namespace GigMobile.ViewModels.TrustEnforcers
{
	public class TrustEnforcersViewModel : BaseViewModel<bool>
    {
        private readonly ISecureDatabase _secureDatabase;

        public TrustEnforcersViewModel(ISecureDatabase secureDatabase)
        {
            _secureDatabase = secureDatabase;
        }

        public TrustEnforcer[] TrustEnforcers { get; set; }

        private ICommand _addTrEnfCommand;

        public ICommand AddTrEnfCommand => _addTrEnfCommand ??= new Command(() => { NavigationService.NavigateAsync<AddTrEnfViewModel, bool>(FromSetup, animated: true); });

        public bool FromSetup { get; private set; }

        public async override Task Initialize()
        {
            await base.Initialize();
            var enforcers = await _secureDatabase.GetTrustEnforcersAsync();
            TrustEnforcers = enforcers?.Values?.ToArray();
        }

        public override void Prepare(bool data)
        {
            FromSetup = data;
        }
    }
}

