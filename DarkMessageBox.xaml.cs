using System.Windows;

namespace xOTACompanion
{
    public partial class DarkMessageBox : Window
    {
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        private DarkMessageBox() => InitializeComponent();

        public static MessageBoxResult Show(string message, string title = "xOTA Companion",
            MessageBoxButton buttons = MessageBoxButton.OK,
            MessageBoxImage icon = MessageBoxImage.None,
            Window? owner = null)
        {
            var dlg = new DarkMessageBox();
            dlg.TitleText.Text = title;
            dlg.MessageText.Text = message;

            dlg.IconText.Text = icon switch
            {
                MessageBoxImage.Error       => "❌",
                MessageBoxImage.Warning     => "⚠️",
                MessageBoxImage.Question    => "❓",
                MessageBoxImage.Information => "ℹ️",
                _ => ""
            };

            switch (buttons)
            {
                case MessageBoxButton.OKCancel:
                    dlg.CancelButton.Visibility = Visibility.Visible;
                    break;
                case MessageBoxButton.YesNo:
                    dlg.OkButton.Visibility = Visibility.Collapsed;
                    dlg.YesButton.Visibility = Visibility.Visible;
                    dlg.NoButton.Visibility = Visibility.Visible;
                    break;
                case MessageBoxButton.YesNoCancel:
                    dlg.OkButton.Visibility = Visibility.Collapsed;
                    dlg.YesButton.Visibility = Visibility.Visible;
                    dlg.NoButton.Visibility = Visibility.Visible;
                    dlg.CancelButton.Visibility = Visibility.Visible;
                    break;
            }

            if (owner != null) dlg.Owner = owner;
            dlg.ShowDialog();
            return dlg.Result;
        }

        private void OkButton_Click(object s, RoutedEventArgs e) { Result = MessageBoxResult.OK; Close(); }
        private void CancelButton_Click(object s, RoutedEventArgs e) { Result = MessageBoxResult.Cancel; Close(); }
        private void YesButton_Click(object s, RoutedEventArgs e) { Result = MessageBoxResult.Yes; Close(); }
        private void NoButton_Click(object s, RoutedEventArgs e) { Result = MessageBoxResult.No; Close(); }
    }
}
