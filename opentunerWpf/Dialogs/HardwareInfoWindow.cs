using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using opentuner.MediaSources;

namespace OpenTuner.Wpf.Dialogs
{
    /// <summary>Native WPF hardware info window.</summary>
    public class HardwareInfoWindow : Window
    {
        private readonly OTSource _source;
        private readonly TextBlock _name = new TextBlock { FontWeight = FontWeights.SemiBold };
        private readonly TextBox _desc = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Height = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        public HardwareInfoWindow(OTSource source)
        {
            _source = source;

            Title = LocalizationManager.Get("dt.hardware");
            Width = 480; Height = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)Application.Current.FindResource("WindowBackground");
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = "Device", Foreground = (Brush)Application.Current.FindResource("TextSecondary") });
            panel.Children.Add(_name);
            panel.Children.Add(new TextBlock { Text = "Description", Margin = new Thickness(0, 12, 0, 4), Foreground = (Brush)Application.Current.FindResource("TextSecondary") });
            panel.Children.Add(_desc);

            var refresh = new Button { Content = LocalizationManager.Get("btn.refresh"), Width = 100, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
            refresh.Click += (s, e) => Refresh();
            panel.Children.Add(refresh);

            Content = panel;
            Refresh();
        }

        private void Refresh()
        {
            if (_source == null)
            {
                _name.Text = "No source connected";
                _desc.Text = "";
                return;
            }

            try { _name.Text = _source.GetDeviceName(); } catch { }
            try { _desc.Text = _source.GetDescription(); } catch { }
        }
    }
}
