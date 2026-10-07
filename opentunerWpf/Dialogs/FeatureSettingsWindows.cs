using System;
using System.Windows;
using System.Windows.Controls;
using opentuner.ExtraFeatures.MqttClient;
using opentuner.ExtraFeatures.QuickTuneControl;
using opentuner.ExtraFeatures.DATVReporter;
using opentuner.ExtraFeatures.BATCWebchat;

namespace OpenTuner.Wpf.Dialogs
{
    // ==================================================================
    public class MqttSettingsWindow : Window
    {
        private readonly MqttManagerSettings _s;
        private TextBox _host, _port;

        public MqttSettingsWindow(MqttManagerSettings s)
        {
            _s = s;
            SettingsUi.StyleWindow(this, "MQTT Client Settings", 320);

            StackPanel content;
            Content = SettingsUi.Layout(this, out content, Save);

            StackPanel b;
            var card = SettingsUi.Card("MQTT Broker", out b);
            _host = SettingsUi.Text(_s.MqttBroker);
            SettingsUi.Row(b, "Broker host / IP:", _host);
            _port = SettingsUi.Text(_s.MqttPort.ToString());
            SettingsUi.Row(b, "Broker port:", _port);
            content.Children.Add(card);
        }

        private void Save()
        {
            _s.MqttBroker = _host.Text.Trim();
            if (int.TryParse(_port.Text, out int p)) _s.MqttPort = p;
            DialogResult = true;
            Close();
        }
    }

    // ==================================================================
    public class QuickTuneSettingsWindow : Window
    {
        private readonly QuickTuneControlSettings _s;
        private readonly TextBox[] _ports = new TextBox[4];

        public QuickTuneSettingsWindow(QuickTuneControlSettings s)
        {
            _s = s;
            SettingsUi.StyleWindow(this, "QuickTune Settings", 360);

            StackPanel content;
            Content = SettingsUi.Layout(this, out content, Save);

            StackPanel b;
            var card = SettingsUi.Card("UDP listen ports", out b);
            for (int i = 0; i < 4; i++)
            {
                _ports[i] = SettingsUi.Text(i < _s.UDPListenPorts.Length ? _s.UDPListenPorts[i].ToString() : "0");
                SettingsUi.Row(b, "RX " + (i + 1) + " port:", _ports[i]);
            }
            content.Children.Add(card);
        }

        private void Save()
        {
            var ports = new int[4];
            for (int i = 0; i < 4; i++)
                ports[i] = int.TryParse(_ports[i].Text, out int p) ? p : _s.UDPListenPorts[i];
            _s.UDPListenPorts = ports;
            DialogResult = true;
            Close();
        }
    }

    // ==================================================================
    public class DatvReporterSettingsWindow : Window
    {
        private readonly DATVReporterSettings _s;
        private TextBox _callsign, _grid, _url;

        public DatvReporterSettingsWindow(DATVReporterSettings s)
        {
            _s = s;
            SettingsUi.StyleWindow(this, "DATV Reporter Settings", 360);

            StackPanel content;
            Content = SettingsUi.Layout(this, out content, Save);

            StackPanel b;
            var card = SettingsUi.Card("Reporter", out b);
            _callsign = SettingsUi.Text(_s.callsign);
            SettingsUi.Row(b, "Callsign:", _callsign);
            _grid = SettingsUi.Text(_s.grid_locator);
            SettingsUi.Row(b, "Grid locator:", _grid);
            _url = SettingsUi.Text(_s.service_url);
            SettingsUi.Row(b, "Service URL:", _url);
            content.Children.Add(card);
        }

        private void Save()
        {
            _s.callsign = _callsign.Text.Trim();
            _s.grid_locator = _grid.Text.Trim();
            _s.service_url = _url.Text.Trim();
            DialogResult = true;
            Close();
        }
    }

    // ==================================================================
    public class WebChatSettingsWindow : Window
    {
        private readonly WebChatSettings _s;
        private TextBox _fontSize, _template;
        private CheckBox _autostart, _autologin;

        public WebChatSettingsWindow(WebChatSettings s)
        {
            _s = s;
            SettingsUi.StyleWindow(this, "Web Chat Settings", 420);

            StackPanel content;
            Content = SettingsUi.Layout(this, out content, Save);

            StackPanel b;
            var card = SettingsUi.Card("Chat", out b);
            _fontSize = SettingsUi.Text(_s.chat_font_size.ToString());
            SettingsUi.Row(b, "Chat font size:", _fontSize);

            _template = new TextBox
            {
                Text = _s.sigreport_template,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 60,
                VerticalContentAlignment = VerticalAlignment.Top
            };
            SettingsUi.Row(b, "Signal report template:", _template);

            _autostart = new CheckBox { Content = "Open chat on startup" };
            b.Children.Add(_autostart);
            _autologin = new CheckBox { Content = "Auto login (set nickname)" };
            b.Children.Add(_autologin);
            content.Children.Add(card);

            _autostart.IsChecked = _s.gui_autostart;
            _autologin.IsChecked = _s.gui_autologin;
        }

        private void Save()
        {
            if (int.TryParse(_fontSize.Text, out int fs)) _s.chat_font_size = fs;
            _s.sigreport_template = _template.Text;
            _s.gui_autostart = _autostart.IsChecked == true;
            _s.gui_autologin = _autologin.IsChecked == true;
            DialogResult = true;
            Close();
        }
    }
}
