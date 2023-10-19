using System;
using System.Windows.Input;
using CryptoToolkit;
using GigMobile.Services;
using NGeoHash;

namespace GigMobile.ViewModels.Ride.Customer
{
	public class LookingDriverViewModel : BaseViewModel
    {
        private GigGossipNode _gigGossipNode;

        private ICommand _cancelRequestCommand;
        public ICommand CancelRequestCommand => _cancelRequestCommand ??= new Command(() => NavigationService.NavigateAsync<ChooseDriverViewModel>());

        public LookingDriverViewModel(GigGossipNode gigGossipNode)
        {
            _gigGossipNode = gigGossipNode;
        }

        public override async Task Initialize()
        {
            await base.Initialize();

            var fromGh = GeoHash.Encode(latitude: 42.6, longitude: -5.6, numberOfChars: 7);
            var toGh = GeoHash.Encode(latitude: 42.5, longitude: -5.6, numberOfChars: 7);

            var trs = await SecureDatabase.GetTrustEnforcersAsync();
           // trs.Last().Certificate; 
            var certificate = Crypto.DeserializeObject<Certificate>(new byte[] { });//need to load proper certificate

            await _gigGossipNode.BroadcastTopicAsync(new TaxiTopic()
            {
                FromGeohash = fromGh,
                ToGeohash = toGh,
                PickupAfter = DateTime.Now,
                DropoffBefore = DateTime.Now.AddMinutes(20)
            },certificate);
        }
    }
}

