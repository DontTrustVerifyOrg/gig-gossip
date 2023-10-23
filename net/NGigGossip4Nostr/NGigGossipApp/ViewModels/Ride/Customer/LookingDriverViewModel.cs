using System;
using System.Windows.Input;
using CryptoToolkit;
using GigMobile.Services;
using NGeoHash;

namespace GigMobile.ViewModels.Ride.Customer
{
    public class LookingDriverViewModel : BaseViewModel<Tuple<Location, Location>>
    {
        private GigGossipNode _gigGossipNode;
        private readonly ISecureDatabase _secureDatabase;
        private Location _fromLocation;
        private Location _toLocation;

        private ICommand _cancelRequestCommand;
        public ICommand CancelRequestCommand => _cancelRequestCommand ??= new Command(() => NavigationService.NavigateAsync<ChooseDriverViewModel>());

        public LookingDriverViewModel(GigGossipNode gigGossipNode, ISecureDatabase secureDatabase)
        {
            _gigGossipNode = gigGossipNode;
            _secureDatabase = secureDatabase;
        }

        public override void Prepare(Tuple<Location, Location> data)
        {
            _fromLocation = data.Item1;
            _toLocation = data.Item2;
        }

        public override async Task Initialize()
        {
            await base.Initialize();

            var fromGh = GeoHash.Encode(latitude: _fromLocation.Latitude, longitude: _fromLocation.Longitude, numberOfChars: 7);
            var toGh = GeoHash.Encode(latitude: _toLocation.Latitude, longitude: _toLocation.Longitude, numberOfChars: 7);

            var trustEnforcers = await _secureDatabase.GetTrustEnforcersAsync();
            var trustEnforcer = trustEnforcers.Last().Value;
            var certificate = trustEnforcer.Certificate;

            await _gigGossipNode.BroadcastTopicAsync(new TaxiTopic()
            {
                FromGeohash = fromGh,
                ToGeohash = toGh,
                PickupAfter = DateTime.Now,
                DropoffBefore = DateTime.Now.AddHours(3)
            }, certificate);
        }
    }
}

