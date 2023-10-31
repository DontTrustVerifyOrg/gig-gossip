using System.Collections.ObjectModel;
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

        public ObservableCollection<TrustEnforcer> TrustEnforcers { get; set; }

        private ICommand _addTrEnfCommand;
        public ICommand AddTrEnfCommand => _addTrEnfCommand ??= new Command(() => { NavigationService.NavigateAsync<AddTrEnfViewModel, bool>(FromSetup, animated: true); });

        private ICommand _deleteTrEnfCommand;
        public ICommand DeleteTrEnfCommand => _deleteTrEnfCommand ??= new Command<TrustEnforcer>(async (TrustEnforcer tr) =>
        {
            IsBusy = true;
            await _secureDatabase.DeleteTrustEnforcersAsync(tr.Uri);
            var enforcers = await _secureDatabase.GetTrustEnforcersAsync();
            if (enforcers?.Values != null)
                TrustEnforcers = new ObservableCollection<TrustEnforcer>(enforcers.Values);
            IsBusy = false;
        });

        public bool FromSetup { get; private set; }

        public async override void OnAppearing()
        {
            var enforcers = await _secureDatabase.GetTrustEnforcersAsync();
            if (enforcers?.Values != null)
                TrustEnforcers = new ObservableCollection<TrustEnforcer>(enforcers.Values);
        }

        public override void Prepare(bool data)
        {
            FromSetup = data;
        }
    }
}

