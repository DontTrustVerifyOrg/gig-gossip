using GigMobile.Services;
using GigMobile.ViewModels.Ride.Customer;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Projections;

namespace GigMobile.Pages.Ride.Customer;

public partial class PickLocationPage : BasePage<PickLocationViewModel>
{
    private MyLocationLayer _myLocationLayer;
    private CancellationTokenSource _cts;

    public PickLocationPage()
    {
        InitializeComponent();

        BuildMap();

        Loaded += PickLocationPage_Loaded;
    }

    protected virtual void BuildMap()
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
    }

    protected override void OnAppearing()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = Task.Run(async () => await StartUpdateUserLocation(_cts.Token));
        base.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        _cts?.Cancel();
        base.OnDisappearing();
    }

    private async Task StartUpdateUserLocation(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var location = await GeolocationService.GetCachedLocation();
            if (location != null)
            {
                var myLocation = new MPoint(location.Longitude, location.Latitude);
                Dispatcher.Dispatch(() => _myLocationLayer.UpdateMyLocation(myLocation));
            }
            await Task.Delay(2000, cancellationToken);
        }
    }

    void MapTouchStarted(System.Object sender, Mapsui.UI.TouchedEventArgs e)
    {
        _target.Fill = Colors.Transparent;
    }

    void MapTouchEnded(System.Object sender, Mapsui.UI.TouchedEventArgs e)
    {
        _target.Fill = Colors.Gray;

        var viewport = _mapView.Map.Navigator.Viewport;
        var (lon, lat) = SphericalMercator.ToLonLat(viewport.CenterX, viewport.CenterY);

        ViewModel.PickCoordinate(new Location(lat, lon));
    }

    private void PickLocationPage_Loaded(object sender, EventArgs e)
    {
        MPoint sphericalMercatorCoordinate = null;
        if (ViewModel?.InitCoordinate != null)
            sphericalMercatorCoordinate = SphericalMercator.FromLonLat(ViewModel.InitCoordinate.Longitude,
                ViewModel.InitCoordinate.Latitude).ToMPoint();
        else
        {
            //TODO Mock Start Location
            var centerOfSydney = new MPoint(151.209900, -33.865143);
            sphericalMercatorCoordinate = SphericalMercator.FromLonLat(centerOfSydney.X, centerOfSydney.Y).ToMPoint();
        }

        _mapView.Map.Home = n => n.CenterOnAndZoomTo(sphericalMercatorCoordinate, n.Resolutions[18]);
    }
}
