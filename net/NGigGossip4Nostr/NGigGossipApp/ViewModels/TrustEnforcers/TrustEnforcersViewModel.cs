using System.Windows.Input;

namespace GigMobile.ViewModels.TrustEnforcers
{
	public class TrustEnforcersViewModel : BaseViewModel
    {
        public string TrustEnforcer { get; set; } = "www.stripe.com";
        public string[] TrustEnforcers { get; set; } = new string[3] { "www.trust-me.com", "www.stripe.com", "www.gig-trust.com" };

        private ICommand _addTrEnfCommand;
        public ICommand AddTrEnfCommand => _addTrEnfCommand ??= new Command(() => { NavigationService.NavigateAsync<AddTrEnfViewModel>(animated: true); });
    }
}

