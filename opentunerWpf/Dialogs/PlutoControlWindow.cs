using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using opentuner.Transmit;

namespace OpenTuner.Wpf.Dialogs
{
    /// <summary>Native WPF Pluto (F5OEO) transmit control window.</summary>
    public class PlutoControlWindow : Window
    {
        private readonly TextBox _callsign = new TextBox { Text = "NOCALL", Width = 160 };
        private readonly TextBlock _status = new TextBlock { Foreground = (Brush)Application.Current.FindResource("TextSecondary"), Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };

        public PlutoControlWindow()
        {
            Title = "Pluto Control";
            Width = 420; Height = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)Application.Current.FindResource("WindowBackground");
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            var panel = new StackPanel { Margin = new Thickness(16) };

            panel.Children.Add(new TextBlock { Text = "Pluto (F5OEO) transmit control", FontSize = 15, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(new TextBlock
            {
                Text = "Configure the callsign used by the F5OEO Pluto DATV transmitter.",
                Margin = new Thickness(0, 8, 0, 12),
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.FindResource("TextSecondary")
            });

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = "Callsign:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            row.Children.Add(_callsign);
            panel.Children.Add(row);

            var configure = new Button { Content = "Configure Callsign & Reboot", Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            configure.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
            configure.Click += (s, e) => Configure();
            panel.Children.Add(configure);

            panel.Children.Add(_status);
            Content = panel;
        }

        private void Configure()
        {
            try
            {
                var control = new F5OEOPlutoControl(null);
                control.ConfigureCallsignAndReboot(_callsign.Text.Trim());
                _status.Text = "Callsign configured and reboot requested.";
            }
            catch (Exception ex)
            {
                _status.Text = "Configure failed: " + ex.Message;
            }
        }
    }
}
