using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using opentuner.MediaSources;

namespace OpenTuner.Wpf.Dialogs
{
    /// <summary>Native WPF frequency entry / tuner control (replaces tunerControlForm usage).</summary>
    public class TuneWindow : Window
    {
        private readonly OTSource _source;

        private ComboBox _rx;
        private TextBox _freqMHz;
        private TextBox _symbolRate;
        private ComboBox _rfInput;

        private readonly int _initialRx;

        public TuneWindow(OTSource source, int initialRx = 0)
        {
            _source = source;
            _initialRx = initialRx;

            Title = LocalizationManager.Get("dt.tune");
            Width = 360; Height = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)Application.Current.FindResource("WindowBackground");
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            BuildUi();

            if (_initialRx >= 0 && _initialRx < _rx.Items.Count)
                _rx.SelectedIndex = _initialRx;
        }

        private void BuildUi()
        {
            var dock = new DockPanel();

            var buttons = new Border
            {
                Background = (Brush)Application.Current.FindResource("SurfaceBackground"),
                BorderBrush = (Brush)Application.Current.FindResource("Border"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 10, 16, 10)
            };
            DockPanel.SetDock(buttons, Dock.Bottom);

            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var tune = new Button { Content = "Tune", Width = 96, Margin = new Thickness(0, 0, 10, 0) };
            tune.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
            tune.Click += (s, e) => { ApplyTune(); DialogResult = true; };
            var close = new Button { Content = LocalizationManager.Get("btn.close"), Width = 96 };
            close.Click += (s, e) => { DialogResult = false; Close(); };
            sp.Children.Add(tune);
            sp.Children.Add(close);
            buttons.Child = sp;
            dock.Children.Add(buttons);

            var panel = new StackPanel { Margin = new Thickness(16) };

            _rx = new ComboBox();
            int count = _source != null ? Math.Max(1, _source.GetVideoSourceCount()) : 2;
            for (int i = 0; i < count; i++) _rx.Items.Add("RX " + (i + 1));
            _rx.SelectedIndex = 0;
            AddRow(panel, "Receiver", _rx);

            _freqMHz = new TextBox { Text = "10491.5000" };
            AddRow(panel, "Frequency (MHz)", _freqMHz);

            _symbolRate = new TextBox { Text = "1500" };
            AddRow(panel, "Symbol rate (ksym)", _symbolRate);

            _rfInput = new ComboBox();
            _rfInput.Items.Add("A");
            _rfInput.Items.Add("B");
            _rfInput.SelectedIndex = 0;
            AddRow(panel, "RF input", _rfInput);

            dock.Children.Add(panel);
            Content = dock;
        }

        private void AddRow(Panel panel, string label, FrameworkElement control)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lbl = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.FindResource("TextSecondary")
            };
            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(control, 1);
            g.Children.Add(lbl);
            g.Children.Add(control);
            panel.Children.Add(g);
        }

        private void ApplyTune()
        {
            if (_source == null)
                return;

            int rx = Math.Max(0, _rx.SelectedIndex);

            if (!double.TryParse(_freqMHz.Text, System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out double mhz))
                return;

            uint sr = 1500;
            uint.TryParse(_symbolRate.Text, out sr);
            if (sr == 0) sr = 1500;

            _source.SetFrequency(rx, (uint)Math.Round(mhz * 1000), sr, true);
        }
    }
}
