using System.Windows;
using System.Windows.Controls;
using xOTACompanion.Models;
using xOTACompanion.Services;

namespace xOTACompanion
{
    public partial class ConfigWindow : Window
    {
        private readonly AppConfig _config;

        public ConfigWindow(AppConfig config)
        {
            InitializeComponent();
            try { Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/tree_icon.ico")); } catch { }
            _config = config;
            LoadValues();
        }

        private void LoadValues()
        {
            RefreshBox.Text          = _config.AutoRefreshMinutes.ToString();
            ShowPotaCheck.IsChecked  = _config.ShowPota;
            ShowSotaCheck.IsChecked  = _config.ShowSota;
            ShowWwbotaCheck.IsChecked = _config.ShowWwbota;
            KmRadio.IsChecked       = _config.DistanceUnit != "mi";
            MiRadio.IsChecked       = _config.DistanceUnit == "mi";

            if (!_config.LoadedFromGreenLogger)
            {
                StationSection.Visibility = Visibility.Visible;
                RadiosSection.Visibility  = Visibility.Visible;
                MyCallsignBox.Text        = _config.MyCallsign;
                MyLocatorBox.Text         = _config.MyLocator;
                MapboxTokenBox.Text       = _config.MapboxAccessToken;
                RefreshRadioList();
            }

            RefreshOperatorList();
        }

        private void RefreshOperatorList()
        {
            OperatorListBox.Items.Clear();
            foreach (var op in _config.Operators)
            {
                var keyStatus = string.IsNullOrWhiteSpace(op.SotaApiKey) ? "not set" : "\u2022\u2022\u2022\u2022\u2022\u2022\u2022";
                OperatorListBox.Items.Add(new ListBoxItem
                {
                    Content = $"{op.Callsign}  —  SOTA key: {keyStatus}",
                    Tag     = op
                });
            }
            OperatorsSection.Visibility = _config.Operators.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            EditSotaKeyButton.IsEnabled = false;
        }

        private void OperatorListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            EditSotaKeyButton.IsEnabled = OperatorListBox.SelectedItem != null;
        }

        private void EditOperator_Click(object sender, RoutedEventArgs e)
        {
            if (OperatorListBox.SelectedItem is not ListBoxItem item) return;
            if (item.Tag is not OperatorProfile op) return;
            var dlg = new OperatorEditDialog(op) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                ConfigService.Save(_config);
                RefreshOperatorList();
            }
        }

        private void RefreshRadioList()
        {
            RadioListBox.Items.Clear();
            foreach (var r in _config.Radios)
                RadioListBox.Items.Add(r);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(RefreshBox.Text, out int mins)) _config.AutoRefreshMinutes = Math.Max(0, mins);
            _config.ShowPota     = ShowPotaCheck.IsChecked  == true;
            _config.ShowSota     = ShowSotaCheck.IsChecked  == true;
            _config.ShowWwbota   = ShowWwbotaCheck.IsChecked == true;
            _config.DistanceUnit = MiRadio.IsChecked == true ? "mi" : "km";

            if (StationSection.Visibility == Visibility.Visible)
            {
                _config.MyCallsign        = MyCallsignBox.Text.Trim().ToUpperInvariant();
                _config.MyLocator         = MyLocatorBox.Text.Trim().ToUpperInvariant();
                _config.MapboxAccessToken = MapboxTokenBox.Text.Trim();
            }

            DialogResult = true;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void AddRadio_Click(object sender, RoutedEventArgs e)
        {
            var newRadio = new RadioConfig
            {
                RadioId = _config.Radios.Count > 0 ? _config.Radios.Max(r => r.RadioId) + 1 : 1
            };
            var dlg = new RadioEditDialog(newRadio) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _config.Radios.Add(dlg.Radio);
                RefreshRadioList();
            }
        }

        private void EditRadio_Click(object sender, RoutedEventArgs e)
        {
            if (RadioListBox.SelectedItem is not RadioConfig selected) return;
            var dlg = new RadioEditDialog(selected) { Owner = this };
            if (dlg.ShowDialog() == true)
                RefreshRadioList();
        }

        private void RemoveRadio_Click(object sender, RoutedEventArgs e)
        {
            if (RadioListBox.SelectedItem is not RadioConfig selected) return;
            _config.Radios.Remove(selected);
            RefreshRadioList();
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
        }
    }
}
