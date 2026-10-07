using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;

using opentuner;
using opentuner.Utilities;
using opentuner.MediaSources;
using opentuner.MediaSources.Minitiouner;
using opentuner.MediaSources.Longmynd;
using opentuner.MediaSources.WinterHill;
using opentuner.MediaPlayers;
using OpenTuner.Wpf.Sdr;
using OpenTuner.Wpf.Players;
using OpenTuner.Wpf.Properties;
using OpenTuner.Wpf.Spectrum;
using opentuner.ExtraFeatures.MqttClient;
using opentuner.ExtraFeatures.QuickTuneControl;
using opentuner.ExtraFeatures.DATVReporter;

namespace OpenTuner.Wpf
{
    public partial class MainWindow : Window
    {
        private readonly List<OTSource> _sources = new List<OTSource>();
        private MainSettings _settings;
        private SettingsManager<MainSettings> _settingsManager;

        private SdrSpectrumControl _sdrControl;
        private OTSource _connectedSource;
        private int _connectedIndex = -1;

        private readonly List<OTMediaPlayer> _players = new List<OTMediaPlayer>();
        private readonly List<ContentControl> _videoCells = new List<ContentControl>();
        private readonly List<TextBlock> _videoInfo = new List<TextBlock>();
        private readonly List<TextBlock> _videoVolume = new List<TextBlock>();
        private readonly List<FrameworkElement> _videoViews = new List<FrameworkElement>();

        private bool _stacked;
        private bool _fullscreen;
        private int _focusedVideo = -1;
        private readonly List<TSRecorder> _recorders = new List<TSRecorder>();
        private readonly List<TSUdpStreamer> _streamers = new List<TSUdpStreamer>();
        private PropertyPanelControl _propertyPanel;
        private BatcSpectrumControl _batcControl;

        private MqttManager _mqtt;
        private QuickTuneControl _quickTune;
        private DATVReporter _datv;
        private OpenTuner.Wpf.Dialogs.BroadcastListenerWindow _broadcastWindow;

        public MainWindow()
        {
            InitializeComponent();

            _settings = new MainSettings();
            _settingsManager = new SettingsManager<MainSettings>("open_tuner_settings");
            _settings = _settingsManager.LoadSettings(_settings);

            if (string.IsNullOrEmpty(_settings.media_path) || _settings.media_path == AppDomain.CurrentDomain.BaseDirectory)
                _settings.media_path = AppDomain.CurrentDomain.BaseDirectory + "Screenshots\\";
            if (string.IsNullOrEmpty(_settings.media_video_path) || _settings.media_video_path == AppDomain.CurrentDomain.BaseDirectory)
                _settings.media_video_path = AppDomain.CurrentDomain.BaseDirectory + "Videos\\";
            try { Directory.CreateDirectory(_settings.media_path); } catch { }
            try { Directory.CreateDirectory(_settings.media_video_path); } catch { }

            // modern / dark theme
            ThemeManager.Apply(false);
            menuDarkMode.IsChecked = false;

            try { debugHost.Content = new OpenTuner.Wpf.Dialogs.LogViewControl(); } catch { }

            // load sources (reusing the existing logic classes)
            try
            {
                _sources.Add(new MinitiounerSource());
                _sources.Add(new LongmyndSource());
                _sources.Add(new WinterHillSource());
            }
            catch (Exception)
            {
                // ignore - sources are optional for the shell
            }

            foreach (OTSource s in _sources)
                comboSources.Items.Add(s.GetName());

            if (comboSources.Items.Count > 0)
            {
                int idx = _settings.default_source;
                if (idx < 0 || idx >= comboSources.Items.Count)
                    idx = 0;
                comboSources.SelectedIndex = idx;
            }

            chkBatcSpectrum.IsChecked = _settings.enable_spectrum_checkbox;
            chkBatcChat.IsChecked = _settings.enable_chatform_checkbox;
            chkMqtt.IsChecked = _settings.enable_mqtt_checkbox;
            chkQuicktune.IsChecked = _settings.enable_quicktune_checkbox;
            chkDatvReporter.IsChecked = _settings.enable_datvreporter_checkbox;
            chkSdrSpectrum.IsChecked = _settings.enable_sdr_spectrum_checkbox;

            chkBatcSpectrum.Checked += Feature_Changed;
            chkBatcSpectrum.Unchecked += Feature_Changed;
            chkBatcChat.Checked += Feature_Changed;
            chkBatcChat.Unchecked += Feature_Changed;
            chkMqtt.Checked += Feature_Changed;
            chkMqtt.Unchecked += Feature_Changed;
            chkQuicktune.Checked += Feature_Changed;
            chkQuicktune.Unchecked += Feature_Changed;
            chkDatvReporter.Checked += Feature_Changed;
            chkDatvReporter.Unchecked += Feature_Changed;
            chkSdrSpectrum.Checked += Feature_Changed;
            chkSdrSpectrum.Unchecked += Feature_Changed;

            Closing += (s, e) =>
            {
                try { _propertyPanel?.Detach(); } catch { }
                try { _sdrControl?.Disconnect(); } catch { }
                try { _mqtt?.Disconnect(); } catch { }
                try { _quickTune?.Close(); } catch { }
                try { _datv?.Close(); } catch { }
                try { foreach (var p in _players) p.Stop(); } catch { }
                SaveSettings();
            };

            if (chkSdrSpectrum.IsChecked == true)
            {
                try
                {
                    EnsureSdrControl();
                    tabsTools.SelectedIndex = 1;
                }
                catch { }
            }

            PreviewKeyDown += MainWindow_PreviewKeyDown;
        }

