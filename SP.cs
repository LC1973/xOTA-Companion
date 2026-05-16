using System.Windows;
using System.Windows.Controls;

namespace xOTACompanion
{
    /// <summary>
    /// Attached property that adds uniform spacing between StackPanel children,
    /// mimicking the WinUI3 / MAUI StackPanel.Spacing property.
    /// Usage in XAML:  local:SP.Gap="8"
    /// </summary>
    public static class SP
    {
        public static readonly DependencyProperty GapProperty =
            DependencyProperty.RegisterAttached("Gap", typeof(double), typeof(SP),
                new UIPropertyMetadata(0.0, OnGapChanged));

        public static double GetGap(DependencyObject obj) => (double)obj.GetValue(GapProperty);
        public static void SetGap(DependencyObject obj, double value) => obj.SetValue(GapProperty, value);

        private static void OnGapChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is StackPanel sp)
            {
                sp.Loaded -= Apply;
                sp.Loaded += Apply;
                // Apply immediately if already loaded
                if (sp.IsLoaded) Apply(sp, null!);
            }
        }

        private static void Apply(object sender, RoutedEventArgs e)
        {
            if (sender is not StackPanel sp) return;
            double gap = GetGap(sp);
            bool horizontal = sp.Orientation == Orientation.Horizontal;

            for (int i = 0; i < sp.Children.Count; i++)
            {
                if (sp.Children[i] is not FrameworkElement fe) continue;
                var m = fe.Margin;
                if (horizontal)
                    fe.Margin = new Thickness(i == 0 ? m.Left : gap, m.Top, m.Right, m.Bottom);
                else
                    fe.Margin = new Thickness(m.Left, i == 0 ? m.Top : gap, m.Right, m.Bottom);
            }
        }
    }
}
