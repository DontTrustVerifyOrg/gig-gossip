using GigMobile.Services;
using GigMobile.ViewModels.Ride.Customer;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.UI.Maui;

namespace GigMobile.Pages.Ride.Customer;

public partial class CreateRidePage : BasePage<CreateRideViewModel>
{
    private MyLocationLayer _myLocationLayer;
    private CancellationTokenSource _cts;

    private Pin _fromPin;
    private Pin _toPin;

    public CreateRidePage()
    {
        InitializeComponent();

        BuildMap();

        Loaded -= BasePage_Loaded;
    }

    private void BuildMap()
    {
        _mapView.IsMyLocationButtonVisible = false;
        _mapView.IsNorthingButtonVisible = false;
        _mapView.IsZoomButtonVisible = false;

        _mapView.Map.Layers.Add(Mapsui.Tiling.OpenStreetMap.CreateTileLayer());

        _myLocationLayer = new MyLocationLayer(_mapView.Map) { IsCentered = false };
        _mapView.Map.Layers.Add(_myLocationLayer);

        _mapView.Map.Widgets.Add(new Mapsui.Widgets.ScaleBar.ScaleBarWidget(_mapView.Map)
        {
            MarginY = 20f,
            TextAlignment = Mapsui.Widgets.Alignment.Center,
            HorizontalAlignment = Mapsui.Widgets.HorizontalAlignment.Left,
            VerticalAlignment = Mapsui.Widgets.VerticalAlignment.Bottom
        });
        
        MPoint sphericalMercatorCoordinate = null;
        if (ViewModel?.InitUserCoordinate != null)
            sphericalMercatorCoordinate = SphericalMercator.FromLonLat(ViewModel.InitUserCoordinate.Longitude,
                ViewModel.InitUserCoordinate.Latitude).ToMPoint();
        else
        {
            var centerOfSydney = new MPoint(151.209900, -33.865143);
            sphericalMercatorCoordinate = SphericalMercator.FromLonLat(centerOfSydney.X, centerOfSydney.Y).ToMPoint();
        }

        _mapView.Map.Home = n => n.CenterOnAndZoomTo(sphericalMercatorCoordinate, n.Resolutions[18]);
    }

    protected override void OnAppearing()
    {
        ViewModel.PropertyChanged += OnVMPropertyChanged;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = Task.Run(async () => await StartUpdateUserLocation(_cts.Token));
        base.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        ViewModel.PropertyChanged -= OnVMPropertyChanged;

        _cts?.Cancel();
        base.OnDisappearing();
    }


    private async void OnVMPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        try
        {
            //TODO Lockation mock
            List<Location> allLocations = new() { new Location(-33.865143, 151.209900) };

            if (e.PropertyName == nameof(ViewModel.FromLocation))
            {
                if (_fromPin != null)
                    _mapView.Pins.Remove(_fromPin);

                _fromPin = new Pin(_mapView)
                {
                    Label = "Picking up point",
                    Address = ViewModel.FromAddress,
                    Position = new Position(ViewModel.FromLocation.Latitude, ViewModel.FromLocation.Longitude),
                    Color = Color.FromArgb("#00A4B4")
                };
                _mapView.Pins.Add(_fromPin);
                allLocations.Add(ViewModel.FromLocation);
            }
            else if (e.PropertyName == nameof(ViewModel.ToLocation))
            {
                if (_toPin != null)
                    _mapView.Pins.Remove(_toPin);

                _toPin = new Pin(_mapView)
                {
                    Label = "Destination point",
                    Address = ViewModel.ToAddress,
                    Position = new Position(ViewModel.ToLocation.Latitude, ViewModel.ToLocation.Longitude),
                    Color = Color.FromArgb("#043418")
                };
                _mapView.Pins.Add(_toPin);
                allLocations.Add(ViewModel.ToLocation);
            }
            else
                return;

            if (_fromPin != null && _toPin != null)
            {
                _mapView.Drawables.Clear();

                var line = new Polyline { StrokeWidth = 7, StrokeColor = Mapsui.UI.Maui.KnownColor.Black };

                var routeLocations = await ViewModel.GetRouteAsync();

                foreach (var pt in routeLocations)
                {
                    line.Positions.Add(new Position(pt.Latitude, pt.Longitude));
                    allLocations.Add(pt);
                }

                _mapView.Drawables.Add(line);
            }

            var xMin = allLocations.Select(x => x.Longitude).Min() - 0.003;
            var xMax = allLocations.Select(x => x.Longitude).Max() + 0.003;
            var yMin = allLocations.Select(x => x.Latitude).Min() - 0.003;
            var yMax = allLocations.Select(x => x.Latitude).Max() + 0.003;

            var startPoint = SphericalMercator.FromLonLat(xMin, yMin).ToMPoint();
            var endPoint = SphericalMercator.FromLonLat(xMax, yMax).ToMPoint();

            _mapView.Map.Navigator.ZoomToBox(new MRect(startPoint.X, startPoint.Y, endPoint.X, endPoint.Y));
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    private async Task StartUpdateUserLocation(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var location = await GeolocationService.GetCachedLocation();
            //TODO Mock
            location ??= new Location(-33.865143, 151.209900);
            if (location != null)
            {
                _myLocationLayer.UpdateMyLocation(SphericalMercator.FromLonLat(location.Longitude, location.Latitude).ToMPoint());
            }
            await Task.Delay(2000, cancellationToken);
        }
    }
}
