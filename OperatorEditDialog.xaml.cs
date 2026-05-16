using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using xOTACompanion.Models;

namespace xOTACompanion
{
    public partial class OperatorEditDialog : Window
    {
        public OperatorProfile Profile { get; }

        public OperatorEditDialog(OperatorProfile profile)
        {
            InitializeComponent();
            Profile = profile;
            CallsignBox.Text     = profile.Callsign;
            NameBox.Text         = profile.Name;
            LocatorBox.Text      = profile.Locator;
            SotaKeyBox.Password  = profile.SotaApiKey;
            IsDefaultCheck.IsChecked = profile.IsDefault;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CallsignBox.Text))
            {
                DarkMessageBox.Show("Callsign is required.", "Validation", owner: this);
                return;
            }
            Profile.Callsign   = CallsignBox.Text.Trim().ToUpperInvariant();
            Profile.Name       = NameBox.Text.Trim();
            Profile.Locator    = LocatorBox.Text.Trim().ToUpperInvariant();
            Profile.SotaApiKey = SotaKeyBox.Password.Trim();
            Profile.IsDefault  = IsDefaultCheck.IsChecked == true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void FetchToken_Click(object sender, RoutedEventArgs e)
        {
            SotaTokenPanel.Visibility = SotaTokenPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
            SotaTokenStatus.Text = string.Empty;
        }

        private async void GetToken_Click(object sender, RoutedEventArgs e)
        {
            var username = SotaUsernameBox.Text.Trim();
            var password = SotaPasswordBox.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                SotaTokenStatus.Text = "Enter username and password.";
                return;
            }

            GetTokenButton.IsEnabled = false;
            SotaTokenStatus.Foreground = (System.Windows.Media.Brush)FindResource("DarkTextSecondary");
            SotaTokenStatus.Text = "Fetching…";

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var form = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "password"),
                    new KeyValuePair<string, string>("client_id",  "sotawatch"),
                    new KeyValuePair<string, string>("username",   username),
                    new KeyValuePair<string, string>("password",   password),
                });

                var resp = await http.PostAsync(
                    "https://sso.sota.org.uk/auth/realms/SOTA/protocol/openid-connect/token",
                    form);
                var body = await resp.Content.ReadAsStringAsync();

                if (resp.IsSuccessStatusCode)
                {
                    var doc   = JsonDocument.Parse(body);
                    var token = doc.RootElement.GetProperty("access_token").GetString() ?? string.Empty;
                    SotaKeyBox.Password           = token;
                    SotaTokenPanel.Visibility     = Visibility.Collapsed;
                    SotaPasswordBox.Password      = string.Empty;
                    SotaTokenStatus.Text          = string.Empty;
                }
                else
                {
                    SotaTokenStatus.Foreground = (System.Windows.Media.Brush)FindResource("ErrorRed");
                    SotaTokenStatus.Text = $"Failed ({(int)resp.StatusCode}) — check credentials.";
                }
            }
            catch (Exception ex)
            {
                SotaTokenStatus.Foreground = (System.Windows.Media.Brush)FindResource("ErrorRed");
                SotaTokenStatus.Text = $"Error: {ex.Message}";
            }
            finally
            {
                GetTokenButton.IsEnabled = true;
            }
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
        }
    }
}
