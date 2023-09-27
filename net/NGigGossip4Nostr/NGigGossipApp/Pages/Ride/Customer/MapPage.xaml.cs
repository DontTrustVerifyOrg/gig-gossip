using GigMobile.Services;
using GigMobile.ViewModels.Ride.Customer;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.UI.Maui;

namespace GigMobile.Pages.Ride.Customer;

public partial class MapPage : BasePage<MapViewModel>
{
    private MapControl _mapControl;
    private MyLocationLayer _myLocationLayer;

    public MapPage()
    {
        InitializeComponent();

        BuildMap();
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

        var centerOfSydney = new MPoint(151.209900 , - 33.865143);
        var sphericalMercatorCoordinate = SphericalMercator.FromLonLat(centerOfSydney.X, centerOfSydney.Y).ToMPoint();
        _mapControl.Map.Home = n => n.CenterOnAndZoomTo(sphericalMercatorCoordinate, n.Resolutions[15]);

        Content = _mapControl;
    }

    private async Task OnMapLoaded()
    {
        var location = await GeolocationService.GetCachedLocation();
        if (location != null)
        {
            var myLocation = new MPoint(location.Longitude, location.Latitude);
            var sphericalMercatorCoordinate = SphericalMercator.FromLonLat(myLocation.X, myLocation.Y).ToMPoint();
            _mapControl.Map.Navigator.CenterOnAndZoomTo(sphericalMercatorCoordinate, _mapControl.Map.Navigator.Resolutions[15]);
            _myLocationLayer.UpdateMyLocation(myLocation);
        }
    }
}
