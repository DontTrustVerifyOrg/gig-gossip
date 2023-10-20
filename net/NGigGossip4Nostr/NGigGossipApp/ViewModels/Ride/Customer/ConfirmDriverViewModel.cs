using System.Windows.Input;
using CryptoToolkit;
using GigGossipFrames;
using GigMobile.Services;
using NGigGossip4Nostr;

namespace GigMobile.ViewModels.Ride.Customer
{
    public class ConfirmDriverViewModel : BaseViewModel
    {
        private DriverProposal _selectedDriverProposal;
        private IGigGossipNodeEventSource gigGossipNodeEventSource;
        private ICommand _confirmCommand;
        public ICommand ConfirmCommand => _confirmCommand ??= new Command(() => NavigationService.NavigateAsync<RateDriverViewModel>());

        public ConfirmDriverViewModel(IGigGossipNodeEventSource gigGossipNodeEventSource)
        {
            this.gigGossipNodeEventSource = gigGossipNodeEventSource;
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
            var directCom = new DirectCom(e.GigGossipNode, e.TaxiReply.Relays, GigGossipNodeConfig.ChunkSize);
            await directCom.SendDirectMessage(e.TaxiReply.PublicKey, new DirectMessage()
            {
                Relays = e.GigGossipNode.NostrRelays,
                Kind = "ACK",
                Data = Crypto.SerializeObject(e.TaxiReply.Secret)
            });
        }
    }
}
