using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SocketIOClient;
using Newtonsoft.Json.Linq;
using opentuner.ExtraFeatures.BATCWebchat;
using opentuner.MediaSources;

namespace OpenTuner.Wpf.Dialogs
{
    /// <summary>Native WPF QO-100 wideband web chat (replaces WebChatForm/wbchat).</summary>
    public class WebChatWindow : Window
    {
        private readonly WebChatSettings _settings;
        private readonly OTSource _source;

        private RichTextBox _chat;
        private ListBox _users;
        private TextBox _nick;
        private TextBox _message;
        private TextBlock _connected;
        private Button[] _sigReport = new Button[4];

        private SocketIO _client;

        private volatile bool _closing = false;
        private int _reconnectAttempt = 0;
        private readonly object _reconnectLock = new object();
        private bool _reconnecting = false;

        public WebChatWindow(WebChatSettings settings, OTSource source)
        {
            _settings = settings;
            _source = source;

            Title = LocalizationManager.Get("dt.webchat");
            Width = 720; Height = 520;
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
                Padding = new Thickness(12, 8, 12, 8)
            };
            DockPanel.SetDock(top, Dock.Top);
            var topSp = new StackPanel { Orientation = Orientation.Horizontal };
            topSp.Children.Add(new TextBlock { Text = "Nick:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            _nick = new TextBox { Width = 120 };
            topSp.Children.Add(_nick);
            var setNick = new Button { Content = "Set Nick", Margin = new Thickness(8, 0, 0, 0) };
            setNick.Click += (s, e) => SetNick();
            topSp.Children.Add(setNick);
            var stay = new CheckBox { Content = "Stay on top", Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            stay.Checked += (s, e) => Topmost = true;
            stay.Unchecked += (s, e) => Topmost = false;
            topSp.Children.Add(stay);
            _connected = new TextBlock { Text = "Connected: False", Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.FindResource("TextSecondary") };
            topSp.Children.Add(_connected);
            top.Child = topSp;
            dock.Children.Add(top);

            var bottom = new Border
            {
                Background = (Brush)Application.Current.FindResource("SurfaceBackground"),
                BorderBrush = (Brush)Application.Current.FindResource("Border"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 8, 12, 8)
            };
            DockPanel.SetDock(bottom, Dock.Bottom);
            var bottomSp = new StackPanel { Orientation = Orientation.Horizontal };
            _message = new TextBox { Width = 420 };
            _message.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) SendMessage(); };
            bottomSp.Children.Add(_message);
            var send = new Button { Content = "Send", Margin = new Thickness(8, 0, 0, 0) };
            send.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
            send.Click += (s, e) => SendMessage();
            bottomSp.Children.Add(send);

            int count = _source != null ? _source.GetVideoSourceCount() : 1;
            for (int i = 0; i < Math.Min(4, Math.Max(1, count)); i++)
            {
                int idx = i;
                _sigReport[i] = new Button { Content = "Sig RX" + (i + 1), Margin = new Thickness(8, 0, 0, 0) };
                _sigReport[i].Click += (s, e) => SignalReport(idx);
                bottomSp.Children.Add(_sigReport[i]);
            }
            bottom.Child = bottomSp;
            dock.Children.Add(bottom);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });

