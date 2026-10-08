using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Newtonsoft.Json.Linq;

namespace OpenTuner.Wpf.Dialogs
{
    /// <summary>
    /// Browses recorded .ts files and plays them back with embedded LibVLC.
    /// Signal metadata (callsign/service) is read from the .json sidecar when present.
    /// </summary>
    public class RecordManagerWindow : Window
    {
        private readonly string _folder;

        private ListView _list;
        private LibVLCSharp.WPF.VideoView _video;
        private LibVLC _libVlc;
        private LibVLCSharp.Shared.MediaPlayer _player;
        private Media _media;

        private TextBlock _info;
        private Slider _position;
        private TextBlock _time;
        private bool _seeking;

        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };

        private class RecItem
        {
            public string Path;
            public string Name;
            public string Size;
            public string Date;
            public string Meta;
            public override string ToString() => Name + "   [" + Size + "]  " + Date + (string.IsNullOrEmpty(Meta) ? "" : "   " + Meta);
        }

        public RecordManagerWindow(string folder)
        {
            _folder = folder;

            Title = LocalizationManager.Get("dt.recordmanager");
            Width = 980; Height = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)Application.Current.FindResource("WindowBackground");
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            try
            {
                _libVlc = new LibVLC("--no-video-title-show");
                _player = new LibVLCSharp.Shared.MediaPlayer(_libVlc);
            }
            catch { }

            BuildUi();

            _timer.Tick += (s, e) => UpdatePosition();
            _timer.Start();

            Loaded += (s, e) => Refresh();
            Closed += (s, e) =>
            {
                _timer.Stop();
                try { _player?.Stop(); } catch { }
                try { _media?.Dispose(); } catch { }
                try { _player?.Dispose(); } catch { }
                try { _libVlc?.Dispose(); } catch { }
            };
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
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var refresh = new Button { Content = LocalizationManager.Get("btn.refresh"), Margin = new Thickness(0, 0, 8, 0) };
            refresh.Click += (s, e) => Refresh();
            var openFolder = new Button { Content = "Open folder", Margin = new Thickness(0, 0, 8, 0) };
            openFolder.Click += (s, e) => OpenFolder();
            var del = new Button { Content = LocalizationManager.Get("btn.delete"), Margin = new Thickness(0, 0, 8, 0) };
            del.Click += (s, e) => DeleteSelected();
            var playExt = new Button { Content = "Open externally" };
            playExt.Click += (s, e) => OpenSelectedExternally();
            sp.Children.Add(refresh);
            sp.Children.Add(openFolder);
            sp.Children.Add(del);
            sp.Children.Add(playExt);
            top.Child = sp;
            dock.Children.Add(top);

            var bottom = new Border
            {
                Background = (Brush)Application.Current.FindResource("SurfaceBackground"),
                BorderBrush = (Brush)Application.Current.FindResource("Border"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 8, 12, 8)
            };
            DockPanel.SetDock(bottom, Dock.Bottom);
            var bsp = new StackPanel { Orientation = Orientation.Horizontal };
            var play = new Button { Content = LocalizationManager.Get("btn.play"), Width = 80, Margin = new Thickness(0, 0, 8, 0) };
            play.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
            play.Click += (s, e) => PlaySelected();
            var pause = new Button { Content = LocalizationManager.Get("btn.pause"), Width = 80, Margin = new Thickness(0, 0, 8, 0) };
            pause.Click += (s, e) => { try { _player?.Pause(); } catch { } };
            var stop = new Button { Content = LocalizationManager.Get("btn.stop"), Width = 80, Margin = new Thickness(0, 0, 16, 0) };
            stop.Click += (s, e) => { try { _player?.Stop(); } catch { } };
            _position = new Slider { Width = 380, VerticalAlignment = VerticalAlignment.Center };
            _position.PreviewMouseDown += (s, e) => _seeking = true;
            _position.PreviewMouseUp += (s, e) => _seeking = false;
            _position.ValueChanged += (s, e) => Seek();
            _time = new TextBlock { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.FindResource("TextSecondary") };
            bsp.Children.Add(play);
            bsp.Children.Add(pause);
            bsp.Children.Add(stop);
            bsp.Children.Add(_position);
            bsp.Children.Add(_time);
            bottom.Child = bsp;
            dock.Children.Add(bottom);

