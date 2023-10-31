using System.Collections.ObjectModel;
using System.Windows.Input;
using GigMobile.Services;
using NGeoHash;

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

    public class ChooseDriverViewModel : BaseViewModel<Tuple<Location, Location>>
    {
        private readonly GigGossipNode _gigGossipNode;
        private readonly IGigGossipNodeEventSource _gigGossipNodeEventSource;
        private readonly ISecureDatabase _secureDatabase;
        private readonly IGeocoder _geocoder;
        private Location _fromLocation;
        private Location _toLocation;
        private Guid _requestTopic;

        public ChooseDriverViewModel(GigGossipNode gigGossipNode,
            IGigGossipNodeEventSource gigGossipNodeEventSource,
            ISecureDatabase secureDatabase,
            IGeocoder geocoder)
        {
            _gigGossipNode = gigGossipNode;
            _gigGossipNodeEventSource = gigGossipNodeEventSource;
            _secureDatabase = secureDatabase;
            _geocoder = geocoder;
        }

        public override void Prepare(Tuple<Location, Location> data)
        {
            _fromLocation = data.Item1;
            _toLocation = data.Item2;
        }

        private ICommand _cancelRequestCommand;
        public ICommand CancelRequestCommand => _cancelRequestCommand ??= new Command(async () =>
        {
            //TODO Cancel request
            //await _gigGossipNode.CancelBroadcastTopicAsync(_requestTopic);
            
            await NavigationService.NavigateBackAsync();
        });

        private ICommand _selectDriverCommand;
        public ICommand SelectDriverCommand => _selectDriverCommand ??= new Command<DriverProposal>(async (proposal) =>
        {
            await NavigationService.NavigateAsync<ConfirmDriverViewModel>();
        });

        public ObservableCollection<DriverProposal> DriversProposals { get; set; } = new();

        public string FromAddress { get; set; }
        public string ToAddress { get; set; }

        public override async Task Initialize()
        {
            await base.Initialize();

            _ = Task.Run(async () =>
            {
                FromAddress = await _geocoder.GetLocationAddress(_fromLocation);
                ToAddress = await _geocoder.GetLocationAddress(_toLocation);
                var fromGh = GeoHash.Encode(latitude: _fromLocation.Latitude, longitude: _fromLocation.Longitude, numberOfChars: 7);
                var toGh = GeoHash.Encode(latitude: _toLocation.Latitude, longitude: _toLocation.Longitude, numberOfChars: 7);
                var trustEnforcers = await _secureDatabase.GetTrustEnforcersAsync();
                var trustEnforcer = trustEnforcers.Last().Value;
                var certificate = trustEnforcer.Certificate;
                _requestTopic = await _gigGossipNode.BroadcastTopicAsync(new TaxiTopic()
                {
                    FromGeohash = fromGh,
                    ToGeohash = toGh,
                    PickupAfter = DateTime.Now,
                    DropoffBefore = DateTime.Now.AddHours(3)
                }, certificate);

                while (DriversProposals.Count < 10)
                {
                    try
                    {
                        await Task.Delay(2000);
                        var proposal = new DriverProposal()
                        {
                            DriverName = "Oscar",
                            Price = 25,
                            WaitTime = DateTimeOffset.MinValue, //TODO arrival time estimate
                        };
                        Application.Current.MainPage.Dispatcher.Dispatch(() => DriversProposals.Add(proposal));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }
            });
        }

        private void OnNewDriverProposed(object sender, NewResponseEventArgs e)
        {
            Application.Current.MainPage.Dispatcher.Dispatch(() => 
                DriversProposals.Add(new DriverProposal()
                {
                    EventArgs = e,
                    DriverProperies = e.ReplyPayload.ReplierCertificate.Properties,
                    Price = e.DecodedNetworkInvoice.NumSatoshis + e.DecodedReplyInvoice.NumSatoshis,
                    SecurityCenterUri = e.ReplyPayload.ReplierCertificate.ServiceUri,
                    WaitTime = DateTimeOffset.MinValue, //TODO arrival time estimate
                }));
        }

        public override void OnAppearing()
        {
            _gigGossipNodeEventSource.OnNewResponse += OnNewDriverProposed;

            base.OnAppearing();
        }

        public override void OnDisappearing()
        {
            _gigGossipNodeEventSource.OnNewResponse -= OnNewDriverProposed;

            base.OnDisappearing();
        }
    }
}

