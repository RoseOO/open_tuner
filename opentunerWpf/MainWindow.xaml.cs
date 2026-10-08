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
        private readonly List<FrameworkElement> _videoViews = new List<FrameworkElement>();

        // per-video overlay elements (max 4 tuners)
        private readonly TextBlock[] _ovTitle = new TextBlock[4];
        private readonly TextBlock[] _ovQrz = new TextBlock[4];
        private readonly TextBlock[] _ovStream = new TextBlock[4];
        private readonly TextBlock[] _ovCodec = new TextBlock[4];
        private readonly TextBlock[] _ovState = new TextBlock[4];
        private readonly TextBlock[] _ovVolume = new TextBlock[4];
        private readonly string[] _ovLastService = new string[4];

        private readonly Grid[] _videoOverlays = new Grid[4];         // the WPF content drawn on top of each VLC view
        private readonly Border[] _ovInfoBorder = new Border[4];
        private readonly StackPanel[] _ovRightStack = new StackPanel[4];
        private readonly Border[] _ovInfoButton = new Border[4];
        private readonly bool[] _overlayHidden = new bool[4];
        private int _soloFocus = -1;                                   // feed index shown fullscreen, or -1

        private bool _stacked;
        private bool _fullscreen;
        private int _focusedVideo = -1;
        private string _layoutPreset = "2-side";
        private readonly List<TSRecorder> _recorders = new List<TSRecorder>();
        private readonly List<TSUdpStreamer> _streamers = new List<TSUdpStreamer>();
        private PropertyPanelControl _propertyPanel;
        private BatcSpectrumControl _batcControl;

        private MqttManager _mqtt;
        private QuickTuneControl _quickTune;
        private DATVReporter _datv;
        private OpenTuner.Wpf.Dialogs.BroadcastListenerWindow _broadcastWindow;

        private opentuner.ExtraFeatures.QRZ.QrzClient _qrzClient;
        private opentuner.ExtraFeatures.QRZ.QrzSettings _qrzSettings;

        private string _lastServiceName = "";
        private System.Windows.Threading.DispatcherTimer _statusTimer;
        private System.Windows.Threading.DispatcherTimer _snapshotTimer;
        private SignalGraphControl _signalGraph;

        private bool _lastLockState = false;
        private bool _lastRecState = false;
        private OTSourceData _latestData;
        private readonly OTSourceData[] _latestByTuner = new OTSourceData[4];

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

            // localization: apply the saved language, then build the language menu
            LocalizationManager.Apply(string.IsNullOrEmpty(_settings.language) ? "en" : _settings.language);
            BuildLanguageMenu();
            try { Serilog.Log.Information("UI language: " + LocalizationManager.CurrentLanguage); } catch { }

            try { debugHost.Content = new OpenTuner.Wpf.Dialogs.LogViewControl(); } catch { }

            // signal history graph
            try
            {
                _signalGraph = new SignalGraphControl();
                signalHost.Content = _signalGraph;

                comboGraphWindow.Items.Add("1 min");
                comboGraphWindow.Items.Add("2 min");
                comboGraphWindow.Items.Add("5 min");
                comboGraphWindow.Items.Add("10 min");
                comboGraphWindow.Items.Add("30 min");
                comboGraphWindow.SelectedIndex = 2;   // 5 min
            }
            catch { }

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

            // QRZ lookup settings
            try
            {
                var qrzMgr = new SettingsManager<opentuner.ExtraFeatures.QRZ.QrzSettings>("qrz_settings");
                _qrzSettings = qrzMgr.LoadSettings(new opentuner.ExtraFeatures.QRZ.QrzSettings());
            }
            catch { _qrzSettings = new opentuner.ExtraFeatures.QRZ.QrzSettings(); }
            chkQrz.IsChecked = _qrzSettings.enabled;

            // layout preset
            _layoutPreset = string.IsNullOrEmpty(_settings.layout_preset) ? "2-side" : _settings.layout_preset;
            ApplyLayoutPreset(_layoutPreset);

            // toasts + status bar
            try { ToastService.Attach(this); } catch { }
            _statusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statusTimer.Tick += (s, e) => UpdateStatusBar();
            _statusTimer.Start();

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
            chkQrz.Checked += QrzToggled;
            chkQrz.Unchecked += QrzToggled;

            Closing += (s, e) =>
            {
                try { Serilog.Log.Information("Closing: cleanup start"); } catch { }
                try { _statusTimer?.Stop(); } catch { }
                try { _snapshotTimer?.Stop(); } catch { }
                try { _propertyPanel?.Detach(); } catch { }
                try { _batcControl?.Stop(); } catch { }
                try { _sdrControl?.Disconnect(); } catch { }
                try { _mqtt?.Disconnect(); } catch { }
                try { _quickTune?.Close(); } catch { }
                try { _datv?.Close(); } catch { }
                try { foreach (var r in _recorders) r.Close(); } catch { }
                try { foreach (var st in _streamers) st.Close(); } catch { }
                try { foreach (var p in _players) p.Stop(); } catch { }
                try { foreach (var p in _players) p.Close(); } catch { }
                try { _connectedSource?.Close(); } catch { }
                _connectedSource = null;
                SaveSettings();

                // make sure the process actually exits even if a stray
                // non-background thread is still alive
                Application.Current.Shutdown();

                var killer = new System.Threading.Thread(() =>
                {
                    System.Threading.Thread.Sleep(1500);
                    try { Serilog.Log.Information("Shutdown watchdog: forcing exit"); } catch { }
                    try { Environment.Exit(0); } catch { }
                })
                { IsBackground = true };
                killer.Start();
                try { Serilog.Log.Information("Closing: cleanup done"); } catch { }
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
            // don't steal keys while typing in a text field
            var focused = System.Windows.Input.Keyboard.FocusedElement as DependencyObject;
            if (focused is TextBox || focused is PasswordBox || focused is System.Windows.Controls.Primitives.TextBoxBase)
                return;

            if (e.Key == System.Windows.Input.Key.Escape && _fullscreen)
            {
                ExitFullscreen();
                e.Handled = true;
                return;
            }

            switch (e.Key)
            {
                case System.Windows.Input.Key.F11:
                    if (_fullscreen) ExitFullscreen(); else EnterFullscreen(-1);
                    e.Handled = true;
                    return;

                case System.Windows.Input.Key.F1:
                    new OpenTuner.Wpf.Dialogs.ShortcutsWindow { Owner = this }.ShowDialog();
                    e.Handled = true;
                    return;

                case System.Windows.Input.Key.R:
                    ToggleRecord(-1);
                    e.Handled = true;
                    return;

                case System.Windows.Input.Key.S:
                    TakeSnapshot();
                    ToastService.Show(LocalizationManager.Get("mw.toast.snapshot"), ToastKind.Info, 2);
                    e.Handled = true;
                    return;

                case System.Windows.Input.Key.M:
                    try { _connectedSource?.ToggleMute(_focusedVideo < 0 ? 0 : _focusedVideo); } catch { }
                    e.Handled = true;
                    return;

                case System.Windows.Input.Key.U:
                    ToggleStream(-1);
                    e.Handled = true;
                    return;

                case System.Windows.Input.Key.D1:
                case System.Windows.Input.Key.NumPad1:
                    FocusVideo(0); e.Handled = true; return;
                case System.Windows.Input.Key.D2:
                case System.Windows.Input.Key.NumPad2:
                    FocusVideo(1); e.Handled = true; return;
                case System.Windows.Input.Key.D3:
                case System.Windows.Input.Key.NumPad3:
                    FocusVideo(2); e.Handled = true; return;
                case System.Windows.Input.Key.D4:
                case System.Windows.Input.Key.NumPad4:
                    FocusVideo(3); e.Handled = true; return;
            }
        }

        private void FocusVideo(int index)
        {
            if (index < 0 || index >= _videoViews.Count)
                return;

            ToggleFullscreen(index);
        }

        private void ToggleRecord(int index)
        {
            try
            {
                if (index < 0)
                {
                    bool anyOn = _recorders.Exists(r => r.record);
                    foreach (var r in _recorders) r.record = !anyOn;
                }
                else if (index < _recorders.Count)
                {
                    _recorders[index].record = !_recorders[index].record;
                }
            }
            catch { }
        }

        private void ToggleStream(int index)
        {
            try
            {
                if (index < 0)
                {
                    bool anyOn = _streamers.Exists(s => s.stream);
                    foreach (var s in _streamers) s.stream = !anyOn;
                }
                else if (index < _streamers.Count)
                {
                    _streamers[index].stream = !_streamers[index].stream;
                }
            }
            catch { }
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
            bottomRow.MinHeight = 0;                       // otherwise a 0 height is clamped to 140
            bottomRow.Height = new GridLength(0);
            tabsTools.Visibility = Visibility.Collapsed;
            statusBar.Visibility = Visibility.Collapsed;   // truly fullscreen

            _soloFocus = focus;
            ApplyVideoLayout();

            // the VLC/Flyleaf overlay window resizes lazily after a fullscreen
            // transition, so re-assert it on the next render pass
            Dispatcher.BeginInvoke(new Action(() => RefreshVideoOverlays()),
                System.Windows.Threading.DispatcherPriority.Render);
        }

        /// <summary>Re-asserts the overlay content/visibility after a layout change.</summary>
        private void RefreshVideoOverlays()
        {
            try
            {
                for (int i = 0; i < _videoViews.Count; i++)
                {
                    if (!(_videoViews[i] is ContentControl vc))
                        continue;

                    bool present = _soloFocus >= 0 ? (i == _soloFocus) : (vc.Visibility == Visibility.Visible);
                    if (!present)
                        continue;

                    if (vc.Content != _videoOverlays[i])
                        vc.Content = _videoOverlays[i];
                    vc.InvalidateVisual();
                    vc.UpdateLayout();
                }
            }
            catch { }
        }

        private void ExitFullscreen()
        {
            _fullscreen = false;
            _focusedVideo = -1;
            _soloFocus = -1;

            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = WindowState.Normal;

            mainMenu.Visibility = Visibility.Visible;

            // restore the left column / tools panel, honouring the hide toggles
            leftColumn.MinWidth = _propsHidden ? 0 : 300;
            leftColumn.Width = new GridLength(_propsHidden ? 0 : 340);
            leftSplitter.Visibility = _propsHidden ? Visibility.Collapsed : Visibility.Visible;
            leftPanel.Visibility = _propsHidden ? Visibility.Collapsed : Visibility.Visible;

            bottomSplitterRow.Height = _toolsHidden ? new GridLength(0) : new GridLength(4);
            bottomSplitter.Visibility = _toolsHidden ? Visibility.Collapsed : Visibility.Visible;
            bottomRow.MinHeight = _toolsHidden ? 0 : 140;
            bottomRow.Height = _toolsHidden ? new GridLength(0) : new GridLength(260);
            tabsTools.Visibility = _toolsHidden ? Visibility.Collapsed : Visibility.Visible;
            statusBar.Visibility = Visibility.Visible;

            ApplyVideoLayout();
            Dispatcher.BeginInvoke(new Action(() => RefreshVideoOverlays()),
                System.Windows.Threading.DispatcherPriority.Render);
        }

        private void FullScreen_Click(object sender, RoutedEventArgs e)
        {
            if (_fullscreen) ExitFullscreen(); else EnterFullscreen(-1);
        }

        private void LayoutPreset_Click(object sender, RoutedEventArgs e)
        {
            ApplyLayoutPreset((sender as FrameworkElement)?.Tag as string ?? "2-side");
            _settings.layout_preset = _layoutPreset;
            SaveSettings();
        }

        private void ApplyLayoutPreset(string preset)
        {
            if (string.IsNullOrEmpty(preset))
                preset = "2-side";

            _layoutPreset = preset;
            _stacked = preset == "2-stack";

            if (menuLayout1 != null) menuLayout1.IsChecked = preset == "1";
            if (menuLayout2Side != null) menuLayout2Side.IsChecked = preset == "2-side";
            if (menuLayout2Stack != null) menuLayout2Stack.IsChecked = preset == "2-stack";
            if (menuLayout4 != null) menuLayout4.IsChecked = preset == "4-quad";

            ApplyVideoLayout();
        }

        /// <summary>
        /// Re-arranges the existing video cells for the current layout preset (or the
        /// fullscreen "solo" feed). The video views are never re-parented (that would
        /// tear down the LibVLC WPF overlay window and break clicks/overlays); we only
        /// change spans/visibility and detach the overlay content of hidden feeds so
        /// their separate VLC foreground windows don't bleed through.
        /// </summary>
        private void ApplyVideoLayout()
        {
            if (_videoCells.Count < 4)
                return;

            int n = _videoViews.Count;

            // which feed index is shown in each cell (solo fullscreen overrides)
            Func<int, bool> inLayout;
            int firstShown = 0;

            if (_soloFocus >= 0)
            {
                inLayout = i => i == _soloFocus;
                firstShown = _soloFocus;
            }
            else
            {
                int effective;
                switch (_layoutPreset)
                {
                    case "1": effective = Math.Min(n, 1); break;
                    case "2-side":
                    case "2-stack": effective = Math.Min(n, 2); break;
                    default: effective = Math.Min(n, 4); break;
                }
                inLayout = i => i < effective;
            }

            for (int i = 0; i < 4; i++)
            {
                var c = _videoCells[i];
                Grid.SetRow(c, 0);
                Grid.SetColumn(c, 0);
                Grid.SetRowSpan(c, 1);
                Grid.SetColumnSpan(c, 1);

                bool present = inLayout(i);
                c.Visibility = present ? Visibility.Visible : Visibility.Collapsed;

                if (i < _videoViews.Count && _videoViews[i] is ContentControl vc)
                {
                    if (present)
                    {
                        // (re)attach the view if it was unloaded, and show its overlay
                        if (!ReferenceEquals(c.Content, _videoViews[i]))
                            c.Content = _videoViews[i];
                        vc.Visibility = Visibility.Visible;
                        vc.Content = _videoOverlays[i];
                    }
                    else
                    {
                        // UNLOAD the view so its LibVLC/Flyleaf overlay window is
                        // torn down - merely collapsing a cell leaves that separate
                        // window floating over the fullscreen video.
                        vc.Visibility = Visibility.Collapsed;
                        vc.Content = null;
                        if (c.Content != null)
                            c.Content = null;
                    }
                }
            }

            // spans
            if (_soloFocus >= 0)
            {
                var c = _videoCells[_soloFocus];
                Grid.SetRowSpan(c, 2);
                Grid.SetColumnSpan(c, 2);
                return;
            }

            switch (_layoutPreset)
            {
                case "1":
                    Grid.SetRowSpan(_videoCells[0], 2);
                    Grid.SetColumnSpan(_videoCells[0], 2);
                    break;

                case "2-side":
                    Grid.SetRowSpan(_videoCells[0], 2);
                    Grid.SetRow(_videoCells[1], 0);
                    Grid.SetColumn(_videoCells[1], 1);
                    Grid.SetRowSpan(_videoCells[1], 2);
                    break;

                case "2-stack":
                    Grid.SetColumnSpan(_videoCells[0], 2);
                    Grid.SetRow(_videoCells[1], 1);
                    Grid.SetColumn(_videoCells[1], 0);
                    Grid.SetColumnSpan(_videoCells[1], 2);
                    break;

                case "4-quad":
                    for (int i = 0; i < 4; i++)
                    {
                        Grid.SetRow(_videoCells[i], i / 2);
                        Grid.SetColumn(_videoCells[i], i % 2);
                    }
                    break;
            }
        }

        /// <summary>Single-click on a feed: toggle its on-screen overlays.</summary>
        private void ToggleOverlays(int idx)
        {
            if (idx < 0 || idx >= 4)
                return;

            _overlayHidden[idx] = !_overlayHidden[idx];
            var vis = _overlayHidden[idx] ? Visibility.Collapsed : Visibility.Visible;

            if (_ovInfoBorder[idx] != null) _ovInfoBorder[idx].Visibility = vis;
            if (_ovRightStack[idx] != null) _ovRightStack[idx].Visibility = vis;

            if (_ovInfoButton[idx] != null)
                _ovInfoButton[idx].Opacity = _overlayHidden[idx] ? 0.5 : 1.0;
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
            try { _batcControl?.Stop(); } catch { }
            try { _propertyPanel?.Detach(); } catch { }
            try { _connectedSource?.Close(); } catch { }

            _players.Clear();
            _recorders.Clear();
            _streamers.Clear();
            _mqtt = null;
            _quickTune = null;
            _datv = null;
            _batcControl = null;
            _connectedSource = null;
            _latestData = null;
            _soloFocus = -1;

            videoArea.Children.Clear();
            txtVideo.Visibility = Visibility.Visible;
            propCard.Visibility = Visibility.Collapsed;
            sourceSelCard.Visibility = Visibility.Visible;
            extraCard.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// If a source is configured with "Always Ask", show the WPF interface
        /// chooser and apply the choice. Returns false if the user cancels.
        /// </summary>
        private bool PromptInterfaceIfNeeded(OTSource source)
        {
            try
            {
                if (source is WinterHillSource)
                {
                    var s = source.GetSettingsObject() as WinterHillSettings;
                    if (s != null && s.DefaultInterface == 0)
                    {
                        var dlg = new OpenTuner.Wpf.Dialogs.ChooseInterfaceWindow(
                            "WinterHill Interface",
                            new[] { "WinterHill (ZR6TG Variant)", "PicoTuner (G4EWJ Ethernet WH)" }) { Owner = this };
                        if (dlg.ShowDialog() != true)
                            return false;
                        s.DefaultInterface = (byte)(dlg.SelectedIndex + 1);
                        source.PersistSettings();
                    }
                }
                else if (source is MinitiounerSource)
                {
                    var s = source.GetSettingsObject() as MinitiounerSettings;
                    if (s != null && s.DefaultInterface == 0)
                    {
                        var dlg = new OpenTuner.Wpf.Dialogs.ChooseInterfaceWindow(
                            "Minitiouner Interface",
                            new[] { "FTDI FT2232", "PicoTuner" }) { Owner = this };
                        if (dlg.ShowDialog() != true)
                            return false;
                        s.DefaultInterface = (byte)(dlg.SelectedIndex + 1);
                        source.PersistSettings();
                    }
                }
            }
            catch { }
            return true;
        }

        private bool ConnectSource(OTSource source)
        {
            // honour the "Always Ask" interface setting for the sources that have one
            if (!PromptInterfaceIfNeeded(source))
                return false;

            int n = source.InitializeHeadless(ChangeVideo);
            if (n < 0)
            {
                MessageBox.Show("Error connecting source: " + source.GetName(), "Open Tuner",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            BuildVideoLayout(n);

            _players.Clear();
            _videoViews.Clear();
            for (int i = 0; i < 4; i++)
            {
                _ovTitle[i] = null; _ovQrz[i] = null; _ovStream[i] = null;
                _ovCodec[i] = null; _ovState[i] = null; _ovVolume[i] = null;
                _ovLastService[i] = "";
                _videoOverlays[i] = null; _ovInfoBorder[i] = null; _ovRightStack[i] = null;
                _overlayHidden[i] = false; _ovInfoButton[i] = null;
            }
            _soloFocus = -1;

            for (int i = 0; i < n; i++)
            {
                bool useFfmpeg = _settings.mediaplayer_preferences[i] == 1 && App.FfmpegAvailable;

                ContentControl view = useFfmpeg
                    ? (ContentControl)new FlyleafLib.Controls.WPF.FlyleafHost()
                    : new LibVLCSharp.WPF.VideoView();
                int idx = i;

                var badgeBg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xB0, 0, 0, 0));

                var title = new TextBlock
                {
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 14,
                    FontWeight = System.Windows.FontWeights.Bold,
                    Text = "RX " + (i + 1)
                };

                var qrz = new TextBlock
                {
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFB, 0xDD, 0x2D)),
                    FontSize = 11,
                    Margin = new System.Windows.Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Bottom
                };

                var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
                titleRow.Children.Add(title);
                titleRow.Children.Add(qrz);

                var stream = new TextBlock
                {
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xDD, 0xDD, 0xDD)),
                    FontSize = 11,
                    Margin = new System.Windows.Thickness(0, 3, 0, 0),
                    Text = "Waiting for signal..."
                };

                var codec = new TextBlock
                {
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9F, 0xB0, 0xC0)),
                    FontSize = 11
                };

                var infoPanel = new StackPanel();
                infoPanel.Children.Add(titleRow);
                infoPanel.Children.Add(stream);
                infoPanel.Children.Add(codec);

                var infoBorder = new Border
                {
                    Background = badgeBg,
                    CornerRadius = new System.Windows.CornerRadius(4),
                    Padding = new System.Windows.Thickness(9, 6, 9, 6),
                    Margin = new System.Windows.Thickness(6, 6, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = infoPanel
                };

                var state = new TextBlock
                {
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Padding = new System.Windows.Thickness(6, 2, 6, 2),
                    Background = badgeBg,
                    Text = "NO SIGNAL"
                };

                var vol = new TextBlock
                {
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Padding = new System.Windows.Thickness(6, 2, 6, 2),
                    Background = badgeBg,
                    Margin = new System.Windows.Thickness(0, 4, 0, 0),
                    Text = "Vol 0"
                };

                // top-right column: badges (toggled by "info") + always-visible chips
                var rightStack = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                rightStack.Children.Add(state);
                rightStack.Children.Add(vol);

                var mdlicons = new System.Windows.Media.FontFamily("Segoe MDL2 Assets");

                var infoText = new TextBlock
                {
                    Text = "\uE946",                       // info glyph
                    FontFamily = mdlicons,
                    FontSize = 13,
                    Foreground = System.Windows.Media.Brushes.White
                };
                var infoChip = new Border
                {
                    Background = badgeBg,
                    CornerRadius = new System.Windows.CornerRadius(4),
                    Padding = new System.Windows.Thickness(7, 3, 7, 3),
                    Margin = new System.Windows.Thickness(0, 0, 6, 0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Child = infoText,
                    ToolTip = "Show / hide the stream overlay"
                };
                infoChip.MouseLeftButtonDown += (s, e) => { ToggleOverlays(idx); e.Handled = true; };

                var fullChip = new Border
                {
                    Background = badgeBg,
                    CornerRadius = new System.Windows.CornerRadius(4),
                    Padding = new System.Windows.Thickness(7, 3, 7, 3),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = "\uE740",                   // fullscreen glyph
                        FontFamily = mdlicons,
                        FontSize = 13,
                        Foreground = System.Windows.Media.Brushes.White
                    },
                    ToolTip = "Fullscreen this RX"
                };
                fullChip.MouseLeftButtonDown += (s, e) => { ToggleFullscreen(idx); e.Handled = true; };

                var chipRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new System.Windows.Thickness(0, 4, 0, 0)
                };
                chipRow.Children.Add(infoChip);
                chipRow.Children.Add(fullChip);

                var rightCol = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new System.Windows.Thickness(0, 6, 6, 0)
                };
                rightCol.Children.Add(rightStack);   // badges - collapsed by "info"
                rightCol.Children.Add(chipRow);      // chips - always visible

                var overlay = new Grid { Background = System.Windows.Media.Brushes.Transparent };
                overlay.Children.Add(infoBorder);
                overlay.Children.Add(rightCol);

                view.Content = overlay;

                overlay.MouseWheel += (s, e) =>
                {
                    source.UpdateVolume(idx, e.Delta > 0 ? 10 : -10);
                    vol.Text = "Vol " + source.GetVolume(idx);
                    e.Handled = true;
                };

                _videoCells[i].Content = view;
                _videoOverlays[i] = overlay;
                _ovInfoBorder[i] = infoBorder;
                _ovRightStack[i] = rightStack;
                _ovInfoButton[i] = infoChip;
                _ovTitle[i] = title;
                _ovQrz[i] = qrz;
                _ovStream[i] = stream;
                _ovCodec[i] = codec;
                _ovState[i] = state;
                _ovVolume[i] = vol;
                _videoViews.Add(view);

                OTMediaPlayer player = useFfmpeg
                    ? (OTMediaPlayer)new OpenTuner.Wpf.Players.WpfFlyleafMediaPlayer((FlyleafLib.Controls.WPF.FlyleafHost)view)
                    : new OpenTuner.Wpf.Players.WpfVlcMediaPlayer((LibVLCSharp.WPF.VideoView)view);
                player.Initialize(source.GetVideoDataQueue(i), i);
                player.onVideoOut += Player_onVideoOut;
                _players.Add(player);
            }

            ApplyVideoLayout();

            source.ConfigureVideoPlayers(_players);
            source.ConfigureMediaPath(_settings.media_path);

            // honour "mute at startup"
            if (_settings.mute_at_startup)
            {
                try { source.OverrideDefaultMuted(true); } catch { }
            }

            // raw .ts recorders and UDP streamers (needed by the R / U media buttons)
            _recorders.Clear();
            for (int i = 0; i < n; i++)
            {
                var rec = new TSRecorder(_settings.media_video_path, i, source)
                {
                    FilenameTemplate = string.IsNullOrEmpty(_settings.record_filename_template)
                        ? "{callsign}_{service}_{freq}_{date}_{time}"
                        : _settings.record_filename_template,
                    WriteSidecar = _settings.record_sidecar,
                    MaxSizeBytes = _settings.record_max_mb > 0 ? _settings.record_max_mb * 1024L * 1024L : 0,
                    MaxDuration = _settings.record_max_minutes > 0 ? TimeSpan.FromMinutes(_settings.record_max_minutes) : TimeSpan.Zero
                };
                _recorders.Add(rec);
            }
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
                try { _batcControl?.Stop(); } catch { }
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

            ApplyLayoutPreset(_layoutPreset);
            StartSnapshotTimer();

            // signal history RX list
            try
            {
                if (_signalGraph != null) _signalGraph.Clear();
                comboSignalRx.Items.Clear();
                for (int i = 0; i < n; i++)
                    comboSignalRx.Items.Add("RX " + (i + 1));
                if (comboSignalRx.Items.Count > 0)
                    comboSignalRx.SelectedIndex = 0;
            }
            catch { }

            // refresh the QSO "From RX" buttons for the (re)connected tuner count
            try { _qsoLogControl?.Reload(); } catch { }

            return true;
        }

        private void SignalRx_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_signalGraph != null)
                _signalGraph.Tuner = Math.Max(0, comboSignalRx.SelectedIndex);
        }

        private void GraphSeries_Click(object sender, RoutedEventArgs e)
        {
            if (_signalGraph == null)
                return;

            _signalGraph.ShowMer = chkGraphMer.IsChecked == true;
            _signalGraph.ShowMargin = chkGraphMargin.IsChecked == true;
            _signalGraph.ShowBer = chkGraphBer.IsChecked == true;
            _signalGraph.InvalidateVisual();
        }

        private void GraphWindow_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_signalGraph == null)
                return;

            double[] secs = { 60, 120, 300, 600, 1800 };
            int i = comboGraphWindow.SelectedIndex;
            if (i >= 0 && i < secs.Length)
                _signalGraph.WindowSeconds = secs[i];
        }

        private void SignalClear_Click(object sender, RoutedEventArgs e)
        {
            _signalGraph?.Clear();
        }

        private void StartSnapshotTimer()
        {
            _snapshotTimer?.Stop();
            _snapshotTimer = null;

            int interval = _settings.snapshot_interval_seconds;
            if (interval <= 0)
                return;

            _snapshotTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
            _snapshotTimer.Tick += (s, e) => TakeSnapshot();
            _snapshotTimer.Start();
        }

        private void TakeSnapshot()
        {
            try
            {
                for (int i = 0; i < _players.Count; i++)
                {
                    string name = _settings.media_path + CommonFunctions.GenerateTimestampFilename() + "_rx" + (i + 1) + ".png";
                    _players[i].SnapShot(name);
                }
            }
            catch { }
        }

        private void Source_OnSourceData(int videoNr, OTSourceData properties, string description)
        {
            if (properties == null)
                return;

            _latestData = properties;

            try
            {
                if (videoNr >= 0 && videoNr < _latestByTuner.Length)
                    _latestByTuner[videoNr] = properties;
            }
            catch { }

            try { _signalGraph?.AddSample(videoNr, properties.mer, properties.db_margin, properties.ber, properties.demod_locked); } catch { }

            try { UpdateVideoOverlay(videoNr, properties); } catch { }

            try
            {
                int recIdx = videoNr;
                if (recIdx >= 0 && recIdx < _recorders.Count)
                    _recorders[recIdx].LatestData = properties;
            }
            catch { }

            try { _batcControl?.updateSignalCallsign(properties.service_name, properties.frequency, properties.symbol_rate); } catch { }

            // lock acquired / lost toast
            try
            {
                if (videoNr == 0)
                {
                    if (properties.demod_locked && !_lastLockState)
                        ToastService.Show(LocalizationManager.Get("mw.toast.locked") + (string.IsNullOrEmpty(properties.service_name) ? "" : ": " + properties.service_name), ToastKind.Success, 4);
                    else if (!properties.demod_locked && _lastLockState)
                        ToastService.Show(LocalizationManager.Get("mw.toast.lost"), ToastKind.Warning, 3);
                    _lastLockState = properties.demod_locked;
                }
            }
            catch { }

            // QRZ + stream info are shown in the per-video overlay (UpdateVideoOverlay)

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

        private async void LookupAndToast(string callsign)
        {
            try
            {
                if (_qrzClient == null)
                    _qrzClient = new opentuner.ExtraFeatures.QRZ.QrzClient(_qrzSettings);

                var r = await _qrzClient.LookupAsync(callsign);
                if (r.success)
                    ToastService.Show("QRZ " + r.callsign + (string.IsNullOrEmpty(r.name) ? "" : ": " + r.name) +
                                      (string.IsNullOrEmpty(r.country) ? "" : " (" + r.country + ")"), ToastKind.Info, 6);
            }
            catch { }
        }

        /// <summary>Updates the stream-info + QRZ overlay drawn on top of a video.</summary>
        private void UpdateVideoOverlay(int idx, OTSourceData d)
        {
            if (idx < 0 || idx >= 4 || d == null)
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_ovTitle[idx] == null)
                        return;

                    string svc = (d.service_name ?? "").Trim();
                    string prov = (d.service_provider ?? "").Trim();

                    // callsign from the service name, falling back to the service provider
                    string csSvc = opentuner.Utilities.CallsignParser.Extract(svc);
                    string csProv = opentuner.Utilities.CallsignParser.Extract(prov);
                    string callsign = !string.IsNullOrEmpty(csSvc) ? csSvc : csProv;

                    // show the callsign prominently (even when QRZ is disabled)
                    _ovTitle[idx].Text = !string.IsNullOrEmpty(callsign)
                        ? callsign
                        : (!string.IsNullOrEmpty(svc) ? svc : (d.demod_locked ? "Locked" : "No signal"));

                    var s = new System.Text.StringBuilder();
                    if (d.frequency > 0) s.Append((d.frequency / 1000.0).ToString("F3")).Append(" MHz   ");
                    if (d.symbol_rate > 0) s.Append(d.symbol_rate).Append(" kSym   ");
                    s.Append("SNR/MER ").Append(d.mer.ToString("F1")).Append(" dB   ");
                    s.Append("Margin ").Append(d.db_margin.ToString("F1")).Append(" dB");
                    if (d.ber > 0) s.Append("   BER ").Append(d.ber.ToString("0.#E+0"));
                    if (!string.IsNullOrEmpty(d.modcode)) s.Append("   ").Append(d.modcode);
                    _ovStream[idx].Text = s.ToString();

                    bool recording = idx < _recorders.Count && _recorders[idx] != null && _recorders[idx].record;
                    bool streaming = idx < _streamers.Count && _streamers[idx] != null && _streamers[idx].stream;

                    string st = d.demod_locked ? "LOCKED" : "NO LOCK";
                    if (recording) st += "   REC";
                    if (streaming) st += "   STREAM";
                    _ovState[idx].Text = st;

                    if (recording)
                        _ovState[idx].Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x6B));
                    else if (d.demod_locked)
                        _ovState[idx].Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6B, 0xE0, 0x8A));
                    else
                        _ovState[idx].Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0x7A, 0x7A));

                    // operator line: service name / provider (used as QRZ fallback text)
                    string info = svc;
                    if (!string.IsNullOrEmpty(prov) && prov != svc)
                        info = string.IsNullOrEmpty(info) ? prov : info + "  (" + prov + ")";
                    if (info == callsign)
                        info = "";

                    string key = svc + "|" + prov;
                    if (key != _ovLastService[idx])
                    {
                        _ovLastService[idx] = key;

                        if (_qrzSettings != null && _qrzSettings.enabled && !string.IsNullOrEmpty(callsign))
                        {
                            _ovQrz[idx].Text = "QRZ...";
                            DoQrzOverlay(idx, csSvc ?? callsign, csProv, info);
                        }
                        else
                        {
                            // QRZ disabled - still show the operator / service info
                            _ovQrz[idx].Text = info;
                        }
                    }
                }
                catch { }
            }));
        }

        /// <summary>
        /// Looks the callsign up on QRZ, falling back to the service-provider
        /// callsign if the service-name lookup fails. On total failure it shows
        /// the supplied operator text instead.
        /// </summary>
        private async void DoQrzOverlay(int idx, string primary, string secondary, string fallback)
        {
            try
            {
                if (_qrzClient == null)
                    _qrzClient = new opentuner.ExtraFeatures.QRZ.QrzClient(_qrzSettings);

                opentuner.ExtraFeatures.QRZ.QrzResult r = null;

                if (!string.IsNullOrEmpty(primary))
                    r = await _qrzClient.LookupAsync(primary);

                if ((r == null || !r.success) && !string.IsNullOrEmpty(secondary) && secondary != primary)
                    r = await _qrzClient.LookupAsync(secondary);

                string text = (r != null && r.success)
                    ? (string.IsNullOrEmpty(r.name) ? r.callsign : r.name) +
                      (string.IsNullOrEmpty(r.country) ? "" : " (" + r.country + ")")
                    : (fallback ?? "");

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (idx >= 0 && idx < 4 && _ovQrz[idx] != null)
                        _ovQrz[idx].Text = text;
                }));
            }
            catch { }
        }

        private void UpdateStatusBar()
        {
            try
            {
                string L(string k) => LocalizationManager.Get(k);

                if (_connectedSource == null)
                {
                    statusSource.Text = L("mw.status.source") + ": -";
                    statusDevice.Text = L("mw.status.device") + ": -";
                    statusLock.Text = L("mw.status.lock") + ": -";
                    statusLock.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
                    statusDetail.Text = "";
                    statusRec.Text = "";
                    statusStream.Text = "";
                    statusDisk.Text = L("mw.status.disk") + ": -";
                    return;
                }

                statusSource.Text = L("mw.status.source") + ": " + _connectedSource.GetName();
                statusDevice.Text = L("mw.status.device") + ": " + _connectedSource.GetDeviceName();

                var d = _latestData;
                bool locked = d != null && d.demod_locked;
                statusLock.Text = L("mw.status.lock") + ": " + L(locked ? "mw.status.yes" : "mw.status.no");
                statusLock.Foreground = locked
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3D, 0xB0, 0x6B))
                    : (System.Windows.Media.Brush)FindResource("TextSecondary");

                if (d != null)
                    statusDetail.Text = (string.IsNullOrEmpty(d.service_name) ? "" : d.service_name + "  ") +
                                        (d.frequency > 0 ? (d.frequency / 1000.0).ToString("F3") + " MHz  " : "") +
                                        (d.symbol_rate > 0 ? d.symbol_rate + " kSym  " : "") +
                                        "MER " + d.mer.ToString("F1") + "  Margin " + d.db_margin.ToString("F1");

                bool rec = _recorders.Exists(r => r.record);
                bool stream = _streamers.Exists(s => s.stream);
                statusRec.Text = rec ? "REC" : "";
                statusRec.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD9, 0x53, 0x4F));
                statusStream.Text = stream ? "STREAM" : "";
                statusStream.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0x90, 0xD9));

                // keep the per-video overlay volume in sync with the real volume
                for (int i = 0; i < _players.Count && i < 4; i++)
                {
                    if (_ovVolume[i] == null)
                        continue;
                    int v = _connectedSource.GetVolume(i);
                    if (v >= 0)
                        _ovVolume[i].Text = "Vol " + v;
                }

                if (rec && !_lastRecState)
                    ToastService.Show(L("mw.toast.recstart"), ToastKind.Success, 3);
                else if (!rec && _lastRecState)
                    ToastService.Show(L("mw.toast.recstop"), ToastKind.Info, 3);
                _lastRecState = rec;

                try
                {
                    string root = Path.GetPathRoot(_settings.media_video_path);
                    if (!string.IsNullOrEmpty(root))
                    {
                        var drive = new DriveInfo(root);
                        statusDisk.Text = L("mw.status.disk") + ": " + (drive.AvailableFreeSpace / (1024.0 * 1024 * 1024)).ToString("F1") + " GB " + L("mw.status.free");
                    }
                }
                catch { }
            }
            catch { }
        }

        private void BuildVideoLayout(int n)
        {
            videoArea.Children.Clear();
            videoArea.RowDefinitions.Clear();
            videoArea.ColumnDefinitions.Clear();
            _videoCells.Clear();

            // fixed 2x2 grid; the layout preset changes spans/visibility only, so
            // the video views (and their LibVLC overlay windows) are never rebuilt.
            videoArea.RowDefinitions.Add(new RowDefinition());
            videoArea.RowDefinitions.Add(new RowDefinition());
            videoArea.ColumnDefinitions.Add(new ColumnDefinition());
            videoArea.ColumnDefinitions.Add(new ColumnDefinition());

            for (int i = 0; i < 4; i++)
            {
                var cell = new ContentControl { Margin = new Thickness(1) };
                Grid.SetRow(cell, 0);
                Grid.SetColumn(cell, 0);
                videoArea.Children.Add(cell);
                _videoCells.Add(cell);
            }
        }

        private void Player_onVideoOut(object sender, MediaStatus status)
        {
            try
            {
                int id = ((OTMediaPlayer)sender).getID();
                if (id < 0 || id >= 4 || _ovCodec[id] == null)
                    return;

                string video = (string.IsNullOrEmpty(status.VideoCodec) ? "" : status.VideoCodec) +
                               (status.VideoWidth > 0 ? " " + status.VideoWidth + "x" + status.VideoHeight : "");
                string audio = (string.IsNullOrEmpty(status.AudioCodec) ? "" : status.AudioCodec) +
                               (status.AudioChannels > 0 ? " " + status.AudioChannels + "ch" : "");

                string text = video;
                if (!string.IsNullOrEmpty(audio))
                    text += (string.IsNullOrEmpty(text) ? "" : "   ") + audio;

                Dispatcher.Invoke(() => _ovCodec[id].Text = text.Trim());
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

                // auto-record: start the raw .ts recorder as soon as streaming starts
                if (_settings.auto_record && i < _recorders.Count && _recorders[i] != null)
                    _recorders[i].record = true;
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
            if (menuHighContrast != null) menuHighContrast.IsChecked = false;
            ThemeManager.Apply(menuDarkMode.IsChecked);
            try { _signalGraph?.InvalidateVisual(); } catch { }
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
            try
            {
                if (_mqtt == null)
                    _mqtt = new MqttManager();

                new OpenTuner.Wpf.Dialogs.PlutoControlWindow(_mqtt, _connectedSource) { Owner = this }.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Pluto control failed: " + ex.Message, "Open Tuner");
            }
        }

        private void WebChat_Click(object sender, RoutedEventArgs e)
        {
            if (_connectedSource == null)
            {
                MessageBox.Show(LocalizationManager.Get("msg.connectsource"), "Open Tuner");
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
                MessageBox.Show(LocalizationManager.Get("msg.connectsource"), "Open Tuner");
                return;
            }

            var win = new OpenTuner.Wpf.Dialogs.TuneWindow(_connectedSource) { Owner = this };
            win.ShowDialog();
        }

        private void DebugLog_Click(object sender, RoutedEventArgs e)
        {
            new OpenTuner.Wpf.Dialogs.DebugWindow { Owner = this }.Show();
        }

        private bool _toolsHidden;
        private bool _propsHidden;

        private void HideProps_Click(object sender, RoutedEventArgs e)
        {
            // collapse the whole left column (it holds the source/properties panel)
            bool willShow = leftPanel.Visibility != Visibility.Visible;
            _propsHidden = !willShow;

            if (willShow)
            {
                leftColumn.MinWidth = 300;
                leftColumn.Width = new GridLength(340);
                leftSplitter.Visibility = Visibility.Visible;
                leftPanel.Visibility = Visibility.Visible;
            }
            else
            {
                leftColumn.MinWidth = 0;
                leftColumn.Width = new GridLength(0);
                leftSplitter.Visibility = Visibility.Collapsed;
                leftPanel.Visibility = Visibility.Collapsed;
            }

            menuHideProps.Header = LocalizationManager.Get(willShow ? "mw.menu.hideprops" : "mw.menu.showprops");
        }

        private void HideSpectrum_Click(object sender, RoutedEventArgs e)
        {
            _toolsHidden = !_toolsHidden;

            // bottomRow has MinHeight=140 in XAML - clear it or a 0 height is clamped
            bottomRow.MinHeight = _toolsHidden ? 0 : 140;
            bottomRow.Height = _toolsHidden ? new GridLength(0) : new GridLength(260);
            bottomSplitterRow.Height = _toolsHidden ? new GridLength(0) : new GridLength(4);
            bottomSplitter.Visibility = _toolsHidden ? Visibility.Collapsed : Visibility.Visible;
            tabsTools.Visibility = _toolsHidden ? Visibility.Collapsed : Visibility.Visible;

            menuHideTools.Header = LocalizationManager.Get(_toolsHidden ? "mw.menu.showtools" : "mw.menu.hidetools");
        }

        private void Docs_Click(object sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start("https://www.zr6tg.co.za/opentuner-documentation/"); } catch { }
        }

        private void QrzToggled(object sender, RoutedEventArgs e)
        {
            if (_qrzSettings == null)
                _qrzSettings = new opentuner.ExtraFeatures.QRZ.QrzSettings();

            _qrzSettings.enabled = chkQrz.IsChecked == true;
            _qrzClient = null;

            var mgr = new SettingsManager<opentuner.ExtraFeatures.QRZ.QrzSettings>("qrz_settings");
            try { mgr.SaveSettings(_qrzSettings); } catch { }
        }

        private void QrzSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_qrzSettings == null)
                _qrzSettings = new opentuner.ExtraFeatures.QRZ.QrzSettings();

            if (new OpenTuner.Wpf.Dialogs.QrzWindow(_qrzSettings) { Owner = this }.ShowDialog() == true)
            {
                _qrzClient = null;
                chkQrz.IsChecked = _qrzSettings.enabled;
                var mgr = new SettingsManager<opentuner.ExtraFeatures.QRZ.QrzSettings>("qrz_settings");
                try { mgr.SaveSettings(_qrzSettings); } catch { }
            }
        }

        private void HighContrast_Click(object sender, RoutedEventArgs e)
        {
            if (menuHighContrast.IsChecked)
            {
                menuDarkMode.IsChecked = false;
                ThemeManager.ApplyHighContrast(true);
            }
            else
            {
                ThemeManager.Apply(menuDarkMode.IsChecked);
            }
            try { _signalGraph?.InvalidateVisual(); } catch { }
        }

        private void BuildLanguageMenu()
        {
            menuLanguage.Items.Clear();

            foreach (var (code, name) in LocalizationManager.Languages)
            {
                var item = new MenuItem
                {
                    Header = name,
                    Tag = code,
                    IsCheckable = true,
                    IsChecked = code == LocalizationManager.CurrentLanguage
                };
                item.Click += Language_Click;
                menuLanguage.Items.Add(item);
            }
        }

        private void Language_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            string lang = mi?.Tag as string ?? "en";

            LocalizationManager.Apply(lang);

            // update the check marks
            foreach (var obj in menuLanguage.Items)
                if (obj is MenuItem m)
                    m.IsChecked = (m.Tag as string) == lang;

            _settings.language = lang;
            SaveSettings();

            // these two headers are set in code (not DynamicResource) - refresh them
            try
            {
                menuHideProps.Header = LocalizationManager.Get(leftPanel.Visibility == Visibility.Visible ? "mw.menu.hideprops" : "mw.menu.showprops");
                menuHideTools.Header = LocalizationManager.Get(_toolsHidden ? "mw.menu.showtools" : "mw.menu.hidetools");
            }
            catch { }
        }

        private void Shortcuts_Click(object sender, RoutedEventArgs e)
        {
            new OpenTuner.Wpf.Dialogs.ShortcutsWindow { Owner = this }.ShowDialog();
        }

        private void BandScan_Click(object sender, RoutedEventArgs e)
        {
            if (_connectedSource == null)
            {
                MessageBox.Show(LocalizationManager.Get("msg.connectsource"), "Open Tuner");
                return;
            }

            var win = new OpenTuner.Wpf.Dialogs.BandScanWindow(_connectedSource, _latestData) { Owner = this };
            win.Show();
        }

        private void Beacons_Click(object sender, RoutedEventArgs e)
        {
            var win = new OpenTuner.Wpf.Dialogs.BeaconWindow(_connectedSource) { Owner = this };
            win.ShowDialog();
        }

        private OpenTuner.Wpf.Dialogs.QsoLogControl _qsoLogControl;
        private OpenTuner.Wpf.Dialogs.QsoLogWindow _qsoLogWindow;

        private void QsoLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_qsoLogWindow == null)
                {
                    _qsoLogWindow = new OpenTuner.Wpf.Dialogs.QsoLogWindow(_settings, QsoRxCount, CurrentSignalForQso, SignalInfoForQso) { Owner = this };
                    _qsoLogWindow.Closed += (s, ev) => _qsoLogWindow = null;
                }
                _qsoLogWindow.Show();
                _qsoLogWindow.Activate();
            }
            catch (Exception ex)
            {
                MessageBox.Show("QSO log failed: " + ex.Message, "Open Tuner");
            }
        }

        private void EnsureQsoControl()
        {
            if (_qsoLogControl != null)
            {
                _qsoLogControl.Reload();
                return;
            }

            _qsoLogControl = new OpenTuner.Wpf.Dialogs.QsoLogControl(_settings, QsoRxCount, CurrentSignalForQso, SignalInfoForQso);
            qsoHost.Content = _qsoLogControl;
        }

        private void TabsTools_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (e.OriginalSource == tabsTools && tabsTools.SelectedItem == tabQso)
                    EnsureQsoControl();
            }
            catch { }
        }

        /// <summary>Current tuned signal's callsign + frequency for QSO auto-fill (per RX).</summary>
        private (string call, double freqMhz) CurrentSignalForQso(int rx)
        {
            OTSourceData d = (rx >= 0 && rx < _latestByTuner.Length) ? _latestByTuner[rx] : _latestData;
            if (d == null)
                return ("", 0);

            string svc = (d.service_name ?? "").Trim();
            string prov = (d.service_provider ?? "").Trim();
            string call = opentuner.Utilities.CallsignParser.Extract(svc)
                          ?? opentuner.Utilities.CallsignParser.Extract(prov)
                          ?? "";

            double freq = d.frequency > 0 ? d.frequency / 1000.0 : 0;
            return (call, freq);
        }

        /// <summary>Formatted signal details for a tuner, for the QSO comment.</summary>
        private string SignalInfoForQso(int rx)
        {
            OTSourceData d = (rx >= 0 && rx < _latestByTuner.Length) ? _latestByTuner[rx] : _latestData;
            if (d == null)
                return "";

            string svc = (d.service_name ?? "").Trim();
            string prov = (d.service_provider ?? "").Trim();
            string call = opentuner.Utilities.CallsignParser.Extract(svc)
                          ?? opentuner.Utilities.CallsignParser.Extract(prov)
                          ?? "";

            var sb = new System.Text.StringBuilder();
            sb.Append("RX").Append(rx + 1).Append(": ");
            if (!string.IsNullOrEmpty(call)) sb.Append(call).Append(' ');
            if (!string.IsNullOrEmpty(svc)) sb.Append('"').Append(svc).Append('"').Append(' ');
            if (!string.IsNullOrEmpty(prov)) sb.Append('(').Append(prov).Append(") ");
            if (d.frequency > 0) sb.Append((d.frequency / 1000.0).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append(" MHz  ");
            if (d.symbol_rate > 0) sb.Append(d.symbol_rate).Append(" kSym  ");
            sb.Append("SNR/MER ").Append(d.mer.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append(" dB  ");
            sb.Append("Margin ").Append(d.db_margin.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append(" dB");
            if (d.ber > 0) sb.Append("  BER ").Append(d.ber.ToString("0.#E+0", System.Globalization.CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(d.modcode)) sb.Append("  ").Append(d.modcode);

            return sb.ToString().Trim();
        }

        /// <summary>Number of tuners/RXs available from the connected source.</summary>
        private int QsoRxCount()
        {
            try
            {
                if (_connectedSource != null)
                    return Math.Max(1, _connectedSource.GetVideoSourceCount());
            }
            catch { }
            return 2;
        }

        private void RecordManager_Click(object sender, RoutedEventArgs e)
        {
            var win = new OpenTuner.Wpf.Dialogs.RecordManagerWindow(_settings.media_video_path) { Owner = this };
            win.Show();
        }

        private void Gallery_Click(object sender, RoutedEventArgs e)
        {
            var win = new OpenTuner.Wpf.Dialogs.GalleryWindow(_settings.media_path) { Owner = this };
            win.Show();
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





