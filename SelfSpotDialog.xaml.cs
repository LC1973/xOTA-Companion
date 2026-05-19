using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using xOTACompanion.Models;
using xOTACompanion.Services;

namespace xOTACompanion
{
    public partial class SelfSpotDialog : Window
    {
        private readonly AppConfig    _config;
        private readonly PotaService   _pota   = new();
        private readonly SotaService   _sota   = new();
        private readonly WwbotaService _wwbota = new();

        /// <param name="config">Application config (for operator lookup and API keys).</param>
        /// <param name="currentFreqMhz">Pre-filled frequency in MHz, or null.</param>
        /// <param name="currentMode">Pre-filled mode string, or null.</param>
        public SelfSpotDialog(AppConfig config, double? currentFreqMhz = null, string? currentMode = null)
        {
            InitializeComponent();
            _config = config;

            // Pre-fill operator callsign
            var op = config.Operators.FirstOrDefault(o => o.Callsign == config.ActiveOperatorCallsign)
                  ?? config.Operators.FirstOrDefault(o => o.IsDefault)
                  ?? config.Operators.FirstOrDefault();
            CallsignBox.Text = op?.Callsign ?? config.MyCallsign;

            // Pre-fill frequency / mode
            if (currentFreqMhz.HasValue)
                FreqBox.Text = currentFreqMhz.Value.ToString("F4");
            if (!string.IsNullOrWhiteSpace(currentMode))
            {
                foreach (ComboBoxItem item in ModeCombo.Items)
                {
                    if (item.Content?.ToString() == currentMode)
                    { ModeCombo.SelectedItem = item; break; }
                }
            }
            if (ModeCombo.SelectedItem == null) ModeCombo.SelectedIndex = 0;

            PotaRadio.IsChecked = true;
        }

        private void Source_Changed(object sender, RoutedEventArgs e)
        {
            if (RefLabel == null) return;
            if (PotaRadio.IsChecked == true)
                RefLabel.Text = "PARK REFERENCE  (e.g. K-0001)";
            else if (BotaRadio.IsChecked == true)
                RefLabel.Text = "BUNKER REFERENCE  (e.g. B/G-2114)";
            else
                RefLabel.Text = "SUMMIT CODE  (e.g. G/SP-001)";
        }

        private async void Spot_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Visibility = Visibility.Collapsed;

            if (string.IsNullOrWhiteSpace(CallsignBox.Text))
            { ShowStatus("Callsign is required.", false); return; }
            if (string.IsNullOrWhiteSpace(ReferenceBox.Text))
            { ShowStatus("Reference is required.", false); return; }
            if (!double.TryParse(FreqBox.Text, out double freqMhz) || freqMhz <= 0)
            { ShowStatus("Please enter a valid frequency in MHz.", false); return; }
            var mode = (ModeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "SSB";

            SpotButton.IsEnabled = false;

            try
            {
                bool ok; string msg;
                if (PotaRadio.IsChecked == true)
                {
                    (ok, msg) = await _pota.PostSelfSpotAsync(
                        CallsignBox.Text.Trim().ToUpperInvariant(),
                        ReferenceBox.Text.Trim().ToUpperInvariant(),
                        freqMhz * 1000.0,   // PotaService expects kHz
                        mode,
                        CommentBox.Text.Trim());
                }
                else if (BotaRadio.IsChecked == true)
                {
                    (ok, msg) = await _wwbota.PostSelfSpotAsync(
                        CallsignBox.Text.Trim().ToUpperInvariant(),
                        ReferenceBox.Text.Trim().ToUpperInvariant(),
                        freqMhz,
                        mode,
                        CommentBox.Text.Trim());
                }
                else
                {
                    var op = _config.Operators.FirstOrDefault(o => o.Callsign == CallsignBox.Text.Trim().ToUpperInvariant());
                    var apiKey = op?.SotaApiKey ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(apiKey))
                    { ShowStatus("No SOTAwatch API key configured for this callsign.\nAdd it in Settings → Operators.", false); SpotButton.IsEnabled = true; return; }
                    (ok, msg) = await _sota.PostSelfSpotAsync(
                        CallsignBox.Text.Trim().ToUpperInvariant(),
                        ReferenceBox.Text.Trim().ToUpperInvariant(),
                        freqMhz,
                        mode,
                        apiKey,
                        CommentBox.Text.Trim());
                }
                ShowStatus(msg, ok);
                if (ok) SpotButton.Content = "✓ Spotted";
            }
            catch (Exception ex)
            {
                ShowStatus($"Error: {ex.Message}", false);
            }
            finally
            {
                SpotButton.IsEnabled = true;
            }
        }

        private void ShowStatus(string message, bool success)
        {
            StatusText.Text       = message;
            StatusText.Foreground = success
                ? (Brush)FindResource("SuccessGreen")
                : (Brush)FindResource("ErrorRed");
            StatusText.Visibility = Visibility.Visible;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
        }
    }
}
