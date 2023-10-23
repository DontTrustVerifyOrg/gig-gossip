using System;
using System.Windows.Input;
using CryptoToolkit;
using GigMobile.Services;
using NGigGossip4Nostr;

namespace GigMobile.ViewModels.Ride.Driver
{
    public class DriverParametersViewModel : BaseViewModel
    {
        private ICommand _saveCommand;
        public ICommand SaveCommand => _saveCommand ??= new Command(() => NavigationService.NavigateAsync<RideNotificationViewModel>());

        private string secret;
        private string customerPublicKey;
        private DirectCom directCom;
        private readonly ISecureDatabase _secureDatabase;

        public DriverParametersViewModel(DirectCom directCom, ISecureDatabase secureDatabase)
        {
            this.directCom = directCom;
            _secureDatabase = secureDatabase;
        }

        public async void Accept(AcceptBroadcastEventArgs args, long fee)
        {
            var taxiTopic = Crypto.DeserializeObject<TaxiTopic>(
                args.BroadcastFrame.BroadcastPayload.SignedRequestPayload.Topic);

            if (taxiTopic != null)
            {
                secret = Crypto.GenerateRandomPreimage().AsHex();
                var taxiReply = new TaxiReply()
                {
                    PublicKey = args.GigGossipNode.PublicKey,
                    Relays = args.GigGossipNode.NostrRelays,
                    Secret = secret,
                };

                var trustEnforcers = await _secureDatabase.GetTrustEnforcersAsync();
                var trustEnforcer = trustEnforcers.Last().Value;
                var certificate = trustEnforcer.Certificate;

                directCom.Stop();
                directCom.RegisterFrameType<TaxiAckFrame>();
                directCom.RegisterFrameType<TaxiLocationFrame>();
                directCom.OnDirectMessage += DirectCom_OnDirectMessage;
                await directCom.StartAsync(args.GigGossipNode.NostrRelays);

                await args.GigGossipNode.AcceptBroadcastAsync(args.PeerPublicKey, args.BroadcastFrame,
                    new AcceptBroadcastResponse()
                    {
                        Message = Crypto.SerializeObject(taxiReply),
                        Fee = fee,
                        SettlerServiceUri = new Uri(trustEnforcer.Uri),
                        MyCertificate = certificate
                    });
            }

        }

        private void OnTaxiAckFrame(string senderPublicKey, TaxiAckFrame taxiAckFrame)
        {
            if (taxiAckFrame.Secret == secret)
            {
                customerPublicKey = senderPublicKey;
            }
        }

        private void OnTaxiLocationFrame(string senderPublicKey,TaxiLocationFrame taxiLocationFrame)
        {
            if (customerPublicKey == senderPublicKey)
            {
                var loc=taxiLocationFrame.Location; //draw location
            }
        }

        private void DirectCom_OnDirectMessage(object sender, DirectMessageEventArgs e)
        {
            if (e.Frame is TaxiAckFrame)
                OnTaxiAckFrame(e.SenderPublicKey,(TaxiAckFrame)e.Frame);
            else if (e.Frame is TaxiLocationFrame)
                OnTaxiLocationFrame(e.SenderPublicKey, (TaxiLocationFrame)e.Frame);

        }
    }
}