        private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape && _fullscreen)
            {
                ExitFullscreen();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.F11)
            {
                if (_fullscreen) ExitFullscreen(); else EnterFullscreen(-1);
                e.Handled = true;
            }
        }

        private void ToggleFullscreen(int focus)
        {
            if (_fullscreen && _focusedVideo == focus)
            {
                ExitFullscreen();
                return;
            }

            if (_fullscreen)
                ExitFullscreen();

            EnterFullscreen(focus);
        }

        private void EnterFullscreen(int focus)
        {
            _fullscreen = true;
            _focusedVideo = focus;

            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;

            mainMenu.Visibility = Visibility.Collapsed;

            leftColumn.MinWidth = 0;
            leftColumn.Width = new GridLength(0);
            leftSplitter.Visibility = Visibility.Collapsed;
            leftPanel.Visibility = Visibility.Collapsed;

            bottomSplitterRow.Height = new GridLength(0);
            bottomSplitter.Visibility = Visibility.Collapsed;
            bottomRow.Height = new GridLength(0);
            tabsTools.Visibility = Visibility.Collapsed;

            for (int i = 0; i < _videoCells.Count; i++)
                _videoCells[i].Visibility = (focus < 0 || i == focus) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ExitFullscreen()
        {
            _fullscreen = false;
            _focusedVideo = -1;

            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = WindowState.Normal;

            mainMenu.Visibility = Visibility.Visible;

            leftColumn.MinWidth = 300;
            leftColumn.Width = new GridLength(340);
            leftSplitter.Visibility = Visibility.Visible;
            leftPanel.Visibility = Visibility.Visible;

            bottomSplitterRow.Height = new GridLength(4);
            bottomSplitter.Visibility = Visibility.Visible;
            bottomRow.Height = new GridLength(260);
            tabsTools.Visibility = Visibility.Visible;

            foreach (var c in _videoCells)
                c.Visibility = Visibility.Visible;
        }

        private void FullScreen_Click(object sender, RoutedEventArgs e)
        {
            if (_fullscreen) ExitFullscreen(); else EnterFullscreen(-1);
        }

        private void LayoutSideBySide_Click(object sender, RoutedEventArgs e)
        {
            _stacked = false;
            menuSideBySide.IsChecked = true;
            menuStacked.IsChecked = false;
            RebuildVideoLayout();
        }

        private void LayoutStacked_Click(object sender, RoutedEventArgs e)
        {
            _stacked = true;
            menuStacked.IsChecked = true;
            menuSideBySide.IsChecked = false;
            RebuildVideoLayout();
        }

        private void RebuildVideoLayout()
        {
            if (_videoViews.Count == 0)
                return;

            BuildVideoLayout(_videoViews.Count);

            for (int i = 0; i < _videoViews.Count && i < _videoCells.Count; i++)
                _videoCells[i].Content = _videoViews[i];
        }

        private void Feature_Changed(object sender, RoutedEventArgs e)
        {
            _settings.enable_spectrum_checkbox = chkBatcSpectrum.IsChecked == true;
            _settings.enable_chatform_checkbox = chkBatcChat.IsChecked == true;
            _settings.enable_mqtt_checkbox = chkMqtt.IsChecked == true;
            _settings.enable_quicktune_checkbox = chkQuicktune.IsChecked == true;
            _settings.enable_datvreporter_checkbox = chkDatvReporter.IsChecked == true;
            _settings.enable_sdr_spectrum_checkbox = chkSdrSpectrum.IsChecked == true;
            SaveSettings();
        }

        private void SaveSettings()
        {
            try { _settingsManager?.SaveSettings(_settings); } catch { }
        }

        private void ComboSources_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = comboSources.SelectedIndex;
            if (idx < 0 || idx >= _sources.Count)
                return;

            txtSourceInfo.Text = _sources[idx].GetDescription();
            _settings.default_source = idx;
            SaveSettings();
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            int idx = comboSources.SelectedIndex;
            if (idx < 0 || idx >= _sources.Count)
                return;

            _connectedIndex = idx;
            _connectedSource = CreateSource(idx);

            try
            {
                if (!ConnectSource(_connectedSource))
                    return;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error connecting source: " + ex.Message, "Open Tuner",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            txtVideo.Visibility = Visibility.Collapsed;
            sourceSelCard.Visibility = Visibility.Collapsed;
            extraCard.Visibility = Visibility.Collapsed;

            if (chkSdrSpectrum.IsChecked == true)
            {
                EnsureSdrControl();
                tabsTools.SelectedIndex = 1;   // show the SDR tab
            }
        }

        private OTSource CreateSource(int idx)
        {
            switch (idx)
            {
                case 0: return new MinitiounerSource();
                case 1: return new LongmyndSource();
                case 2: return new WinterHillSource();
                default: return new MinitiounerSource();
            }
        }

        private void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            try { foreach (var p in _players) p.Stop(); } catch { }
            try { foreach (var r in _recorders) r.Close(); } catch { }
            try { foreach (var s in _streamers) s.Close(); } catch { }
            try { _mqtt?.Disconnect(); } catch { }
            try { _quickTune?.Close(); } catch { }
            try { _datv?.Close(); } catch { }
            try { _propertyPanel?.Detach(); } catch { }
            try { _connectedSource?.Close(); } catch { }

            _players.Clear();
            _recorders.Clear();
            _streamers.Clear();
            _mqtt = null;
            _quickTune = null;
            _datv = null;
            _connectedSource = null;

            videoArea.Children.Clear();
            txtVideo.Visibility = Visibility.Visible;
            propCard.Visibility = Visibility.Collapsed;
            sourceSelCard.Visibility = Visibility.Visible;
            extraCard.Visibility = Visibility.Visible;
        }

        private bool ConnectSource(OTSource source)
        {
            int n = source.InitializeHeadless(ChangeVideo);
            if (n < 0)
            {
                MessageBox.Show("Error connecting source: " + source.GetName(), "Open Tuner",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            BuildVideoLayout(n);

            _players.Clear();
            _videoInfo.Clear();
            _videoVolume.Clear();
            _videoViews.Clear();

            for (int i = 0; i < n; i++)
            {
                var view = new LibVLCSharp.WPF.VideoView();

                TextBlock info = new TextBlock
                {
                    Foreground = System.Windows.Media.Brushes.White,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 0, 0, 0)),
                    Padding = new System.Windows.Thickness(6, 3, 6, 3),
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Visibility = Visibility.Collapsed,
                    TextWrapping = TextWrapping.Wrap
                };

                TextBlock vol = new TextBlock
                {
                    Foreground = System.Windows.Media.Brushes.White,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 0, 0, 0)),
                    Padding = new System.Windows.Thickness(6, 3, 6, 3),
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Text = "Vol 0"
                };

                var overlay = new Grid { Background = System.Windows.Media.Brushes.Transparent };
                overlay.Children.Add(info);
                overlay.Children.Add(vol);
                view.Content = overlay;

                int idx = i;
                overlay.MouseWheel += (s, e) =>
                {
                    source.UpdateVolume(idx, e.Delta > 0 ? 10 : -10);
                    vol.Text = "Vol " + source.GetVolume(idx);
                    e.Handled = true;
                };
                overlay.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.ClickCount == 2)
                    {
                        ToggleFullscreen(idx);
                        e.Handled = true;
                    }
                    else
                    {
                        info.Visibility = info.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                    }
                };

                _videoCells[i].Content = view;
                _videoInfo.Add(info);
                _videoVolume.Add(vol);
                _videoViews.Add(view);

                var player = new WpfVlcMediaPlayer(view);
                player.Initialize(source.GetVideoDataQueue(i), i);
                player.onVideoOut += Player_onVideoOut;
                _players.Add(player);
            }

            source.ConfigureVideoPlayers(_players);
            source.ConfigureMediaPath(_settings.media_path);

            // raw .ts recorders and UDP streamers (needed by the R / U media buttons)
            _recorders.Clear();
            for (int i = 0; i < n; i++)
                _recorders.Add(new TSRecorder(_settings.media_video_path, i, source));
            source.ConfigureTSRecorders(_recorders);

            _streamers.Clear();
            for (int i = 0; i < n; i++)
                _streamers.Add(new TSUdpStreamer(_settings.streamer_udp_hosts[i], _settings.streamer_udp_ports[i], i, source));
            source.ConfigureTSStreamers(_streamers);

            // native WPF tuner property panel
            _propertyPanel?.Detach();
            _propertyPanel = new PropertyPanelControl();
            _propertyPanel.Attach(source);
            propHost.Content = _propertyPanel;
            propCard.Visibility = Visibility.Visible;

            // native WPF BATC spectrum
            if (chkBatcSpectrum.IsChecked == true)
            {
                _batcControl = new BatcSpectrumControl();
                _batcControl.OnSignalSelected += Sdr_OnSignalSelected;
                batcHost.Content = _batcControl;
            }

            // extra features (logic reused from the core)
            if (chkMqtt.IsChecked == true) { try { _mqtt = new MqttManager(); } catch { } }
            if (chkQuicktune.IsChecked == true) { try { _quickTune = new QuickTuneControl(source); } catch { } }
            if (chkDatvReporter.IsChecked == true) { _datv = new DATVReporter(); try { _datv.Connect(); } catch { } }
            if (chkBatcChat.IsChecked == true) { try { OpenWebChat(); } catch { } }

            source.OnSourceData += Source_OnSourceData;

            // "Tuner Control" from a property right-click opens the WPF tune dialog
            source.TunerControlRequested += tuner =>
                Dispatcher.Invoke(() => new OpenTuner.Wpf.Dialogs.TuneWindow(source, tuner) { Owner = this }.ShowDialog());

            return true;
        }

        private void Source_OnSourceData(int videoNr, OTSourceData properties, string description)
        {
            if (properties == null)
                return;

            try { _batcControl?.updateSignalCallsign(properties.service_name, properties.frequency, properties.symbol_rate); } catch { }

            if (_mqtt != null)
            {
                try { _mqtt.SendProperties(properties, _connectedSource.GetName() + "/" + description); } catch { }
            }

            if (_datv != null && properties.demod_locked)
            {
                try
                {
                    _datv.SendISawMessage(new ISawMessage(
                        properties.service_name,
                        properties.db_margin,
                        properties.mer,
                        (long)properties.frequency,
                        (int)properties.symbol_rate,
                        _connectedSource.GetDeviceName()));
                }
                catch { }
            }
        }

        private void BuildVideoLayout(int n)
        {
            videoArea.Children.Clear();
            videoArea.RowDefinitions.Clear();
            videoArea.ColumnDefinitions.Clear();
            _videoCells.Clear();

            int cols, rows;
            if (n <= 1) { rows = 1; cols = 1; }
            else if (n == 2) { rows = _stacked ? 2 : 1; cols = _stacked ? 1 : 2; }
            else if (n == 4) { rows = 2; cols = 2; }
            else { rows = 1; cols = n; }

            for (int r = 0; r < rows; r++) videoArea.RowDefinitions.Add(new RowDefinition());
            for (int c = 0; c < cols; c++) videoArea.ColumnDefinitions.Add(new ColumnDefinition());

            for (int i = 0; i < n; i++)
            {
                var cell = new ContentControl { Margin = new Thickness(1) };
                Grid.SetRow(cell, i / cols);
                Grid.SetColumn(cell, i % cols);
                videoArea.Children.Add(cell);
                _videoCells.Add(cell);
            }
        }

        private void Player_onVideoOut(object sender, MediaStatus status)
        {
            try
            {
                int id = ((OTMediaPlayer)sender).getID();
                if (id < 0 || id >= _videoInfo.Count)
                    return;

                string text =
                    (string.IsNullOrEmpty(status.VideoCodec) ? "" : status.VideoCodec) +
                    (status.VideoWidth > 0 ? "  " + status.VideoWidth + "x" + status.VideoHeight : "") +
                    "\n" +
                    (string.IsNullOrEmpty(status.AudioCodec) ? "" : status.AudioCodec) +
                    (status.AudioChannels > 0 ? "  " + status.AudioChannels + "ch" : "");

                Dispatcher.Invoke(() => _videoInfo[id].Text = text.Trim());
            }
            catch { }
        }

        private void ChangeVideo(int videoNumber, bool start)
        {
            int i = videoNumber - 1;
            if (i < 0 || i >= _players.Count)
                return;

            if (start)
            {
                _connectedSource?.StartStreaming(i);
                _players[i].Play();
            }
            else
            {
                _connectedSource?.StopStreaming(i);
                _players[i].Stop();
            }
        }

        private void EnsureSdrControl()
        {
            if (_sdrControl != null)
                return;

            _sdrControl = new SdrSpectrumControl();
            _sdrControl.OnSignalSelected += Sdr_OnSignalSelected;
            _sdrControl.OnStatus += Sdr_OnStatus;
            _sdrControl.OnRequestSettings += OpenSdrSettings;

            sdrHost.Content = _sdrControl;

            int rxCount = _connectedSource != null ? Math.Max(1, _connectedSource.GetVideoSourceCount()) : 2;
            comboSdrRx.Items.Clear();
            for (int i = 0; i < rxCount; i++)
                comboSdrRx.Items.Add("RX " + (i + 1));
            if (comboSdrRx.Items.Count > 0)
                comboSdrRx.SelectedIndex = 0;
        }

        private void SdrRx_Changed(object sender, SelectionChangedEventArgs e)
        {
            int r = Math.Max(0, comboSdrRx.SelectedIndex);
            _sdrControl?.SetSelectedReceiver(r);
            _batcControl?.SetSelectedReceiver(r);
        }

        private void SdrStrongest_Click(object sender, RoutedEventArgs e)
        {
            _sdrControl?.TuneStrongest();
        }

        private void Sdr_OnSignalSelected(int receiver, uint freqKHz, uint symbolRate)
        {
            try
            {
                _connectedSource?.SetFrequency(receiver, freqKHz, symbolRate, true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SDR tune failed: " + ex.Message);
            }
        }

        private void Sdr_OnStatus(string status)
        {
            Dispatcher.Invoke(() => txtSdrStatus.Text = status);
        }

        private void OpenSdrSettings()
        {
            if (_sdrControl == null)
                return;

            SdrSettingsWindow win = new SdrSettingsWindow(_sdrControl.Settings) { Owner = this };
            if (win.ShowDialog() == true)
            {
                // device settings may have changed - recreate the control/device
                _sdrControl.ApplySettings();

                if (sdrHost.Content == _sdrControl)
                    sdrHost.Content = _sdrControl;
            }
        }

        private void SourceSettings_Click(object sender, RoutedEventArgs e)
        {
            int idx = comboSources.SelectedIndex;
            if (idx < 0 || idx >= _sources.Count)
                return;

            try
            {
                OTSource src = _sources[idx];
                object settings = src.GetSettingsObject();

                if (settings == null)
                {
                    MessageBox.Show("This source has no editable settings.", "Open Tuner");
                    return;
                }

                System.Windows.Window win;

                if (settings is MinitiounerSettings ms)
                    win = new OpenTuner.Wpf.Dialogs.MinitiounerSettingsWindow(ms) { Owner = this };
                else if (settings is WinterHillSettings ws)
                    win = new OpenTuner.Wpf.Dialogs.WinterhillSettingsWindow(ws) { Owner = this };
                else if (settings is LongmyndSettings ls)
                    win = new OpenTuner.Wpf.Dialogs.LongmyndSettingsWindow(ls) { Owner = this };
                else
                    win = new OpenTuner.Wpf.Dialogs.SettingsEditorWindow(settings, src.GetName() + " Settings") { Owner = this };

                if (win.ShowDialog() == true)
                    src.PersistSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Settings failed: " + ex.Message, "Open Tuner",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SdrSettings_Click(object sender, RoutedEventArgs e)
        {
            EnsureSdrControl();
            OpenSdrSettings();
        }

        private void DarkMode_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.Apply(menuDarkMode.IsChecked);
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new OpenTuner.Wpf.Dialogs.SettingsWindow(_settings) { Owner = this };
                if (win.ShowDialog() == true)
                    SaveSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Settings failed: " + ex.Message, "Open Tuner",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Presets_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var mgr = new SettingsManager<List<opentuner.StoredFrequency>>("frequency_presets");
                var presets = mgr.LoadSettings(new List<opentuner.StoredFrequency>());
                var win = new OpenTuner.Wpf.Dialogs.FrequencyManagerWindow(presets) { Owner = this };
                win.ShowDialog();
                mgr.SaveSettings(presets);
                _connectedSource?.UpdateFrequencyPresets(presets);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Frequency manager failed: " + ex.Message, "Open Tuner");
            }
        }

        private void OpenSettingsObject<T>(string name, string title) where T : new()
        {
            var mgr = new SettingsManager<T>(name);
            T s = mgr.LoadSettings(new T());
            var win = new OpenTuner.Wpf.Dialogs.SettingsEditorWindow(s, title) { Owner = this };
            if (win.ShowDialog() == true)
                mgr.SaveSettings(s);
        }

        private void BatcSpectrumInfo_Click(object sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start("https://www.zr6tg.co.za/opentuner-spectrum/"); } catch { }
        }

        private void MqttSettings_Click(object sender, RoutedEventArgs e)
        {
            var mgr = new SettingsManager<opentuner.ExtraFeatures.MqttClient.MqttManagerSettings>("mqttclient_settings");
            var s = mgr.LoadSettings(new opentuner.ExtraFeatures.MqttClient.MqttManagerSettings());
            if (new OpenTuner.Wpf.Dialogs.MqttSettingsWindow(s) { Owner = this }.ShowDialog() == true)
                mgr.SaveSettings(s);
        }

        private void QuickTuneSettings_Click(object sender, RoutedEventArgs e)
        {
            var mgr = new SettingsManager<opentuner.ExtraFeatures.QuickTuneControl.QuickTuneControlSettings>("quicktune_settings");
            var s = mgr.LoadSettings(new opentuner.ExtraFeatures.QuickTuneControl.QuickTuneControlSettings());
            if (new OpenTuner.Wpf.Dialogs.QuickTuneSettingsWindow(s) { Owner = this }.ShowDialog() == true)
                mgr.SaveSettings(s);
        }

        private void DatvSettings_Click(object sender, RoutedEventArgs e)
        {
            var mgr = new SettingsManager<opentuner.ExtraFeatures.DATVReporter.DATVReporterSettings>("datvreporter_settings");
            var s = mgr.LoadSettings(new opentuner.ExtraFeatures.DATVReporter.DATVReporterSettings());
            if (new OpenTuner.Wpf.Dialogs.DatvReporterSettingsWindow(s) { Owner = this }.ShowDialog() == true)
                mgr.SaveSettings(s);
        }

        private void WebChatSettings_Click(object sender, RoutedEventArgs e)
        {
            var mgr = new SettingsManager<opentuner.ExtraFeatures.BATCWebchat.WebChatSettings>("qo100_webchat_settings");
            var s = mgr.LoadSettings(new opentuner.ExtraFeatures.BATCWebchat.WebChatSettings());
            if (new OpenTuner.Wpf.Dialogs.WebChatSettingsWindow(s) { Owner = this }.ShowDialog() == true)
                mgr.SaveSettings(s);
        }

        private void BroadcastListener_Click(object sender, RoutedEventArgs e)
        {
            _broadcastWindow = new OpenTuner.Wpf.Dialogs.BroadcastListenerWindow { Owner = this };
            _broadcastWindow.Show();
        }

        private void HardwareInfo_Click(object sender, RoutedEventArgs e)
        {
            new OpenTuner.Wpf.Dialogs.HardwareInfoWindow(_connectedSource) { Owner = this }.Show();
        }

        private void ExternalTools_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var mgr = new SettingsManager<List<opentuner.ExternalTool>>("external_tools");
                var tools = mgr.LoadSettings(new List<opentuner.ExternalTool>());
                var win = new OpenTuner.Wpf.Dialogs.ExternalToolsWindow(tools) { Owner = this };
                win.ShowDialog();
                mgr.SaveSettings(tools);
            }
            catch (Exception ex)
            {
                MessageBox.Show("External tools failed: " + ex.Message, "Open Tuner");
            }
        }

        private void PlutoControl_Click(object sender, RoutedEventArgs e)
        {
            new OpenTuner.Wpf.Dialogs.PlutoControlWindow { Owner = this }.Show();
        }

        private void WebChat_Click(object sender, RoutedEventArgs e)
        {
            if (_connectedSource == null)
            {
                MessageBox.Show("Connect to a source first.", "Open Tuner");
                return;
            }

            OpenWebChat();
        }

        private OpenTuner.Wpf.Dialogs.WebChatWindow _webChat;

        private void OpenWebChat()
        {
            try
            {
                var mgr = new SettingsManager<opentuner.ExtraFeatures.BATCWebchat.WebChatSettings>("qo100_webchat_settings");
                var settings = mgr.LoadSettings(new opentuner.ExtraFeatures.BATCWebchat.WebChatSettings());

                if (_webChat == null)
                {
                    _webChat = new OpenTuner.Wpf.Dialogs.WebChatWindow(settings, _connectedSource) { Owner = this };
                    _webChat.Closed += (s, e) => _webChat = null;
                }

                _webChat.Show();
                _webChat.Activate();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Web chat failed: " + ex.Message, "Open Tuner");
            }
        }

        private void Tune_Click(object sender, RoutedEventArgs e)
        {
            if (_connectedSource == null)
            {
                MessageBox.Show("Connect to a source first.", "Open Tuner");
                return;
            }

            var win = new OpenTuner.Wpf.Dialogs.TuneWindow(_connectedSource) { Owner = this };
            win.ShowDialog();
        }

        private void DebugLog_Click(object sender, RoutedEventArgs e)
        {
            new OpenTuner.Wpf.Dialogs.DebugWindow { Owner = this }.Show();
        }

        private void HideProps_Click(object sender, RoutedEventArgs e) { }
        private void HideSpectrum_Click(object sender, RoutedEventArgs e) { }

        private void Docs_Click(object sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start("https://www.zr6tg.co.za/opentuner-documentation/"); } catch { }
        }

        private void Link_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement fe && fe.Tag is string url && url.Length > 0)
                    System.Diagnostics.Process.Start(url);
            }
            catch { }
        }

        private void Quit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}




