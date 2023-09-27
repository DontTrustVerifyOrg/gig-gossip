using System.Windows.Input;

namespace GigMobile.ViewModels.Ride.Customer
{
	public class ChooseDriverViewModel : BaseViewModel
    {
        private ICommand _cancelRequestCommand;
        public ICommand CancelRequestCommand => _cancelRequestCommand ??= new Command(() => NavigationService.NavigateAsync<ConfirmDriverViewModel>());

        public DriverProposal[] DriversProposals { get; set; } = new DriverProposal[4] {
            new DriverProposal { DriverName = "Driver A", TrustEnforcerName = "Trust Enforcer 1", Price = 0.00005m, Time = 5 },
            new DriverProposal { DriverName = "Driver B", TrustEnforcerName = "Trust Enforcer 2", Price = 0.00003m, Time = 3 },
            new DriverProposal { DriverName = "Driver C", TrustEnforcerName = "Trust Enforcer 3", Price = 0.00005m, Time = 6 },
            new DriverProposal { DriverName = "Driver D", TrustEnforcerName = "Trust Enforcer 4", Price = 0.00006m, Time = 9 }
        };
    }

    public class DriverProposal
    {
        public string DriverName { get; set; }
        public string TrustEnforcerName { get; set; }
        public decimal Price { get; set; }
        public int Time { get; set; }
    }
}

