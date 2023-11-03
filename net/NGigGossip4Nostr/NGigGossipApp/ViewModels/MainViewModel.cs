using System.Windows.Input;
using BindedMvvm.Attributes;
using CryptoToolkit;
using GigMobile.Services;
using NGeoHash;

namespace GigMobile.ViewModels
{
    [CleanHistory]
    public class MainViewModel : BaseViewModel
    {
        private readonly GigGossipNode _gigGossipNode;
        private readonly IGigGossipNodeEventSource _gigGossipNodeEventSource;
        private readonly ISecureDatabase _secureDatabase;

        public MainViewModel(GigGossipNode gigGossipNode, IGigGossipNodeEventSource gigGossipNodeEventSource, ISecureDatabase secureDatabase)
        {
            _gigGossipNode = gigGossipNode;
            _gigGossipNodeEventSource = gigGossipNodeEventSource;
            _secureDatabase = secureDatabase;
        }

        private ICommand _editTrustEnforcersCommand;
        public ICommand EditTrustEnforcersCommand => _editTrustEnforcersCommand ??= new Command(async () =>
        {
            IsBusy = false;
            await NavigationService.NavigateAsync<TrustEnforcers.TrustEnforcersViewModel>();
            IsBusy = true;
        });

        private ICommand _editWalletDomainCommand;
        public ICommand EditWalletDomainCommand => _editWalletDomainCommand ??= new Command(() => { NavigationService.NavigateAsync<Wallet.AddWalletViewModel>(animated: true); });

        private ICommand _walletDetailsCommand;
        public ICommand WalletDetailsCommand => _walletDetailsCommand ??= new Command(() => { NavigationService.NavigateAsync<Wallet.WalletDetailsViewModel, string>(WalletAddress, animated: true); });

        private ICommand _requestRideCommand;
        public ICommand RequestRideCommand => _requestRideCommand ??= new Command(async () =>
        {
            if (DriverModeOn)
                await Application.Current.MainPage.DisplayAlert("You cann't request a ride", "Please disable a driver mode to use a rider feature's", "Cancel");
            else if (string.IsNullOrEmpty(DefaultTrustEnforcer))
                await Application.Current.MainPage.DisplayAlert("You cann't request a ride", "Firstly setup at least one trust enforcer", "Cancel");
            else
            {
                IsBusy = true;
                await NavigationService.NavigateAsync<Ride.Customer.CreateRideViewModel>(animated: true);
                IsBusy = false;
            }
        });

        private ICommand _driverParametersCommand;
        public ICommand DriverParametersCommand => _driverParametersCommand ??= new Command(() => { NavigationService.NavigateAsync<Ride.Driver.DriverParametersViewModel>(animated: true); });

        private ICommand _withdrawBitcoinCommand;
        public ICommand WithdrawBitcoinCommand => _withdrawBitcoinCommand ??= new Command(() => { NavigationService.NavigateAsync<Wallet.WithdrawBitcoinViewModel>(); });

        public string WalletAddress { get; private set; }
        public long BitcoinBallance { get; private set; }
        public string DefaultTrustEnforcer { get; private set; }

        private bool _driverModeOn;
        public bool DriverModeOn
        {
            get => _driverModeOn;
            set
            {
                _driverModeOn = value;
                if (_driverModeOn)
                {
                    if (string.IsNullOrEmpty(DefaultTrustEnforcer))
                    {
                        Application.Current.MainPage.DisplayAlert("You cann't be a driver", "Firstly setup at least one trust enforcer", "Cancel");
#pragma warning disable CA2011 // Avoid infinite recursion
                        DriverModeOn = false;
#pragma warning restore CA2011 // Avoid infinite recursion
                    }
                    else
                    {
                        //TODO PAWEL
                        _gigGossipNodeEventSource.OnAcceptBroadcast += OnAcceptBroadcast;
                    }

                }
                else
                {
                    //TODO PAWEL
                    _gigGossipNodeEventSource.OnAcceptBroadcast -= OnAcceptBroadcast;
                }
            }
        }

        public async override void OnAppearing()
        {
            IsBusy = true;
            var token = _gigGossipNode.MakeWalletAuthToken();
            WalletAddress = await _gigGossipNode.LNDWalletClient.NewAddressAsync(token);
            BitcoinBallance = await _gigGossipNode.LNDWalletClient.GetBalanceAsync(token);
            var trustEnforcers = await _secureDatabase.GetTrustEnforcersAsync();
            DefaultTrustEnforcer = trustEnforcers?.Values?.Last()?.Name;

            IsBusy = false;

            base.OnAppearing();
        }

        public override void OnDisappearing()
        {
            base.OnDisappearing();
        }

        private static Tuple<Location, Location> GeohashToBoundingBox(string geohash)
        {
            var decoded = GeoHash.Decode(geohash);
            var latitude = decoded.Coordinates.Lat;
            var longitude = decoded.Coordinates.Lon;

            var errorMarginLat = decoded.Error.Lat;
            var errorMarginLon = decoded.Error.Lon;
            return Tuple.Create(new Location(latitude - errorMarginLat, longitude - errorMarginLon), new Location(latitude + errorMarginLat, longitude + errorMarginLon));
        }

        private void OnAcceptBroadcast(object sender, AcceptBroadcastEventArgs e)
        {
            var senderProps = e.BroadcastFrame.BroadcastPayload.SignedRequestPayload.SenderCertificate.Properties;
            var senderSecurityCenter = e.BroadcastFrame.BroadcastPayload.SignedRequestPayload.SenderCertificate.ServiceUri;
            var topic = Crypto.DeserializeObject<TaxiTopic>(e.BroadcastFrame.BroadcastPayload.SignedRequestPayload.Topic);
            var fromLocBB = GeohashToBoundingBox(topic.FromGeohash);
            var toLocBB = GeohashToBoundingBox(topic.ToGeohash);
            var pickupAfter = topic.PickupAfter;
            var dropOffBefore = topic.DropoffBefore;

            //TODO: display new job
            //TODO: show passanger properties (at least "PhoneNumber" should be in the least) with the message: PhoneNumber of the passenger was verified by senderSecurityCenter
            //TODO: on a map there should be a box showing approx location of the passenger
            //TODO: also picpup after and dropoff before time should be displayed
            //TODO: and butons Accept/Reject '
            //TODO: Accept => DriverParametersModel.Accept(e)
        }

    }
}

