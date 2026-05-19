using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using xOTACompanion.Models;
using xOTACompanion.Services;

namespace xOTACompanion
{
    public partial class MainWindow : Window
    {
        // ── data ──────────────────────────────────────────────────────────────
        private AppConfig _config = ConfigService.Load();
        private readonly ObservableCollection<SpotModel> _allSpots = new();
        private ICollectionView? _view;
        private readonly PotaService   _pota   = new();
        private readonly SotaService   _sota   = new();
        private readonly WwbotaService _wwbota = new();
        private DispatcherTimer? _refreshTimer;
        private DateTime _lastRefresh = DateTime.MinValue;
        private bool _isLoadingSpots;
        private IRadioService? _radioService;

        // ── construction ──────────────────────────────────────────────────────
        public MainWindow(bool skippedRadio = false)
        {
            InitializeComponent();
            try { Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/tree_icon.ico")); } catch { }

            RestoreWindowPosition();
            InitFilters();
            BindGrid();
            RefreshOperatorDisplay();
            InitRadio(skippedRadio);
            StartRefreshTimer();

            _ = LoadSpotsAsync();
        }

        // ── window chrome ─────────────────────────────────────────────────────
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }
        private void Minimize_Click(object sender, RoutedEventArgs e)   => WindowState = WindowState.Minimized;
        private void MaxRestore_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void CloseBtn_Click(object sender, RoutedEventArgs e)   => Close();
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            SaveWindowPosition();
            _refreshTimer?.Stop();
            RadioManager.Instance.SetActiveRadio(null);
            Application.Current.Shutdown();
        }

