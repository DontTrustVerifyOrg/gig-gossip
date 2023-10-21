using System.Windows.Input;
using CryptoToolkit;
using GigMobile.Services;
using NGigGossip4Nostr;

namespace GigMobile.ViewModels.Ride.Customer
{
    public class ConfirmDriverViewModel : BaseViewModel
    {
        private DriverProposal _selectedDriverProposal;
        private IGigGossipNodeEventSource gigGossipNodeEventSource;
        private DirectCom directCom;
        private ICommand _confirmCommand;
        public ICommand ConfirmCommand => _confirmCommand ??= new Command(() => NavigationService.NavigateAsync<RateDriverViewModel>());

        public ConfirmDriverViewModel(IGigGossipNodeEventSource gigGossipNodeEventSource, DirectCom directCom)
        {
            this.gigGossipNodeEventSource = gigGossipNodeEventSource;
            this.directCom = directCom;
        }

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

        public override void OnAppearing()
        {
            gigGossipNodeEventSource.OnResponseReady += GigGossipNodeEventSource_OnResponseReady;
            base.OnAppearing();
        }

        public override void OnDisappearing()
        {
            gigGossipNodeEventSource.OnResponseReady -= GigGossipNodeEventSource_OnResponseReady;
            base.OnDisappearing();
        }

        private async void GigGossipNodeEventSource_OnResponseReady(object sender, ResponseReadyEventArgs e)
        {
            directCom.Stop();
            directCom.RegisterFrameType<TaxiLocationFrame>();
            await directCom.StartAsync(e.TaxiReply.Relays);
            await directCom.SendMessageAsync(e.TaxiReply.PublicKey, new TaxiAckFrame()
            {
                Secret = e.TaxiReply.Secret
            }, true);
        }
    }
}
