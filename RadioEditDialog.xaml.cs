using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using xOTACompanion.Models;

namespace xOTACompanion
{
    public partial class RadioEditDialog : Window
    {
        public RadioConfig Radio { get; }

        private static readonly int[] BaudRates = { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200 };

        public RadioEditDialog(RadioConfig radio)
        {
            InitializeComponent();
            Radio = radio;

            // Populate COM ports
            CatPortCombo.Items.Clear();
            foreach (var p in SerialPort.GetPortNames().OrderBy(x => x))
                CatPortCombo.Items.Add(p);
            if (!string.IsNullOrWhiteSpace(radio.CATPortName) && !CatPortCombo.Items.Contains(radio.CATPortName))
                CatPortCombo.Items.Insert(0, radio.CATPortName);

            // Populate baud rates
            BaudCombo.Items.Clear();
            foreach (var b in BaudRates) BaudCombo.Items.Add(b);

            // Fill values
            NameBox.Text      = radio.FriendlyName;
            TciHostBox.Text   = radio.TCIHost;
            TciPortBox.Text   = radio.TCIPort.ToString();
            IsDefaultCheck.IsChecked = radio.IsDefault;

            TypeCombo.SelectedIndex = radio.ControlType switch { "CAT" => 1, "TCI" => 2, "CIV" => 3, _ => 0 };
            // Leave the box at its XAML default ("94") as a suggestion when the address
            // is unset, so the user has a sensible starting point but must confirm/edit it.
            if (radio.CIVAddress > 0)
                CivAddressBox.Text = radio.CIVAddress.ToString("X2");
            CatPortCombo.SelectedItem = radio.CATPortName;
            BaudCombo.SelectedItem    = radio.CATBaudRate;
            if (BaudCombo.SelectedItem == null) BaudCombo.SelectedItem = 38400;
        }

        private void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var t = (TypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "None";
            bool isSerial = t == "CAT" || t == "CI-V (ICOM)";
            CatPanel.Visibility = isSerial ? Visibility.Visible : Visibility.Collapsed;
            CivPanel.Visibility = t == "CI-V (ICOM)" ? Visibility.Visible : Visibility.Collapsed;
            TciPanel.Visibility = t == "TCI" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                DarkMessageBox.Show("Name is required.", "Validation", owner: this);
                return;
            }

            Radio.FriendlyName = NameBox.Text.Trim();
            var selectedType   = (TypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "None";
            Radio.ControlType  = selectedType == "CI-V (ICOM)" ? "CIV" : selectedType;
            Radio.IsDefault    = IsDefaultCheck.IsChecked == true;
            Radio.CATPortName  = CatPortCombo.SelectedItem?.ToString() ?? string.Empty;
            Radio.CATBaudRate  = BaudCombo.SelectedItem is int br ? br : 38400;
            Radio.TCIHost      = TciHostBox.Text.Trim();
            if (int.TryParse(TciPortBox.Text, out int p)) Radio.TCIPort = p;
            if (int.TryParse(CivAddressBox.Text.TrimStart('0', 'x'), System.Globalization.NumberStyles.HexNumber,
                             null, out int civAddr))
                Radio.CIVAddress = civAddr;

            if (!Radio.IsValid())
            {
                DarkMessageBox.Show("Please fill in all required fields for the selected control type.", "Validation", owner: this);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
        }
    }
}
