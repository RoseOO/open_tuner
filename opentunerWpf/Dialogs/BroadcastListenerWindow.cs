using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using opentuner.MediaSources.WinterHill;

namespace OpenTuner.Wpf.Dialogs
{
    /// <summary>Native WPF PicoTuner (WH) broadcast listener (replaces PicoWHBroadcastListenerForm).</summary>
    public class BroadcastListenerWindow : Window
    {
        private PicoWHBroadcastListener _listener;
        private readonly ListBox _list = new ListBox();
        private readonly TextBlock _ip = new TextBlock { Text = "Unknown", FontWeight = FontWeights.SemiBold };
        private readonly TextBlock _port = new TextBlock { Text = "Unknown", FontWeight = FontWeights.SemiBold };
        private readonly TextBox _newBasePort = new TextBox { Text = "9900", Width = 70 };
        private readonly UdpClient _udp = new UdpClient();

        public BroadcastListenerWindow()
        {
            Title = LocalizationManager.Get("dt.broadcast");
            Width = 620; Height = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)Application.Current.FindResource("WindowBackground");
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            BuildUi();

            Loaded += (s, e) => Start();
            Closed += (s, e) => Stop();
        }

        private void BuildUi()
        {
            var dock = new DockPanel();

            var top = new Border
            {
                Background = (Brush)Application.Current.FindResource("SurfaceBackground"),
                BorderBrush = (Brush)Application.Current.FindResource("Border"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 10, 16, 10)
            };
            DockPanel.SetDock(top, Dock.Top);

            // columns: label, value, copy, label, value, copy, spacer
            var g = new Grid();
            for (int i = 0; i < 6; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var l1 = new TextBlock { Text = "Detected IP:", VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.FindResource("TextSecondary"), Margin = new Thickness(0, 0, 6, 0) };
            Grid.SetColumn(l1, 0);

            _ip.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(_ip, 1);

            var copyIp = new Button { Content = "Copy", Padding = new Thickness(8, 2, 8, 2) };
            copyIp.Click += (s, e) => { try { Clipboard.SetText(_ip.Text); } catch { } };
            Grid.SetColumn(copyIp, 2);

            var l2 = new TextBlock { Text = "Base port:", VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.FindResource("TextSecondary"), Margin = new Thickness(24, 0, 6, 0) };
            Grid.SetColumn(l2, 3);

            _port.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(_port, 4);

            var copyPort = new Button { Content = "Copy", Padding = new Thickness(8, 2, 8, 2) };
            copyPort.Click += (s, e) => { try { Clipboard.SetText(_port.Text); } catch { } };
            Grid.SetColumn(copyPort, 5);

            g.Children.Add(l1);
            g.Children.Add(_ip);
            g.Children.Add(copyIp);
            g.Children.Add(l2);
            g.Children.Add(_port);
            g.Children.Add(copyPort);

            var cmds = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            cmds.Children.Add(new TextBlock { Text = "Remote:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Foreground = (Brush)Application.Current.FindResource("TextSecondary") });
            cmds.Children.Add(RemoteButton("Reset", "[to@wh] reset=147"));
            cmds.Children.Add(RemoteButton("Reboot", "[to@wh] reboot=258"));
            cmds.Children.Add(RemoteButton("Bootsel", "[to@wh] bootsel=369"));
            cmds.Children.Add(new TextBlock { Text = "Base port:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 6, 0), Foreground = (Brush)Application.Current.FindResource("TextSecondary") });
            cmds.Children.Add(_newBasePort);
            var change = new Button { Content = "Change", Margin = new Thickness(6, 0, 0, 0) };
            change.Click += (s, e) => SendRemoteCommand("[to@wh] bip=" + _newBasePort.Text.Trim());
            cmds.Children.Add(change);

            var topStack = new StackPanel();
            topStack.Children.Add(g);
            topStack.Children.Add(cmds);
            top.Child = topStack;
            dock.Children.Add(top);

            var bottom = new Border { Padding = new Thickness(16, 10, 16, 10) };
            DockPanel.SetDock(bottom, Dock.Bottom);
            var close = new Button { Content = LocalizationManager.Get("btn.close"), Width = 90, HorizontalAlignment = HorizontalAlignment.Right };
            close.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
            close.Click += (s, e) => Close();
            bottom.Child = close;
            dock.Children.Add(bottom);

            _list.Margin = new Thickness(16);
            _list.ItemsSource = null;
            dock.Children.Add(_list);

            Content = dock;
        }

        private void Start()
        {
            try
            {
                _listener = new PicoWHBroadcastListener();
                _listener.OnBroadcast += OnBroadcast;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error opening the broadcast listener - make sure you don't already have one running.\n" + ex.Message,
                                "Open Tuner", MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
            }
        }

        private void Stop()
        {
            try { _listener?.Close(); } catch { }
            try { _udp?.Close(); } catch { }
            _listener = null;
        }

        private Button RemoteButton(string text, string command)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0) };
            b.Click += (s, e) => SendRemoteCommand(command);
            return b;
        }

        private void SendRemoteCommand(string command)
        {
            int baseport = 9900;
            int.TryParse(_port.Text, out baseport);
            baseport = baseport / 100 * 100 + 20;

            try
            {
                var ep = new IPEndPoint(IPAddress.Parse(_ip.Text), baseport);
                byte[] outStream = Encoding.ASCII.GetBytes(command);
                _udp.Client.SendTo(outStream, ep);
                _list.Items.Add(DateTime.Now.ToShortTimeString() + " : Sent " + command);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error sending command: " + ex.Message, "Open Tuner");
            }
        }

        private void OnBroadcast(string data)
        {
            Dispatcher.Invoke(() =>
            {
                foreach (string raw in data.Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;

                    _list.Items.Add(DateTime.Now.ToShortTimeString() + " : " + line);
                    if (_list.Items.Count > 1000) _list.Items.RemoveAt(0);

                    if (line.IndexOf("IP address", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(line, @"\b(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\b");
                        if (m.Success) _ip.Text = m.Groups[1].Value;
                    }
                    else if (line.IndexOf("Base IP port", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(line, @"\b(\d{2,5})\b");
                        if (m.Success) _port.Text = m.Groups[1].Value;
                    }
                }

                if (_list.Items.Count > 0) _list.ScrollIntoView(_list.Items[_list.Items.Count - 1]);
            });
        }
    }
}
