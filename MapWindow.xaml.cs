using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using xOTACompanion.Models;
using xOTACompanion.Services;

namespace xOTACompanion
{
    [SupportedOSPlatform("windows10.0.17763.0")]
    public partial class MapWindow : Window
    {
        private readonly double _myLat, _myLon;
        private readonly string _myGrid;
        private readonly SpotModel _spot;
        private readonly double _targetLat, _targetLon;
        private readonly string _mapboxToken;

        public MapWindow(SpotModel SpotModel, string myGrid, string? mapboxToken = null)
        {
            InitializeComponent();
            _spot = SpotModel;
            _myGrid = myGrid ?? string.Empty;
            _mapboxToken = ResolveToken(mapboxToken);

            (_myLat, _myLon) = MaidenheadService.LocatorToCoordinates(_myGrid);

            // Determine target location
            if (!string.IsNullOrWhiteSpace(SpotModel.Grid) && SpotModel.Grid.Length >= 4)
            {
                (_targetLat, _targetLon) = MaidenheadService.LocatorToCoordinates(SpotModel.Grid);
            }
            else if (SpotModel.Latitude.HasValue && SpotModel.Longitude.HasValue)
            {
                _targetLat = SpotModel.Latitude.Value;
                _targetLon = SpotModel.Longitude.Value;
            }
            else
            {
                _targetLat = 0;
                _targetLon = 0;
            }

            TitleText.Text = $"{SpotModel.Source}  {SpotModel.Reference}  –  {SpotModel.Activator}";
            Loaded += OnLoaded;
        }