            var main = new Grid();
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _list = new ListView { Margin = new Thickness(8) };
            _list.MouseDoubleClick += (s, e) => PlaySelected();
            Grid.SetColumn(_list, 0);
            main.Children.Add(_list);

            var splitter = new GridSplitter { Width = 4, HorizontalAlignment = HorizontalAlignment.Stretch, Background = (Brush)Application.Current.FindResource("Border") };
            Grid.SetColumn(splitter, 1);
            main.Children.Add(splitter);

            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _video = new LibVLCSharp.WPF.VideoView { Background = Brushes.Black };
            if (_player != null)
                _video.MediaPlayer = _player;
            Grid.SetRow(_video, 0);
            right.Children.Add(_video);

            _info = new TextBlock { Padding = new Thickness(8), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.FindResource("TextSecondary") };
            Grid.SetRow(_info, 1);
            right.Children.Add(_info);

            Grid.SetColumn(right, 2);
            main.Children.Add(right);

            dock.Children.Add(main);
            Content = dock;
        }

        private void Refresh()
        {
            _list.Items.Clear();

            try
            {
                if (!Directory.Exists(_folder))
                {
                    _info.Text = "Folder does not exist: " + _folder;
                    return;
                }

                var files = Directory.GetFiles(_folder, "*.ts").OrderByDescending(f => File.GetLastWriteTime(f));
                foreach (var f in files)
                {
                    var fi = new FileInfo(f);
                    string meta = "";
                    try
                    {
                        string sidecar = f + ".json";
                        if (File.Exists(sidecar))
                        {
                            var o = JObject.Parse(File.ReadAllText(sidecar));
                            string call = o["callsign"]?.ToString();
                            string svc = o["service_name"]?.ToString();
                            string mhz = o["frequency_mhz"]?.ToString();
                            meta = (string.IsNullOrEmpty(call) ? svc : call) + (string.IsNullOrEmpty(mhz) ? "" : " @ " + mhz + " MHz");
                        }
                    }
                    catch { }

                    _list.Items.Add(new RecItem
                    {
                        Path = f,
                        Name = fi.Name,
                        Size = (fi.Length / (1024.0 * 1024)).ToString("F1", CultureInfo.InvariantCulture) + " MB",
                        Date = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                        Meta = meta
                    });
                }

                if (_list.Items.Count == 0)
                    _info.Text = "No recordings in " + _folder;
            }
            catch (Exception ex)
            {
                _info.Text = "Refresh failed: " + ex.Message;
            }
        }

        private void PlaySelected()
        {
            if (!(_list.SelectedItem is RecItem item) || _player == null)
                return;

            try
            {
                _player.Stop();
                _media?.Dispose();
                _media = new Media(_libVlc, item.Path, FromType.FromPath);
                _player.Play(_media);
                _info.Text = item.Name;
            }
            catch (Exception ex)
            {
                _info.Text = "Playback failed: " + ex.Message;
            }
        }

        private void UpdatePosition()
        {
            try
            {
                if (_player == null || !_player.IsPlaying || _media == null || _seeking)
                    return;

                long len = _media.Duration;
                if (len > 0)
                {
                    _position.Maximum = len;
                    _position.Value = _player.Time;
                    _time.Text = TimeSpan.FromMilliseconds(Math.Max(0, _player.Time)).ToString(@"mm\:ss") +
                                 " / " + TimeSpan.FromMilliseconds(len).ToString(@"mm\:ss");
                }
            }
            catch { }
        }

        private void Seek()
        {
            try
            {
                if (_player != null && _seeking)
                    _player.Time = (long)_position.Value;
            }
            catch { }
        }

        private void DeleteSelected()
        {
            if (!(_list.SelectedItem is RecItem item))
                return;

            if (MessageBox.Show("Delete " + item.Name + "?", "Record Manager", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                if (_player != null && _player.IsPlaying)
                    _player.Stop();
                File.Delete(item.Path);
                string sidecar = item.Path + ".json";
                if (File.Exists(sidecar)) File.Delete(sidecar);
                Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Delete failed: " + ex.Message, "Record Manager");
            }
        }

        private void OpenSelectedExternally()
        {
            if (!(_list.SelectedItem is RecItem item))
                return;

            try { Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true }); } catch { }
        }

        private void OpenFolder()
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + _folder + "\"")); } catch { }
        }
    }
}
