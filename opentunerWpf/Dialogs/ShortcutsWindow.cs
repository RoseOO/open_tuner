using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenTuner.Wpf.Dialogs
{
    public class ShortcutsWindow : Window
    {
        public ShortcutsWindow()
        {
            SettingsUi.StyleWindow(this, LocalizationManager.Get("dt.shortcuts"), 470);
            Width = 460;

            var entries = new (string key, string action)[]
            {
                ("F11", "Toggle full screen"),
                ("Esc", "Leave full screen"),
                ("1 / 2 / 3 / 4", "Full screen the matching video (double-click video too)"),
                ("R", "Toggle recording on all tuners"),
                ("U", "Toggle UDP streaming on all tuners"),
                ("M", "Mute/unmute the focused tuner"),
                ("S", "Take a snapshot"),
                ("Mouse wheel", "Adjust volume over a video"),
                ("F1", "Show this shortcut list")
            };

            var root = new DockPanel();

            var close = new Border
            {
                Background = (Brush)Application.Current.FindResource("SurfaceBackground"),
                BorderBrush = (Brush)Application.Current.FindResource("Border"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 10, 16, 10)
            };
            DockPanel.SetDock(close, Dock.Bottom);
            var closeBtn = new Button { Content = LocalizationManager.Get("btn.close"), Width = 96, HorizontalAlignment = HorizontalAlignment.Right };
            closeBtn.Click += (s, e) => Close();
            close.Child = closeBtn;
            root.Children.Add(close);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) };
            var sp = new StackPanel();
            scroll.Content = sp;
            root.Children.Add(scroll);

            foreach (var (key, action) in entries)
            {
                var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var k = new TextBlock
                {
                    Text = key,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.FindResource("TextPrimary")
                };
                var a = new TextBlock
                {
                    Text = action,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)Application.Current.FindResource("TextSecondary")
                };
                Grid.SetColumn(k, 0);
                Grid.SetColumn(a, 1);
                g.Children.Add(k);
                g.Children.Add(a);
                sp.Children.Add(g);
            }

            Content = root;
        }
    }
}
