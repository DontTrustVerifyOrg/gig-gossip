using System.Windows.Input;

namespace GigMobile.ViewModels.Ride.Customer
{
    public class ConfirmDriverViewModel : BaseViewModel
    {
        private DriverProposal _selectedDriverProposal;
        private ICommand _confirmCommand;
        public ICommand ConfirmCommand => _confirmCommand ??= new Command(() => NavigationService.NavigateAsync<RateDriverViewModel>());

        async void ConfirmDriver()
        {
            NewResponseEventArgs args = _selectedDriverProposal.EventArgs;
            await args.GigGossipNode.AcceptResponseAsync(
                args.ReplyPayload,
                args.ReplyInvoice,
                args.DecodedReplyInvoice,
                args.NetworkInvoice,
                args.DecodedNetworkInvoice);
        }

    }
}

