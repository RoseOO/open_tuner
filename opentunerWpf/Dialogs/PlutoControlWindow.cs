using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using opentuner.MediaSources;
using opentuner.Transmit;
using opentuner.ExtraFeatures.MqttClient;

namespace OpenTuner.Wpf.Dialogs
{
    /// <summary>Native WPF Pluto (F5OEO) transmit control window.</summary>
    public class PlutoControlWindow : Window
    {
        private readonly PlutoTransmitter _tx;

        private TextBlock _vCallsign, _vVersion, _vTemp, _vMode, _vTx, _vFreq, _vGain, _vSr;
        private TextBlock _vFec, _vFecMode, _vConstel, _vFrame, _vPilots, _vTsMode, _vTsSource;
        private TextBlock _status;

        private ComboBox _mode;
        private TextBox _freq, _gain, _sr, _host, _port, _callsign;
        private CheckBox _pilots;

        public PlutoControlWindow(MqttManager mqtt, OTSource source = null)
        {
            _tx = new PlutoTransmitter(mqtt);
            _tx.StateChanged += OnStateChanged;

            Title = LocalizationManager.Get("dt.pluto");
            Width = 520; Height = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)Application.Current.FindResource("WindowBackground");
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) };
            var content = new StackPanel();

            content.Children.Add(new TextBlock
            {
                Text = "Pluto (F5OEO) DATV transmit control via MQTT.",
                Margin = new Thickness(0, 0, 0, 12),
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.FindResource("TextSecondary")
            });

            // status card
            StackPanel st;
            var stCard = SettingsUi.Card("Status", out st);
            _vCallsign = AddReadout(st, "Callsign");
            _vVersion = AddReadout(st, "Version");
            _vTemp = AddReadout(st, "Temperature");
            _vMode = AddReadout(st, "Mode");
            _vTx = AddReadout(st, "Transmitting");
            _vFreq = AddReadout(st, "Frequency (Hz)");
            _vGain = AddReadout(st, "Gain");
            _vSr = AddReadout(st, "Symbol rate");
            _vFec = AddReadout(st, "FEC");
            _vFecMode = AddReadout(st, "FEC mode");
            _vConstel = AddReadout(st, "Constellation");
            _vFrame = AddReadout(st, "Frame");
            _vPilots = AddReadout(st, "Pilots");
            _vTsMode = AddReadout(st, "TS source mode");
            _vTsSource = AddReadout(st, "TS source address");
            content.Children.Add(stCard);

            // control card
            StackPanel ct;
            var ctCard = SettingsUi.Card("Control", out ct);

            _mode = SettingsUi.Combo("Passthrough", "DVBS2 TS", "DVBS2 GSE", "Test Tone");
            _mode.SelectedIndex = 1;
            SettingsUi.Row(ct, "Hardware mode:", _mode);
            AddApply(ct, "Apply mode", () => _tx.SetHardwareMode(_mode.SelectedIndex));

            _freq = SettingsUi.Text("");
            SettingsUi.Row(ct, "Frequency (Hz):", _freq);
            AddApply(ct, "Set frequency", () =>
            {
                if (int.TryParse(_freq.Text, out int f)) _tx.SetFrequency(f);
                else _status.Text = "Invalid frequency.";
            });

            _gain = SettingsUi.Text("");
            SettingsUi.Row(ct, "Gain (dB):", _gain);
            AddApply(ct, "Set gain", () =>
            {
                if (double.TryParse(_gain.Text, out double g)) _tx.SetGain(g);
                else _status.Text = "Invalid gain.";
            });

            _sr = SettingsUi.Text("");
            SettingsUi.Row(ct, "Symbol rate (k):", _sr);
            AddApply(ct, "Set symbol rate", () =>
            {
                if (int.TryParse(_sr.Text, out int s)) _tx.SetSymbolRate(s);
                else _status.Text = "Invalid symbol rate.";
            });

            _pilots = new CheckBox { Content = "Pilots on" };
            ct.Children.Add(_pilots);
            AddApply(ct, "Apply pilots", () => _tx.SetPilots(_pilots.IsChecked == true));

            content.Children.Add(ctCard);

            // TS routing card
            StackPanel rt;
            var rtCard = SettingsUi.Card("Route local TS to Pluto (UDP)", out rt);
            _host = SettingsUi.Text("127.0.0.1");
            SettingsUi.Row(rt, "UDP host:", _host);
            _port = SettingsUi.Text("5000");
            SettingsUi.Row(rt, "UDP port:", _port);
            AddApply(rt, "Route TS", () =>
            {
                if (int.TryParse(_port.Text, out int p))
                    _tx.RouteTsFromUdp(_host.Text.Trim(), p);
                else
                    _status.Text = "Invalid port.";
            });
            content.Children.Add(rtCard);

            // callsign card
            StackPanel cc;
            var ccCard = SettingsUi.Card("Callsign", out cc);
            _callsign = SettingsUi.Text("NOCALL");
            SettingsUi.Row(cc, "Callsign:", _callsign);
            AddApply(cc, "Configure & reboot", () => _tx.ConfigureCallsignAndReboot(_callsign.Text.Trim()));
            content.Children.Add(ccCard);

            _status = new TextBlock
            {
                Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            content.Children.Add(_status);

            scroll.Content = content;
            Content = scroll;

            Closed += (s, e) => _tx.Close();
        }

        private TextBlock AddReadout(Panel panel, string label)
        {
            var value = new TextBlock
            {
                Text = "-",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.FindResource("TextPrimary")
            };
            SettingsUi.Row(panel, label + ":", value);
            return value;
        }

        private void AddApply(Panel panel, string label, Action action)
        {
            var btn = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 4) };
            btn.Click += (s, e) =>
            {
                try { action(); _status.Text = label + " sent."; }
                catch (Exception ex) { _status.Text = label + " failed: " + ex.Message; }
            };
            panel.Children.Add(btn);
        }

        private void OnStateChanged(PlutoState s)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnStateChanged(s)));
                return;
            }

            _vCallsign.Text = s.callsign;
            _vVersion.Text = s.version;
            _vTemp.Text = s.temperature;
            _vMode.Text = s.mode;
            _vTx.Text = s.transmitting ? "On" : "Off";
            _vFreq.Text = s.frequency.ToString("0");
            _vGain.Text = s.gain.ToString("0.##") + " dB";
            _vSr.Text = s.symbol_rate;
            _vFec.Text = s.fec;
            _vFecMode.Text = s.fec_mode;
            _vConstel.Text = s.constel;
            _vFrame.Text = s.frame;
            _vPilots.Text = s.pilots ? "On" : "Off";
            _vTsMode.Text = s.ts_source_mode;
            _vTsSource.Text = s.ts_source_address;

            if (!s.detected)
                _status.Text = "Waiting for a Pluto on MQTT (dt/pluto/#)...";
        }
    }
}
