using System.Collections.ObjectModel;
using System.Windows.Input;

namespace GigMobile.ViewModels.Ride.Customer
{

    public class DriverProposal
    {
        public NewResponseEventArgs EventArgs;
        public string DriverName { get; set; }
        public string[] DriverProperies { get; set; }
        public Uri SecurityCenterUri { get; set; }
        public long Price { get; set; }
        public DateTimeOffset WaitTime { get; set; }
    }

    public class ChooseDriverViewModel : BaseViewModel
    {
        private ICommand _cancelRequestCommand;
        private readonly IGigGossipNodeEventSource gigGossipNodeEventSource;

        public ICommand CancelRequestCommand => _cancelRequestCommand ??= new Command(() => NavigationService.NavigateAsync<ConfirmDriverViewModel>());

        public ObservableCollection<DriverProposal> DriversProposals { get; set; } = new();

        public ChooseDriverViewModel(IGigGossipNodeEventSource gigGossipNodeEventSource)
        {
            this.gigGossipNodeEventSource = gigGossipNodeEventSource;
        }

        public override void OnAppearing()
        {
            base.OnAppearing();
            gigGossipNodeEventSource.OnNewResponse += gigGossipNodeEventSource_OnNewResponse;
        }

        private void gigGossipNodeEventSource_OnNewResponse(object sender, NewResponseEventArgs e)
        {
            DriversProposals.Add(new DriverProposal()
            {
                EventArgs = e,
                DriverProperies = e.ReplyPayload.ReplierCertificate.Properties,
                Price = e.DecodedNetworkInvoice.NumSatoshis + e.DecodedReplyInvoice.NumSatoshis,
                SecurityCenterUri = e.ReplyPayload.ReplierCertificate.ServiceUri,
                WaitTime = DateTimeOffset.MinValue, //TODO arrival time estimate
            });
        }

        public override void OnDisappearing()
        {
            gigGossipNodeEventSource.OnNewResponse -= gigGossipNodeEventSource_OnNewResponse;
            base.OnDisappearing();
        }
    }
}