        // ── window position ───────────────────────────────────────────────────
        private void RestoreWindowPosition()
        {
            if (_config.WindowWidth > 100 && _config.WindowHeight > 100)
            {
                Left   = _config.WindowLeft;
                Top    = _config.WindowTop;
                Width  = _config.WindowWidth;
                Height = _config.WindowHeight;

                // If window would be off all screens, reset to centre
                double vsLeft   = SystemParameters.VirtualScreenLeft;
                double vsTop    = SystemParameters.VirtualScreenTop;
                double vsRight  = vsLeft + SystemParameters.VirtualScreenWidth;
                double vsBottom = vsTop  + SystemParameters.VirtualScreenHeight;
                if (Left < vsLeft || Left + Width > vsRight ||
                    Top  < vsTop  || Top + Height > vsBottom)
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    Left = double.NaN;
                    Top  = double.NaN;
                }
            }
        }
        private void SaveWindowPosition()
        {
            if (double.IsNaN(ActualWidth) || double.IsInfinity(ActualWidth)) return;
            if (double.IsNaN(Left)        || double.IsInfinity(Left))        return;
            _config.WindowLeft   = Left;
            _config.WindowTop    = Top;
            _config.WindowWidth  = ActualWidth;
            _config.WindowHeight = ActualHeight;
            ConfigService.Save(_config);
        }

        // ── operator / radio display ──────────────────────────────────────────
        private void RefreshOperatorDisplay()
        {
            var op = ActiveOperator();
            OperatorLabel.Text = op != null ? op.Callsign : "(no operator)";
        }

        private OperatorProfile? ActiveOperator()
        {
            var op = _config.Operators.FirstOrDefault(o => o.Callsign == _config.ActiveOperatorCallsign)
                  ?? _config.Operators.FirstOrDefault(o => o.IsDefault)
                  ?? _config.Operators.FirstOrDefault();
            if (op == null && !string.IsNullOrWhiteSpace(_config.MyCallsign))
                return new OperatorProfile { Callsign = _config.MyCallsign, Locator = _config.MyLocator };
            return op;
        }

        // ── radio init ────────────────────────────────────────────────────────
        private void InitRadio(bool skipped)
        {
            RadioLabel.Text = "(no radio)";
            RadioDot.Fill   = (Brush)FindResource("ErrorRed");

            if (skipped) return;

            var rc = _config.Radios.FirstOrDefault(r => r.RadioId == _config.ActiveRadioId);
            if (rc == null || rc.ControlType == "None") return;

            IRadioService? svc = rc.ControlType switch
            {
                "CAT" => new CATService(rc.CATPortName, rc.CATBaudRate),
                "TCI" => new TCIService(rc.TCIHost, rc.TCIPort),
                "CIV" => new CIVService(rc.CATPortName, rc.CATBaudRate, (byte)rc.CIVAddress),
                _     => null
            };
            if (svc == null) return;

            svc.Connected        += () => Dispatcher.Invoke(() =>
            {
                RadioDot.Fill             = (Brush)FindResource("SuccessGreen");
                RadioLabel.Text           = rc.FriendlyName;
                RadioConnectBtn.Content   = "Disconnect";
                RadioConnectBtn.IsEnabled = true;
            });
            svc.Disconnected     += () => Dispatcher.Invoke(() =>
            {
                RadioDot.Fill             = (Brush)FindResource("ErrorRed");
                RadioLabel.Text           = "(disconnected)";
                FreqModeLabel.Text        = string.Empty;
                RadioConnectBtn.Content   = "⟳ Connect";
                RadioConnectBtn.IsEnabled = true;
            });
            svc.RadioInfoUpdated += (freq, mode, pwr) => Dispatcher.Invoke(() =>
            {
                FreqModeLabel.Text = $"{freq:F4} MHz  {mode}";
            });
            svc.TXModeChanged += (isTx) => Dispatcher.Invoke(() =>
            {
                FreqModeLabel.Foreground = isTx
                    ? new SolidColorBrush(Color.FromRgb(0xCC, 0x22, 0x22))
                    : new SolidColorBrush(Color.FromRgb(0x00, 0xCC, 0x00));
            });

            _radioService              = svc;
            RadioConnectBtn.Content    = "Connecting…";
            RadioConnectBtn.IsEnabled  = false;
            RadioConnectBtn.Visibility = System.Windows.Visibility.Visible;
            RadioManager.Instance.SetActiveRadio(svc);
            _ = svc.ConnectAsync();
        }


        // ── radio connect button ──────────────────────────────────────────────
        private async void RadioConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_radioService == null) return;
            if (_radioService.IsConnected)
            {
                _radioService.Disconnect();
                // Disconnected event updates the button
            }
            else
            {
                RadioConnectBtn.Content   = "Connecting…";
                RadioConnectBtn.IsEnabled = false;
                bool ok = await _radioService.ConnectAsync();
                // Re-enable in case the service didn't fire Connected/Disconnected
                RadioConnectBtn.IsEnabled = true;
                if (!ok && !_radioService.IsConnected)
                    RadioConnectBtn.Content = "⟳ Connect";
            }
        }

        // ── grid / filter setup ───────────────────────────────────────────────
        private void BindGrid()
        {
            _view = CollectionViewSource.GetDefaultView(_allSpots);
            _view.Filter = ApplyFilter;
            SpotsGrid.ItemsSource = _view;
        }

        private void InitFilters()
        {
            BandFilter.Items.Add("All");
            foreach (var b in new[] { "160m","80m","60m","40m","30m","20m","17m","15m","12m","10m","6m","2m","70cm" })
                BandFilter.Items.Add(b);
            BandFilter.SelectedIndex = 0;

            ModeFilter.Items.Add("All");
            foreach (var m in new[] { "SSB","CW","FT8","FT4","DIGI","AM","FM" })
                ModeFilter.Items.Add(m);
            ModeFilter.SelectedIndex = 0;
        }

        private bool ApplyFilter(object obj)
        {
            if (obj is not SpotModel s) return false;

            if (PotaFilter.IsChecked  == true && s.Source != SpotSource.POTA)   return false;
            if (SotaFilter.IsChecked  == true && s.Source != SpotSource.SOTA)   return false;
            if (BotaFilter.IsChecked  == true && s.Source != SpotSource.WWBOTA) return false;

            var band = BandFilter.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(band) && band != "All" && s.Band != band) return false;

            var mode = ModeFilter.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(mode) && mode != "All" && !s.Mode.StartsWith(mode, StringComparison.OrdinalIgnoreCase)) return false;

            if (RemoveQrtFilter.IsChecked == true &&
                s.Comments != null &&
                s.Comments.IndexOf("QRT", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            var q = SearchBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var ql = q.ToUpperInvariant();
                if (!s.Activator.ToUpperInvariant().Contains(ql) &&
                    !s.Reference.ToUpperInvariant().Contains(ql) &&
                    !(s.Name?.ToUpperInvariant().Contains(ql) ?? false) &&
                    !(s.Comments?.ToUpperInvariant().Contains(ql) ?? false))
                    return false;
            }

            return true;
        }

        private void Filter_Changed(object sender, RoutedEventArgs e) => _view?.Refresh();
        private void Filter_Changed(object sender, SelectionChangedEventArgs e) => _view?.Refresh();
        private void Filter_Changed(object sender, TextChangedEventArgs e) => _view?.Refresh();

        // ── spot loading ──────────────────────────────────────────────────────
        private async Task LoadSpotsAsync()
        {
            if (_isLoadingSpots) return;
            _isLoadingSpots = true;
            try
            {
                SetStatus("Fetching spots…");
                var tasks = new List<Task<List<SpotModel>>>();
                if (_config.ShowPota)   tasks.Add(_pota.FetchSpotsAsync());
                if (_config.ShowSota)   tasks.Add(_sota.FetchSpotsAsync());
                if (_config.ShowWwbota) tasks.Add(_wwbota.FetchSpotsAsync());

                var results = await Task.WhenAll(tasks);

                // Deduplicate: group by activator+reference, keep most recent spot, sum spot counts
                var all = results
                    .SelectMany(r => r)
                    .GroupBy(s => (s.Source, s.Activator.ToUpperInvariant(), s.Reference.ToUpperInvariant()))
                    .Select(g =>
                    {
                        var best = g.OrderByDescending(s => s.SpottedUtc).First();
                        best.SpotCount = g.Sum(s => s.SpotCount > 0 ? s.SpotCount : 1);
                        return best;
                    })
                    .ToList();

                // Calculate distances
                var op    = ActiveOperator();
                var myLoc = op?.Locator ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(myLoc))
                {
                    var (myLat, myLon) = MaidenheadService.LocatorToCoordinates(myLoc);
                    foreach (var s in all)
                    {
                        if (s.Latitude.HasValue && s.Longitude.HasValue)
                        {
                            double distKm = MaidenheadService.CalculateDistanceFromCoords(myLat, myLon, s.Latitude.Value, s.Longitude.Value);
                            s.DistanceKm = _config.DistanceUnit == "mi" ? distKm * 0.621371 : distKm;
                            s.DistanceUnit = _config.DistanceUnit;
                        }
                    }
                }

                // Sort by distance then by age
                var sorted = all.OrderBy(s => s.DistanceKm ?? double.MaxValue).ThenBy(s => s.SpottedUtc).ToList();

                Dispatcher.Invoke(() =>
                {
                    // Track which spot keys existed before the refresh
                    var existingKeys = _allSpots
                        .Select(s => (s.Source, s.Activator.ToUpperInvariant(), s.Reference.ToUpperInvariant()))
                        .ToHashSet();
                    bool isFirstLoad = _lastRefresh == DateTime.MinValue;

                    _allSpots.Clear();
                    foreach (var s in sorted) _allSpots.Add(s);

                    _view?.Refresh();
                    _lastRefresh = DateTime.Now;
                    LastRefreshLabel.Text = $"Last: {_lastRefresh:HH:mm:ss}";
                    UpdateStatusBar();

                    // Defer IsNew flag until after rows are rendered so DataTrigger
                    // always sees a false→true transition and fires EnterActions.
                    if (!isFirstLoad)
                    {
                        var toFlash = sorted
                            .Where(s => !existingKeys.Contains(
                                (s.Source, s.Activator.ToUpperInvariant(), s.Reference.ToUpperInvariant())))
                            .ToList();

                        if (toFlash.Count > 0)
                        {
                            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                            {
                                foreach (var s in toFlash) s.IsNew = true;

                                var resetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.1) };
                                resetTimer.Tick += (_, _) =>
                                {
                                    foreach (var s in toFlash) s.IsNew = false;
                                    resetTimer.Stop();
                                };
                                resetTimer.Start();
                            }));
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogCategory.General, $"Spot fetch error: {ex.Message}");
                Dispatcher.Invoke(() => SetStatus($"Error fetching spots: {ex.Message}"));
            }
            finally
            {
                _isLoadingSpots = false;
            }
        }

        private void UpdateStatusBar()
        {
            int pota  = _allSpots.Count(s => s.Source == SpotSource.POTA);
            int sota  = _allSpots.Count(s => s.Source == SpotSource.SOTA);
            int bota  = _allSpots.Count(s => s.Source == SpotSource.WWBOTA);
            StatusLabel.Text  = $"{_allSpots.Count} spots  (POTA: {pota}  SOTA: {sota}  BOTA: {bota})";
            StatusRight.Text  = $"Refreshes every {_config.AutoRefreshMinutes} min";
        }
        private void SetStatus(string msg) => StatusLabel.Text = msg;

        // ── auto-refresh ──────────────────────────────────────────────────────
        private void StartRefreshTimer()
        {
            if (_config.AutoRefreshMinutes <= 0) return;
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(_config.AutoRefreshMinutes)
            };
            _refreshTimer.Tick += (_, _) => _ = LoadSpotsAsync();
            _refreshTimer.Start();
        }

        // ── toolbar buttons ───────────────────────────────────────────────────
        private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadSpotsAsync();

        private void SelfSpot_Click(object sender, RoutedEventArgs e)
        {
            var radio  = RadioManager.Instance.ActiveRadio;
            var dlg    = new SelfSpotDialog(_config,
                radio?.CurrentFrequency,
                radio?.CurrentMode) { Owner = this };
            dlg.ShowDialog();
        }

        private void Map_Click(object sender, RoutedEventArgs e) => OpenMapForSelected();

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ConfigWindow(_config) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                ConfigService.Save(_config);
                RefreshOperatorDisplay();
                RestartRefreshTimer();
                _ = LoadSpotsAsync();
            }
        }

        private void RestartRefreshTimer()
        {
            _refreshTimer?.Stop();
            _refreshTimer = null;
            StartRefreshTimer();
        }

        // ── grid events ───────────────────────────────────────────────────────
        private void SpotsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            MapButton.IsEnabled = SpotsGrid.SelectedItem is SpotModel s && s.HasLocation;
            if (TuneOnSelectFilter.IsChecked == true && SpotsGrid.SelectedItem is SpotModel spot)
                TuneToSpot(spot);
        }

        private void SpotsGrid_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SpotsGrid.SelectedItem is SpotModel spot)
                TuneToSpot(spot);
        }

        private void SpotsGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (TuneOnSelectFilter.IsChecked == true &&
                (e.Key == Key.Up || e.Key == Key.Down) &&
                SpotsGrid.SelectedItem is SpotModel spot)
            {
                TuneToSpot(spot);
            }
        }

        private void ContextTune_Click(object sender, RoutedEventArgs e)
        {
            if (SpotsGrid.SelectedItem is SpotModel s) TuneToSpot(s);
        }

        private void ActivatorLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Documents.Hyperlink link &&
                link.DataContext is SpotModel spot)
            {
                // Strip portable/prefix parts: EI/G7NRU/P → G7NRU (longest segment)
                var callsign = spot.Activator.Trim().ToUpperInvariant()
                    .Split('/')
                    .OrderByDescending(s => s.Length)
                    .First();
                var url = $"https://www.qrz.com/db/{callsign}";
                try
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Logger.Instance.Log(LogCategory.General, $"QRZ link failed: {ex.Message}");
                    DarkMessageBox.Show($"Could not open browser.\n{ex.Message}", "QRZ", owner: this);
                }
            }
        }

        private void CopyCallsign_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi &&
                mi.Parent is ContextMenu cm &&
                cm.PlacementTarget is TextBlock tb &&
                tb.DataContext is SpotModel spot)
            {
                Clipboard.SetText(spot.Activator);
            }
        }
        private void ContextMap_Click(object sender, RoutedEventArgs e) => OpenMapForSelected();
        private void ContextSelfSpot_Click(object sender, RoutedEventArgs e) => SelfSpot_Click(sender, e);

        // ── actions ───────────────────────────────────────────────────────────
        private void TuneToSpot(SpotModel spot)
        {
            if (RadioManager.Instance.ActiveRadio == null)
            {
                DarkMessageBox.Show("No radio connected.", "Tune", owner: this);
                return;
            }
            RadioManager.Instance.TuneToSpot(spot.FrequencyMhz, spot.Mode);
            Logger.Instance.Log(LogCategory.Radio, $"Tuned to {spot.Activator} on {spot.FrequencyDisplay} {spot.Mode}");
        }

        private void OpenMapForSelected()
        {
            if (SpotsGrid.SelectedItem is not SpotModel spot) return;
            if (!spot.HasLocation)
            {
                DarkMessageBox.Show("No location data for this spot.", "Map", owner: this);
                return;
            }
            var op    = ActiveOperator();
            var token = _config.MapboxAccessToken;

            // Resolve token the same way MapWindow does — check env vars if config is empty
            if (string.IsNullOrWhiteSpace(token))
                token = Environment.GetEnvironmentVariable("XOTA_MAPBOX_TOKEN")
                     ?? Environment.GetEnvironmentVariable("MAPBOX_ACCESS_TOKEN")
                     ?? string.Empty;

            if (string.IsNullOrWhiteSpace(token))
            {
                DarkMessageBox.Show(
                    "Maps require a free Mapbox access token.\n\n" +
                    "To get one:\n" +
                    "  1.  Sign up for a free account at  mapbox.com\n" +
                    "  2.  Copy your Default public token from\n" +
                    "       account.mapbox.com/access-tokens\n" +
                    "       (it starts with  pk.)\n\n" +
                    "  3.  Open Settings (⚙) and paste the token\n" +
                    "       into the Mapbox Token field, then Save.\n",
                    "Maps — setup required",
                    owner: this);
                return;
            }

            var map   = new MapWindow(spot, op?.Locator ?? string.Empty, string.IsNullOrWhiteSpace(token) ? null : token)
            {
                Owner = this
            };
            map.Show();
        }
    }
}
