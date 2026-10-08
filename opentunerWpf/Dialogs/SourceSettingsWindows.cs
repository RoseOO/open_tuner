using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using opentuner.MediaSources.Minitiouner;
using opentuner.MediaSources.WinterHill;
using opentuner.MediaSources.Longmynd;

namespace OpenTuner.Wpf.Dialogs
{
    /// <summary>Shared helpers for the source settings windows.</summary>
    internal static class SettingsUi
    {
        public static void StyleWindow(Window w, string title, double height)
        {
            w.Title = title;
            w.Width = 480;
            w.Height = height;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            w.Background = (Brush)Application.Current.FindResource("WindowBackground");
            w.FontFamily = new FontFamily("Segoe UI");
            w.FontSize = 13;
        }

        public static Border Card(string title, out StackPanel body)
        {
            body = new StackPanel();
            var card = new Border
            {
                Background = (Brush)Application.Current.FindResource("SurfaceBackground"),
                BorderBrush = (Brush)Application.Current.FindResource("Border"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 12),
                Child = body
            };
            body.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
                Margin = new Thickness(0, 0, 0, 8)
            });
            return card;
        }

        public static void Row(Panel panel, string label, FrameworkElement control)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.FindResource("TextSecondary")
            };
            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(control, 1);
            g.Children.Add(lbl);
            g.Children.Add(control);
            panel.Children.Add(g);
        }

        public static DockPanel Layout(Window w, out StackPanel content, Action onSave)
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
            var cancel = new Button { Content = LocalizationManager.Get("btn.cancel"), Width = 96, Margin = new Thickness(0, 0, 10, 0) };
            cancel.Click += (s, e) => { w.DialogResult = false; w.Close(); };
            var save = new Button { Content = LocalizationManager.Get("btn.save"), Width = 96 };
            save.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
            save.Click += (s, e) => onSave();
            sp.Children.Add(cancel);
            sp.Children.Add(save);
            buttons.Child = sp;
            dock.Children.Add(buttons);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) };
            content = new StackPanel();
            scroll.Content = content;
            dock.Children.Add(scroll);

            return dock;
        }

        public static ComboBox Combo(params string[] items)
        {
            var c = new ComboBox();
            foreach (var i in items) c.Items.Add(i);
            return c;
        }

        public static TextBox Text(string value) => new TextBox { Text = value ?? "" };
    }

    // ==================================================================
    public class MinitiounerSettingsWindow : Window
    {
        private readonly MinitiounerSettings _s;

        private ComboBox comboInterface;
        private TextBox txtIp;
        private CheckBox chkAutoReconnect;
        private TextBox txtOffset1, txtOffset2;
        private ComboBox comboSupplyA, comboSupplyB, comboRfInput;

        public MinitiounerSettingsWindow(MinitiounerSettings settings)
        {
            _s = settings;
            SettingsUi.StyleWindow(this, "Minitiouner Settings", 560);

            StackPanel content;
            Content = SettingsUi.Layout(this, out content, Save);

            StackPanel hw;
            var hwCard = SettingsUi.Card("Hardware Interface", out hw);
            comboInterface = SettingsUi.Combo("Always Ask", "FTDI Module", "PicoTuner");
            comboInterface.SelectedIndex = Math.Max(0, Math.Min(2, (int)_s.DefaultInterface));
            SettingsUi.Row(hw, "Default Interface:", comboInterface);
            txtIp = SettingsUi.Text("");
            txtIp.IsEnabled = false;
            SettingsUi.Row(hw, "IP Address:", txtIp);
            chkAutoReconnect = new CheckBox { Content = "Auto reconnect PicoTuner (restore tuning)" };
            hw.Children.Add(chkAutoReconnect);
            content.Children.Add(hwCard);

            StackPanel tp;
            var tpCard = SettingsUi.Card("Tuner Properties", out tp);
            txtOffset1 = SettingsUi.Text(_s.Offset1.ToString());
            SettingsUi.Row(tp, "Tuner 1 Freq Offset:", txtOffset1);
            txtOffset2 = SettingsUi.Text(_s.Offset2.ToString());
            SettingsUi.Row(tp, "Tuner 2 Freq Offset:", txtOffset2);
            comboSupplyA = SettingsUi.Combo("Off", "13V Vertical", "18V Horizontal");
            comboSupplyA.SelectedIndex = Math.Max(0, Math.Min(2, (int)_s.DefaultLnbASupply));
            SettingsUi.Row(tp, "LNB A Supply Default:", comboSupplyA);
            comboSupplyB = SettingsUi.Combo("Off", "13V Vertical", "18V Horizontal");
            comboSupplyB.SelectedIndex = Math.Max(0, Math.Min(2, (int)_s.DefaultLnbBSupply));
            SettingsUi.Row(tp, "LNB B Supply Default:", comboSupplyB);
            comboRfInput = SettingsUi.Combo("Tuner 1 = A, Tuner 2 = A", "Tuner 1 = A, Tuner 2 = B", "Tuner 1 = B, Tuner 2 = A", "Tuner 1 = B, Tuner 2 = B");
            comboRfInput.SelectedIndex = Math.Max(0, Math.Min(3, (int)_s.DefaultRFInput));
            SettingsUi.Row(tp, "Default RF Input:", comboRfInput);
            content.Children.Add(tpCard);

            chkAutoReconnect.IsChecked = _s.AutoReconnect;
        }

        private void Save()
        {
            if (!uint.TryParse(txtOffset1.Text, out uint o1)) { MessageBox.Show("Invalid Offset 1"); return; }
            if (!uint.TryParse(txtOffset2.Text, out uint o2)) { MessageBox.Show("Invalid Offset 2"); return; }

            _s.DefaultInterface = (byte)comboInterface.SelectedIndex;
            _s.DefaultLnbASupply = (byte)comboSupplyA.SelectedIndex;
            _s.DefaultLnbBSupply = (byte)comboSupplyB.SelectedIndex;
            _s.DefaultRFInput = (byte)comboRfInput.SelectedIndex;
            _s.Offset1 = o1;
            _s.Offset2 = o2;
            _s.AutoReconnect = chkAutoReconnect.IsChecked == true;

            DialogResult = true;
            Close();
        }
    }

    // ==================================================================
    public class WinterhillSettingsWindow : Window
    {
        private readonly WinterHillSettings _s;

        private ComboBox comboInterface;
        private TextBox txtUdpIp, txtUdpBasePort;
        private CheckBox chkAutoFind, chkAutoReconnect;
        private TextBox txtWsIp, txtWsPort, txtWsBaseUdp;

        public WinterhillSettingsWindow(WinterHillSettings settings)
        {
            _s = settings;
            SettingsUi.StyleWindow(this, "WinterHill Settings", 620);

            StackPanel content;
            Content = SettingsUi.Layout(this, out content, Save);

            StackPanel gen;
            var genCard = SettingsUi.Card("General", out gen);
            comboInterface = SettingsUi.Combo("Always Ask", "Websocket (ZR6TG)", "PicoTuner Ethernet (G4EWJ)");
            comboInterface.SelectedIndex = Math.Max(0, Math.Min(2, (int)_s.DefaultInterface));
            SettingsUi.Row(gen, "Default Interface:", comboInterface);
            content.Children.Add(genCard);

            StackPanel eth;
            var ethCard = SettingsUi.Card("WinterHill (PicoTuner Ethernet)", out eth);
            txtUdpIp = SettingsUi.Text(_s.WinterHillUdpHost);
            SettingsUi.Row(eth, "IP Address:", txtUdpIp);
            txtUdpBasePort = SettingsUi.Text(_s.WinterHillUdpBasePort.ToString());
            SettingsUi.Row(eth, "Udp Base Port:", txtUdpBasePort);
            chkAutoFind = new CheckBox { Content = "Auto-find on connect" };
            eth.Children.Add(chkAutoFind);
            chkAutoReconnect = new CheckBox { Content = "Auto reconnect and restore tuning" };
            eth.Children.Add(chkAutoReconnect);
            content.Children.Add(ethCard);

            StackPanel ws;
            var wsCard = SettingsUi.Card("WinterHill WS Settings", out ws);
            txtWsIp = SettingsUi.Text(_s.WinterHillWSHost);
            SettingsUi.Row(ws, "WinterHill WS IP:", txtWsIp);
            txtWsPort = SettingsUi.Text(_s.WinterHillWSPort.ToString());
            SettingsUi.Row(ws, "WinterHill WS Port:", txtWsPort);
            txtWsBaseUdp = SettingsUi.Text(_s.WinterHillWSUdpBasePort.ToString());
            SettingsUi.Row(ws, "WinterHill Udp Port:", txtWsBaseUdp);

            var broadcast = new Button { Content = "PicoTuner (WH) Broadcast Listener", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            broadcast.Click += (s, e) => { new BroadcastListenerWindow { Owner = this }.Show(); };
            ws.Children.Add(broadcast);
            content.Children.Add(wsCard);

            chkAutoFind.IsChecked = _s.AutoFindUdp;
            chkAutoReconnect.IsChecked = _s.AutoReconnect;
        }

        private void Save()
        {
            if (!int.TryParse(txtWsPort.Text, out int wsport)) { MessageBox.Show("WinterHill WS Port is not valid."); return; }
            if (!int.TryParse(txtWsBaseUdp.Text, out int baseport)) { MessageBox.Show("WinterHill WS Base Port is not valid"); return; }
            if (!int.TryParse(txtUdpBasePort.Text, out int udpbaseport)) { MessageBox.Show("WinterHill UDP Base Port is not valid"); return; }

            _s.DefaultInterface = (byte)comboInterface.SelectedIndex;
            _s.WinterHillUdpHost = txtUdpIp.Text.Trim();
            _s.WinterHillUdpBasePort = udpbaseport;
            _s.AutoFindUdp = chkAutoFind.IsChecked == true;
            _s.AutoReconnect = chkAutoReconnect.IsChecked == true;
            _s.WinterHillWSHost = txtWsIp.Text.Trim();
            _s.WinterHillWSPort = wsport;
            _s.WinterHillWSUdpBasePort = baseport;

            DialogResult = true;
            Close();
        }
    }

    // ==================================================================
    public class LongmyndSettingsWindow : Window
    {
        private readonly LongmyndSettings _s;

        private ComboBox comboInterface;
        private TextBox txtTsPort, txtWsIp, txtWsPort, txtMqttIp, txtMqttPort, txtCmdTopic, txtOffset1;

        public LongmyndSettingsWindow(LongmyndSettings settings)
        {
            _s = settings;
            SettingsUi.StyleWindow(this, "Longmynd Settings", 620);

            StackPanel content;
            Content = SettingsUi.Layout(this, out content, Save);

            StackPanel hw;
            var hwCard = SettingsUi.Card("Hardware Interface", out hw);
            comboInterface = SettingsUi.Combo("Websocket (M0DNY)", "Mqtt (F5OEO)");
            comboInterface.SelectedIndex = Math.Max(0, Math.Min(1, (int)_s.DefaultInterface));
            SettingsUi.Row(hw, "Default Interface:", comboInterface);
            txtTsPort = SettingsUi.Text(_s.TS_Port.ToString());
            SettingsUi.Row(hw, "TS Port:", txtTsPort);
            txtWsIp = SettingsUi.Text(_s.LongmyndWSHost);
            SettingsUi.Row(hw, "IP Address (WS):", txtWsIp);
            txtWsPort = SettingsUi.Text(_s.LongmyndWSPort.ToString());
            SettingsUi.Row(hw, "Port (WS):", txtWsPort);
            txtMqttIp = SettingsUi.Text(_s.LongmyndMqttHost);
            SettingsUi.Row(hw, "IP Address (MQTT):", txtMqttIp);
            txtMqttPort = SettingsUi.Text(_s.LongmyndMqttPort.ToString());
            SettingsUi.Row(hw, "Port (MQTT):", txtMqttPort);
            txtCmdTopic = SettingsUi.Text(_s.CmdTopic);
            SettingsUi.Row(hw, "MQTT Base CMD Topic:", txtCmdTopic);
            content.Children.Add(hwCard);

            StackPanel tp;
            var tpCard = SettingsUi.Card("Tuner Properties", out tp);
            txtOffset1 = SettingsUi.Text(_s.Offset1.ToString());
            SettingsUi.Row(tp, "Tuner 1 Freq Offset:", txtOffset1);
            content.Children.Add(tpCard);
        }

        private void Save()
        {
            if (!uint.TryParse(txtOffset1.Text, out uint offset)) { MessageBox.Show("Invalid Offset"); return; }
            if (!int.TryParse(txtWsPort.Text, out int wsport)) { MessageBox.Show("Invalid WS Port"); return; }
            if (!int.TryParse(txtMqttPort.Text, out int mqttport)) { MessageBox.Show("Invalid Mqtt Port"); return; }
            if (!int.TryParse(txtTsPort.Text, out int tsport)) { MessageBox.Show("Invalid TS Port"); return; }

            _s.DefaultInterface = (byte)comboInterface.SelectedIndex;
            _s.TS_Port = tsport;
            _s.LongmyndWSHost = txtWsIp.Text.Trim();
            _s.LongmyndWSPort = wsport;
            _s.LongmyndMqttHost = txtMqttIp.Text.Trim();
            _s.LongmyndMqttPort = mqttport;
            _s.Offset1 = offset;
            _s.CmdTopic = txtCmdTopic.Text;

            DialogResult = true;
            Close();
        }
    }
}