            _chat = new RichTextBox
            {
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = new SolidColorBrush(Color.FromRgb(63, 70, 76)),
                Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204)),
                FontFamily = new FontFamily("Consolas"),
                Margin = new Thickness(8)
            };
            Grid.SetColumn(_chat, 0);
            grid.Children.Add(_chat);

            _users = new ListBox { Margin = new Thickness(0, 8, 8, 8) };
            _users.MouseDoubleClick += (s, e) =>
            {
                if (_users.SelectedItem is string nick)
                    _message.Text += " @" + nick + " ";
            };
            Grid.SetColumn(_users, 1);
            grid.Children.Add(_users);

            dock.Children.Add(grid);
            Content = dock;
        }

        private async void Start()
        {
            _nick.Text = string.IsNullOrEmpty(_settings.nickname) ? "NONICK" : _settings.nickname;

            _client = new SocketIO("https://eshail.batc.org.uk/", new SocketIOOptions
            {
                Path = "/wb/chat/socket.io",
                Query = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("room", "eshail-wb") }
            });

            _client.OnConnected += (s, e) =>
            {
                _reconnectAttempt = 0;
                Dispatcher.Invoke(() => _connected.Text = "Connected: True");
                if (_settings.gui_autologin)
                    Dispatcher.Invoke(SetNick);
            };
            _client.OnDisconnected += (s, e) =>
            {
                Dispatcher.Invoke(() => _connected.Text = "Connected: False");
                ScheduleReconnect();
            };
            _client.OnError += (s, e) =>
            {
                Dispatcher.Invoke(() => AddChat("", "", "Error: " + e));
                ScheduleReconnect();
            };
            _client.On("history", r => OnHistory(r));
            _client.On("message", r => OnMessage(r));
            _client.On("nicks", r => OnNicks(r));
            _client.On("viewers", r => OnViewers(r));

            AddChat("", "", "Connecting to eshail.batc.org.uk ...");

            await TryConnect();
        }

        private async System.Threading.Tasks.Task TryConnect()
        {
            try
            {
                if (_client != null && !_client.Connected)
                    await _client.ConnectAsync();

                if (_client == null || !_client.Connected)
                {
                    AddChat("", "", "Not connected (TLS/firewall?). Retrying in the background...");
                    ScheduleReconnect();
                }
            }
            catch (Exception ex)
            {
                AddChat("", "", "Connect failed: " + ex.Message + " - will retry.");
                ScheduleReconnect();
            }
        }

        private void ScheduleReconnect()
        {
            if (_closing)
                return;

            lock (_reconnectLock)
            {
                if (_reconnecting)
                    return;
                _reconnecting = true;
            }

            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    while (!_closing && _client != null && !_client.Connected)
                    {
                        int delay = Math.Min(30, (int)Math.Pow(2, Math.Min(_reconnectAttempt, 5)));
                        _reconnectAttempt++;

                        for (int i = 0; i < delay && !_closing; i++)
                            await System.Threading.Tasks.Task.Delay(1000);

                        if (_closing || _client.Connected)
                            break;

                        try
                        {
                            await _client.ConnectAsync();
                        }
                        catch { }
                    }
                }
                finally
                {
                    lock (_reconnectLock) { _reconnecting = false; }
                    if (!_closing && _client != null && !_client.Connected)
                        ScheduleReconnect();
                }
            });
        }

        private void Stop()
        {
            _closing = true;
            try { _client?.DisconnectAsync(); } catch { }
        }

        private void AddChat(string time, string nick, string msg)
        {
            Dispatcher.Invoke(() =>
            {
                var p = new Paragraph { Margin = new Thickness(0) };
                if (!string.IsNullOrEmpty(time))
                    p.Inlines.Add(new Run(time + " ") { Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204)) });
                if (!string.IsNullOrEmpty(nick))
                {
                    p.Inlines.Add(new Run("<" + nick + "> ") { Foreground = new SolidColorBrush(Color.FromRgb(251, 222, 45)), FontWeight = FontWeights.Bold });
                }
                p.Inlines.Add(new Run(msg) { Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204)) });
                _chat.Document.Blocks.Add(p);
                _chat.ScrollToEnd();
            });
        }

        private static JToken First(SocketIOResponse r)
        {
            JToken root = JToken.Parse(r.ToString());
            return root is JArray arr && arr.Count > 0 ? arr[0] : root;
        }

        private void OnHistory(SocketIOResponse r)
        {
            try
            {
                JToken root = First(r);
                Dispatcher.Invoke(() => { _chat.Document.Blocks.Clear(); _users.Items.Clear(); });

                JToken nicks = root["nicks"];
                if (nicks is JArray na)
                    foreach (var n in na)
                        Dispatcher.Invoke(() => _users.Items.Add(n.ToString()));

                JToken hist = root["history"];
                if (hist is JArray ha)
                {
                    foreach (var h in ha)
                    {
                        string t = h["time"]?.ToString() ?? "";
                        string name = h["name"]?.ToString() ?? "";
                        string msg = h["message"]?.ToString() ?? "";
                        string hhmm = t;
                        try { hhmm = Convert.ToDateTime(t).ToString("HH:mm"); } catch { }
                        AddChat(hhmm, name, msg);
                    }
                }
            }
            catch { }
        }

        private void OnMessage(SocketIOResponse r)
        {
            try
            {
                JToken m = First(r);
                string t = m["time"]?.ToString() ?? "";
                string name = m["name"]?.ToString() ?? "";
                string msg = m["message"]?.ToString() ?? "";
                string hhmm = t;
                try { hhmm = Convert.ToDateTime(t).ToString("HH:mm"); } catch { }
                AddChat(hhmm, name, msg);
            }
            catch { }
        }

        private void OnNicks(SocketIOResponse r)
        {
            try
            {
                JToken nicks = First(r)["nicks"];
                Dispatcher.Invoke(() => _users.Items.Clear());
                if (nicks is JArray na)
                    foreach (var n in na)
                        Dispatcher.Invoke(() => _users.Items.Add(n.ToString()));
            }
            catch { }
        }

        private void OnViewers(SocketIOResponse r)
        {
            try
            {
                string num = First(r)["num"]?.ToString() ?? "";
                Dispatcher.Invoke(() => Title = "QO-100 Wideband Chat - Viewers: " + num);
            }
            catch { }
        }

        private async void SetNick()
        {
            try
            {
                if (_client != null && _client.Connected)
                {
                    string nick = _nick.Text.Trim();
                    if (nick.Length > 0 && nick != "NONICK")
                    {
                        await _client.EmitAsync("setnick", new { nick });
                        _settings.nickname = nick;
                        AddChat(DateTime.Now.ToString("HH:mm"), "Chat", "You are now known as '" + nick + "'");
                    }
                }
            }
            catch { }
        }

        private async void SendMessage()
        {
            try
            {
                if (_client != null && _client.Connected)
                {
                    string msg = _message.Text.Trim();
                    if (msg.Length > 0)
                        await _client.EmitAsync("message", new { message = msg });
                    _message.Text = "";
                }
            }
            catch { }
        }

        private void SignalReport(int tuner)
        {
            try
            {
                var data = _source.GetSignalData(tuner);
                string report = _settings.sigreport_template;
                report = report.Replace("{SN}", data["ServiceName"]);
                report = report.Replace("{SP}", data["ServiceProvider"]);
                report = report.Replace("{DBM}", data["dbMargin"]);
                report = report.Replace("{MER}", data["Mer"] + " dB");
                report = report.Replace("{SR}", data["SR"] + "");
                report = report.Replace("{FREQ}", data["Frequency"] + "");
                _message.Text = report;
                Clipboard.SetText(report);
            }
            catch { }
        }
    }
}