        private string ResolveToken(string? override_)
        {
            var t = !string.IsNullOrWhiteSpace(override_) ? override_
                  : Environment.GetEnvironmentVariable("XOTA_MAPBOX_TOKEN")
                    ?? Environment.GetEnvironmentVariable("MAPBOX_ACCESS_TOKEN")
                    ?? string.Empty;
            if (!string.IsNullOrEmpty(t) && !t.StartsWith("pk.", StringComparison.Ordinal) && !t.StartsWith("sk.", StringComparison.Ordinal))
                return string.Empty;
            return t;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var env = await WebView2Warmup.GetEnvironmentAsync()
                       ?? await CoreWebView2Environment.CreateAsync(null, WebView2Warmup.UserDataFolder);
                await MapWebView.EnsureCoreWebView2Async(env);
                MapWebView.NavigationCompleted += (s, a) =>
                {
                    if (a.IsSuccess) { LoadingPanel.Visibility = Visibility.Collapsed; MapWebView.Visibility = Visibility.Visible; }
                    else LoadingText.Text = $"Map load failed: {a.WebErrorStatus}";
                };
                MapWebView.NavigateToString(BuildHtml());
            }
            catch (Exception ex)
            {
                LoadingText.Text = $"Error: {ex.Message}";
                Logger.Instance.Log(LogCategory.Map, $"MapWindow: {ex.Message}");
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }
            else DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        // -----------------------------------------------------------------------
        private string BuildHtml()
        {
            if (string.IsNullOrWhiteSpace(_mapboxToken))
                return NoTokenHtml();

            var c = CultureInfo.InvariantCulture;

            double centerLat  = (_myLat + _targetLat) / 2;
            double centerLon  = (_myLon + _targetLon) / 2;
            double distKm     = MaidenheadService.CalculateDistanceFromCoords(_myLat, _myLon, _targetLat, _targetLon);
            double distMi     = distKm * 0.621371;
            double zoom       = ZoomForDistance(distKm);

            var gcCoords = BuildGreatCircle(120);

            string spotIcon   = _spot.Source switch
            {
                SpotSource.POTA   => "🌳",
                SpotSource.SOTA   => "⛰️",
                SpotSource.WWBOTA => "🏰",
                _                 => "📍"
            };
            string sourceName = _spot.Source.ToString();
            string refHtml    = EH(_spot.Reference);
            string nameHtml   = EH(_spot.Name);
            string actHtml    = EH(_spot.Activator);
            string gridHtml   = EH(!string.IsNullOrWhiteSpace(_spot.Grid) ? _spot.Grid : "–");
            string modeHtml   = EH(_spot.Mode);
            string freqHtml   = EH(_spot.FrequencyDisplay);

            return $@"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='initial-scale=1,maximum-scale=1,user-scalable=no'>
<title>SpotModel Map</title>
<script src='https://api.mapbox.com/mapbox-gl-js/v3.0.1/mapbox-gl.js'></script>
<link href='https://api.mapbox.com/mapbox-gl-js/v3.0.1/mapbox-gl.css' rel='stylesheet'/>
<style>
*{{margin:0;padding:0;box-sizing:border-box}}
body{{font-family:'Segoe UI',Tahoma,sans-serif;background:#1E1E1E}}
#map{{position:absolute;top:0;bottom:56px;width:100%}}
.info{{position:absolute;bottom:0;left:0;right:0;height:56px;background:linear-gradient(135deg,#1A2A3A,#0A1520);
       border-top:1px solid #1A2A3A;display:flex;align-items:center;padding:0 20px;gap:24px}}
.col{{display:flex;flex-direction:column}}
.label{{font-size:9px;color:#888;text-transform:uppercase;letter-spacing:.5px}}
.val{{font-size:14px;font-weight:bold;color:#00BFFF}}
.valsub{{font-size:12px;color:#CCC}}
.dist{{font-size:20px;font-weight:bold;color:#00FF00}}
.mapboxgl-popup-content{{background:#1A2A3A;color:#CCC;border-radius:8px;padding:12px 16px;border:1px solid #264F78}}
.mapboxgl-popup-tip{{border-top-color:#1A2A3A}}
.pt{{font-size:15px;font-weight:bold;color:#00BFFF;margin-bottom:4px}}
.pc{{font-size:11px;line-height:1.6}}
.mapboxgl-popup-close-button{{color:#888;font-size:18px}}
</style>
</head>
<body>
<div id='map'></div>
<div class='info'>
  <div class='col'><div class='label'>{sourceName}</div><div class='val'>{spotIcon} {refHtml}</div></div>
  <div class='col'><div class='label'>Activator</div><div class='val'>{actHtml}</div><div class='valsub'>{nameHtml}</div></div>
  <div class='col'><div class='label'>Frequency</div><div class='val'>{freqHtml}</div><div class='valsub'>{modeHtml}</div></div>
  <div class='col'><div class='label'>Grid</div><div class='val' style='font-size:12px;font-family:Consolas,monospace'>{gridHtml}</div></div>
  <div style='flex:1'></div>
  <div class='col' style='align-items:flex-end'>
    <div class='label'>Distance</div>
    <div class='dist'>{distKm:N0} km</div>
    <div class='valsub' style='color:#AAA'>{distMi:N0} mi</div>
  </div>
</div>
<script>
mapboxgl.accessToken = '{EJ(_mapboxToken)}';
const map = new mapboxgl.Map({{
  container:'map',
  style:'mapbox://styles/mapbox/dark-v11',
  center:[{centerLon.ToString(c)},{centerLat.ToString(c)}],
  zoom:{zoom.ToString(c)},
  projection:'globe',
  attributionControl:false
}});
map.on('style.load',()=>{{
  map.setFog({{color:'rgb(20,20,30)','high-color':'rgb(40,50,80)','horizon-blend':0.02,'space-color':'rgb(10,10,20)','star-intensity':0.6}});
}});
map.addControl(new mapboxgl.NavigationControl(),'top-right');
map.addControl(new mapboxgl.ScaleControl(),'bottom-left');
map.on('load',()=>{{
  map.addSource('path',{{'type':'geojson','data':{{'type':'Feature','properties':{{}},'geometry':{{'type':'LineString','coordinates':[{gcCoords}]}}}}}});
  map.addLayer({{'id':'path-glow','type':'line','source':'path','paint':{{'line-color':'#4285F4','line-width':10,'line-opacity':0.2,'line-blur':4}}}});
  map.addLayer({{'id':'path-line','type':'line','source':'path','paint':{{'line-color':'#4285F4','line-width':3}}}});
  function pin(color){{const e=document.createElement('div');e.innerHTML=`<svg xmlns=""http://www.w3.org/2000/svg"" width=""28"" height=""40"" viewBox=""0 0 24 36""><path fill=""${{color}}"" stroke=""#FFF"" stroke-width=""1.5"" d=""M12 0C5.4 0 0 5.4 0 12c0 7.2 12 24 12 24s12-16.8 12-24C24 5.4 18.6 0 12 0z""/><circle fill=""#FFF"" cx=""12"" cy=""12"" r=""5""/></svg>`;return e;}}
  new mapboxgl.Marker({{element:pin('#34A853'),offset:[0,-20]}})
    .setLngLat([{_myLon.ToString(c)},{_myLat.ToString(c)}])
    .setPopup(new mapboxgl.Popup({{offset:[0,-42]}}).setHTML('<div class=""pt"">📡 My Station</div><div class=""pc""><b>Grid:</b> {EJ(_myGrid)}</div>'))
    .addTo(map);
  new mapboxgl.Marker({{element:pin('#EA4335'),offset:[0,-20]}})
    .setLngLat([{_targetLon.ToString(c)},{_targetLat.ToString(c)}])
    .setPopup(new mapboxgl.Popup({{offset:[0,-42]}}).setHTML('<div class=""pt"">{spotIcon} {actHtml}</div><div class=""pc""><b>Ref:</b> {refHtml}<br/><b>Name:</b> {nameHtml}<br/><b>Freq:</b> {freqHtml} MHz&nbsp;{modeHtml}<br/><b>Grid:</b> {gridHtml}<br/><b>Distance:</b> {distKm:N0} km / {distMi:N0} mi</div>'))
    .addTo(map);
  const b=new mapboxgl.LngLatBounds();
  b.extend([{_myLon.ToString(c)},{_myLat.ToString(c)}]);
  b.extend([{_targetLon.ToString(c)},{_targetLat.ToString(c)}]);
  map.fitBounds(b,{{padding:80}});
  map.once('moveend',()=>{{try{{map.zoomOut();}}catch(e){{}}}});
}});
</script>
</body>
</html>";
        }

        private string BuildGreatCircle(int segs)
        {
            var c = CultureInfo.InvariantCulture;
            double lat1 = Rad(_myLat), lon1 = Rad(_myLon);
            double lat2 = Rad(_targetLat), lon2 = Rad(_targetLon);

            double cosD = Math.Sin(lat1) * Math.Sin(lat2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Cos(lon2 - lon1);
            cosD = Math.Clamp(cosD, -1.0, 1.0);
            double d = Math.Acos(cosD);
            if (d < 1e-12) return $"[{_myLon.ToString(c)},{_myLat.ToString(c)}],[{_targetLon.ToString(c)},{_targetLat.ToString(c)}]";

            double sinD = Math.Sin(d);
            var sb = new StringBuilder();
            double? prevLon = null;

            for (int i = 0; i <= segs; i++)
            {
                double f = i / (double)segs;
                double A = Math.Sin((1 - f) * d) / sinD;
                double B = Math.Sin(f * d) / sinD;

                double x = A * Math.Cos(lat1) * Math.Cos(lon1) + B * Math.Cos(lat2) * Math.Cos(lon2);
                double y = A * Math.Cos(lat1) * Math.Sin(lon1) + B * Math.Cos(lat2) * Math.Sin(lon2);
                double z = A * Math.Sin(lat1) + B * Math.Sin(lat2);

                double latR = Math.Atan2(z, Math.Sqrt(x * x + y * y));
                double lonR = Math.Atan2(y, x);
                double lonD = Deg(lonR);

                if (prevLon.HasValue)
                {
                    while (lonD - prevLon.Value > 180) lonD -= 360;
                    while (lonD - prevLon.Value < -180) lonD += 360;
                }
                prevLon = lonD;

                if (i > 0) sb.Append(',');
                sb.Append($"[{lonD.ToString(c)},{Deg(latR).ToString(c)}]");
            }
            return sb.ToString();
        }

        private static string NoTokenHtml() =>
            @"<!doctype html><html><head><style>body{margin:24px;background:#111;color:#eee;font-family:Segoe UI}</style></head>
<body><h2>⚠️ Mapbox token not configured</h2>
<p>Open <b>Settings</b> and enter your Mapbox access token.</p>
<p style='color:#888;margin-top:8px'>Free tokens are available at <i>mapbox.com</i></p></body></html>";

        private static double ZoomForDistance(double km) => km switch
        {
            < 50   => 9, < 100  => 8, < 250 => 7, < 500  => 6,
            < 1000 => 5, < 2000 => 4, < 5000 => 3, < 10000 => 2, _ => 1
        };

        private static double Rad(double d) => d * Math.PI / 180.0;
        private static double Deg(double r) => r * 180.0 / Math.PI;

        // HTML-escape
        private static string EH(string s) =>
            (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        // JS-escape
        private static string EJ(string s) =>
            (s ?? "").Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"").Replace("\n", "\\n");
    }
}
