using System.Windows;
using System.Windows.Controls;
using opentuner;

namespace OpenTuner.Wpf.Dialogs
{
    /// <summary>Native WPF replacement for the WinForms settingsForm.</summary>
    public class SettingsWindow : Window
    {
        private readonly MainSettings _settings;

        private ComboBox comboDefaultSource;
        private ComboBox[] comboPlayer = new ComboBox[4];
        private CheckBox[] chkWindowed = new CheckBox[4];
        private TextBox[] txtHost = new TextBox[4];
        private TextBox[] txtPort = new TextBox[4];
        private TextBox txtSnapshotPath;
        private TextBox txtVideoPath;
        private CheckBox chkMuted;
        private CheckBox chkAutoRecord;

        public SettingsWindow(MainSettings settings)
        {
            _settings = settings;

            Title = "Open Tuner Settings";
            Width = 560; Height = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (System.Windows.Media.Brush)Application.Current.FindResource("WindowBackground");
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            FontSize = 13;

            BuildUi();
            LoadValues();
        }

        private StackPanel _content;

        private void BuildUi()
        {
            var dock = new DockPanel();

            var buttons = new Border
            {
                Background = (System.Windows.Media.Brush)Application.Current.FindResource("SurfaceBackground"),
                BorderBrush = (System.Windows.Media.Brush)Application.Current.FindResource("Border"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 10, 16, 10)
            };
            DockPanel.SetDock(buttons, Dock.Bottom);

            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "Cancel", Width = 96, Margin = new Thickness(0, 0, 10, 0) };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            var save = new Button { Content = "Save", Width = 96 };
            save.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
            save.Click += (s, e) => Save_Click();
            sp.Children.Add(cancel);
            sp.Children.Add(save);
            buttons.Child = sp;

            dock.Children.Add(buttons);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) };
            _content = new StackPanel();
            scroll.Content = _content;
            dock.Children.Add(scroll);

            Content = dock;

            comboDefaultSource = new ComboBox();
            comboDefaultSource.Items.Add("Minitiouner Variant");
            comboDefaultSource.Items.Add("Longmynd Variant");
            comboDefaultSource.Items.Add("WinterHill Variant");
            AddRow("Default source", comboDefaultSource);

            for (int i = 0; i < 4; i++)
            {
                comboPlayer[i] = new ComboBox();
                comboPlayer[i].Items.Add("VLC");
                comboPlayer[i].Items.Add("FFMPEG");
                comboPlayer[i].Items.Add("MPV");
                AddRow("Video " + (i + 1) + " player", comboPlayer[i]);

                chkWindowed[i] = new CheckBox { Content = "Separate window" };
                AddRow("", chkWindowed[i]);

                var hb = new StackPanel { Orientation = Orientation.Horizontal };
                txtHost[i] = new TextBox { Width = 150 };
                txtPort[i] = new TextBox { Width = 80, Margin = new Thickness(8, 0, 0, 0) };
                hb.Children.Add(txtHost[i]);
                hb.Children.Add(txtPort[i]);
                AddRow("Stream " + (i + 1) + " UDP host:port", hb);
            }

            txtSnapshotPath = new TextBox();
            AddRow("Snapshot path", txtSnapshotPath);

            txtVideoPath = new TextBox();
            AddRow("Video / recording path", txtVideoPath);

            chkMuted = new CheckBox { Content = "Mute at startup" };
            AddRow("", chkMuted);

            chkAutoRecord = new CheckBox { Content = "Auto-record streams to raw .ts" };
            AddRow("", chkAutoRecord);
        }

        private void AddRow(string label, FrameworkElement control)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lbl = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextSecondary")
            };
            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(control, 1);
            g.Children.Add(lbl);
            g.Children.Add(control);
            _content.Children.Add(g);
        }

        private void LoadValues()
        {
            comboDefaultSource.SelectedIndex = _settings.default_source >= 0 && _settings.default_source < comboDefaultSource.Items.Count ? _settings.default_source : 0;

            for (int i = 0; i < 4; i++)
            {
                comboPlayer[i].SelectedIndex = _settings.mediaplayer_preferences[i] >= 0 && _settings.mediaplayer_preferences[i] < 3 ? _settings.mediaplayer_preferences[i] : 0;
                chkWindowed[i].IsChecked = _settings.mediaplayer_windowed[i];
                txtHost[i].Text = _settings.streamer_udp_hosts[i];
                txtPort[i].Text = _settings.streamer_udp_ports[i].ToString();
            }

            txtSnapshotPath.Text = _settings.media_path;
            txtVideoPath.Text = _settings.media_video_path;
            chkMuted.IsChecked = _settings.mute_at_startup;
            chkAutoRecord.IsChecked = _settings.auto_record;
        }

        private void Save_Click()
        {
            _settings.default_source = comboDefaultSource.SelectedIndex;

            for (int i = 0; i < 4; i++)
            {
                _settings.mediaplayer_preferences[i] = comboPlayer[i].SelectedIndex;
                _settings.mediaplayer_windowed[i] = chkWindowed[i].IsChecked == true;
                _settings.streamer_udp_hosts[i] = txtHost[i].Text;
                if (int.TryParse(txtPort[i].Text, out int p))
                    _settings.streamer_udp_ports[i] = p;
            }

            _settings.media_path = txtSnapshotPath.Text;
            _settings.media_video_path = txtVideoPath.Text;
            _settings.mute_at_startup = chkMuted.IsChecked == true;
            _settings.auto_record = chkAutoRecord.IsChecked == true;

            DialogResult = true;
            Close();
        }
    }
}
