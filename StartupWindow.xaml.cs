using System.Windows;
using xOTACompanion.Models;
using xOTACompanion.Services;

namespace xOTACompanion
{
    public partial class StartupWindow : Window
    {
        private readonly AppConfig _config;
        public bool Launched { get; private set; }
        public bool SkippedRadio { get; private set; }

        public StartupWindow()
        {
            InitializeComponent();
            try { Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/tree_icon.ico")); } catch { }
            _config = ConfigService.Load();
            PopulateOperators();
            PopulateRadios();
            if (_config.LoadedFromGreenLogger)
                SubtitleText.Text = "Operators and radios loaded from GreenLogger \u2713";
        }

        private void PopulateOperators()
        {
            if (_config.Operators.Count == 0)
            {
                // No GreenLogger — hide the operator/SOTA rows and show station info in subtitle
                OperatorLabelText.Visibility = Visibility.Collapsed;
                OperatorCombo.Visibility     = Visibility.Collapsed;
                SotaKeyLabel.Visibility      = Visibility.Collapsed;
                SotaKeyText.Visibility       = Visibility.Collapsed;
                SubtitleText.Text = !string.IsNullOrWhiteSpace(_config.MyCallsign)
                    ? $"Station: {_config.MyCallsign}" +
                      (!string.IsNullOrWhiteSpace(_config.MyLocator) ? $"  ({_config.MyLocator})" : "") +
                      "  —  select radio for this session."
                    : "GreenLogger not found — configure your station in ⚙\u00A0Settings.";
                return;
            }

            OperatorCombo.Items.Clear();
            foreach (var op in _config.Operators)
                OperatorCombo.Items.Add(op);

            // Select previously active operator
            var active = _config.Operators.FirstOrDefault(o => o.Callsign == _config.ActiveOperatorCallsign)
                      ?? _config.Operators.FirstOrDefault(o => o.IsDefault)
                      ?? _config.Operators.FirstOrDefault();
            OperatorCombo.SelectedItem = active ?? OperatorCombo.Items[0];
        }

        private void PopulateRadios()
        {
            RadioCombo.Items.Clear();
            RadioCombo.Items.Add(new RadioConfig { RadioId = 0, FriendlyName = "(no radio)", ControlType = "None" });
            foreach (var r in _config.Radios)
                RadioCombo.Items.Add(r);

            var active = _config.Radios.FirstOrDefault(r => r.RadioId == _config.ActiveRadioId)
                      ?? _config.Radios.FirstOrDefault(r => r.IsDefault)
                      ?? null;
            RadioCombo.SelectedItem = active ?? RadioCombo.Items[0];
        }

        private void OperatorCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (SotaKeyText == null) return;
            if (OperatorCombo.SelectedItem is OperatorProfile op)
            {
                SotaKeyText.Text = string.IsNullOrWhiteSpace(op.SotaApiKey) ? "not set" : "•••••••";
            }
        }

        private void RadioCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (RadioInfoText == null) return;
            if (RadioCombo.SelectedItem is RadioConfig rc)
            {
                RadioInfoText.Text = rc.ControlType switch
                {
                    "TCI" => $"TCI: {rc.TCIHost}:{rc.TCIPort}",
                    "CAT" => $"CAT serial: {rc.CATPortName} @ {rc.CATBaudRate} baud",
                    _     => "No radio connection"
                };
            }
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var cfg = new ConfigWindow(_config) { Owner = this };
            if (cfg.ShowDialog() == true)
            {
                ConfigService.Save(_config);
                PopulateOperators();
                PopulateRadios();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Launched = false;
            Close();
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            SaveSelections();
            Launched = true;
            Close();
        }

        private void SaveSelections()
        {
            if (_config.Operators.Count > 0 && OperatorCombo.SelectedItem is OperatorProfile op)
                _config.ActiveOperatorCallsign = op.Callsign;
            if (RadioCombo.SelectedItem is RadioConfig rc)
                _config.ActiveRadioId = rc.RadioId;
            ConfigService.Save(_config);
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
        }
    }
}
