using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using opentuner.MediaSources;
using opentuner.Utilities;

namespace OpenTuner.Wpf.Dialogs
{
    /// <summary>
    /// Lists the QO-100 bandplan (from extra/bandplan.xml) with one-click tuning.
    /// Beacons are highlighted and listed first.
    /// </summary>
    public class BeaconWindow : Window
    {
        private readonly OTSource _source;
        private readonly Bandplan _bandplan;
        private ListBox _list;
        private TextBlock _detail;

        public BeaconWindow(OTSource source)
        {
            _source = source;
            _bandplan = Bandplan.Load(Bandplan.DefaultPath);

            Title = LocalizationManager.Get("dt.beacons");
            Width = 520; Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)Application.Current.FindResource("WindowBackground");
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            var dock = new DockPanel();

            var buttons = new Border
            {
                Background = (Brush)Application.Current.FindResource("SurfaceBackground"),
                BorderBrush = (Brush)Application.Current.FindResource("Border"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 10, 16, 10)
            };
            DockPanel.SetDock(buttons, Dock.Bottom);
            var btnSp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var tune = new Button { Content = "Tune selected", Width = 130, Margin = new Thickness(0, 0, 10, 0) };
            tune.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
            tune.Click += (s, e) => Tune();
            var close = new Button { Content = LocalizationManager.Get("btn.close"), Width = 96 };
            close.Click += (s, e) => Close();
            btnSp.Children.Add(tune);
            btnSp.Children.Add(close);
            buttons.Child = btnSp;
            dock.Children.Add(buttons);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) };
            var content = new StackPanel();
            scroll.Content = content;
            dock.Children.Add(scroll);

            StackPanel b;
            var card = SettingsUi.Card("Channels (double-click to tune)", out b);
            _list = new ListBox { Height = 360 };
            _list.MouseDoubleClick += (s, e) => Tune();
            _list.SelectionChanged += (s, e) => ShowDetail();
            b.Children.Add(_list);
            content.Children.Add(card);

            _detail = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 6, 4, 0),
                Foreground = (Brush)Application.Current.FindResource("TextSecondary")
            };
            content.Children.Add(_detail);

            Content = dock;
            Populate();
        }

        private void Populate()
        {
            var ordered = new List<BandplanChannel>(_bandplan.Channels);
            ordered.Sort((a, b) =>
            {
                if (a.IsBeacon != b.IsBeacon)
                    return a.IsBeacon ? -1 : 1;
                return a.RxFreqMHz.CompareTo(b.RxFreqMHz);
            });

            foreach (var c in ordered)
            {
                string label = (c.IsBeacon ? "* " : "") + c.ToString();
                _list.Items.Add(new ListBoxItem { Content = label, Tag = c });
            }

            if (_list.Items.Count > 0)
                _list.SelectedIndex = 0;
            else
                _detail.Text = "No bandplan loaded (extra/bandplan.xml missing?).";
        }

        private void ShowDetail()
        {
            if (!(_list.SelectedItem is ListBoxItem item) || !(item.Tag is BandplanChannel c))
                return;

            _detail.Text =
                "RX: " + c.RxFreqMHz.ToString("0.####") + " MHz    " +
                "SYM: " + c.SymbolRate + " k" +
                (c.TxFreqMHz > 0 ? "    TX: " + c.TxFreqMHz.ToString("0.####") + " MHz" : "");
        }

        private void Tune()
        {
            if (!(_list.SelectedItem is ListBoxItem item) || !(item.Tag is BandplanChannel c))
                return;

            if (_source == null)
            {
                MessageBox.Show("Connect to a source first.", "Open Tuner");
                return;
            }

            try
            {
                uint freqKhz = (uint)Math.Round(c.RxFreqMHz * 1000.0);
                _source.SetFrequency(0, freqKhz, c.SymbolRate, true);
                ToastService.Show("Tuned to " + c.RxFreqMHz.ToString("0.####") + " MHz" + (string.IsNullOrEmpty(c.Name) ? "" : " (" + c.Name + ")"), ToastKind.Success, 3);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Tune failed: " + ex.Message, "Open Tuner");
            }
        }
    }
}
