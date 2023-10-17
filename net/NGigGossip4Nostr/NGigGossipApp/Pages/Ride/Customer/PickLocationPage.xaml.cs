using GigMobile.Services;
using GigMobile.ViewModels.Ride.Customer;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.UI;
using Mapsui.UI.Maui;

namespace GigMobile.Pages.Ride.Customer;

public partial class PickLocationPage : BasePage<PickLocationViewModel>
{
    private MyLocationLayer _myLocationLayer;
    private MapControl _mapControl;
    private CancellationTokenSource _cts;

    public PickLocationPage()
    {
        InitializeComponent();

        BuildMap();

        Loaded -= BasePage_Loaded;
    }

    private void BuildMap()
    {
        _mapControl = new MapControl();

        _mapControl.Map.Layers.Add(Mapsui.Tiling.OpenStreetMap.CreateTileLayer());

        _myLocationLayer = new MyLocationLayer(_mapControl.Map) { IsCentered = false };
        _mapControl.Map.Layers.Add(_myLocationLayer);

        _mapControl.Map.Widgets.Add(new Mapsui.Widgets.ScaleBar.ScaleBarWidget(_mapControl.Map)
        {
            TextAlignment = Mapsui.Widgets.Alignment.Center,
            HorizontalAlignment = Mapsui.Widgets.HorizontalAlignment.Left,
            VerticalAlignment = Mapsui.Widgets.VerticalAlignment.Bottom
        });

        _mapControl.Map.Widgets.Add(new Mapsui.Widgets.Zoom.ZoomInOutWidget()
        {
            HorizontalAlignment = Mapsui.Widgets.HorizontalAlignment.Right,
            VerticalAlignment = Mapsui.Widgets.VerticalAlignment.Top,
        });

        MPoint sphericalMercatorCoordinate = null;
        //if (ViewModel.UserCoordinate == null)
        //{
            var centerOfSydney = new MPoint(151.209900, -33.865143);
            sphericalMercatorCoordinate = SphericalMercator.FromLonLat(centerOfSydney.X, centerOfSydney.Y).ToMPoint();
        //}
        //else
        //    sphericalMercatorCoordinate = SphericalMercator.FromLonLat(ViewModel.UserCoordinate.Longitude, ViewModel.UserCoordinate.Latitude).ToMPoint();

        _mapControl.Map.Home = n => n.CenterOnAndZoomTo(sphericalMercatorCoordinate, n.Resolutions[18]);

        _mapControl.Map.Navigator.ViewportChanged += OnViewPortChanged;

        _mapView.Content = _mapControl;
    }

    private void OnViewPortChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        var viewport = (sender as Navigator).Viewport;
        var (lon, lat) = SphericalMercator.ToLonLat(viewport.CenterX, viewport.CenterY);
        ViewModel.OnMapCenterChanged(new Location(lat, lon));
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
}
