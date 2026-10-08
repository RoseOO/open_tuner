using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace OpenTuner.Wpf.Dialogs
{
    /// <summary>Reusable live log tail view (used by the Debug tab and Debug window).</summary>
    public class LogViewControl : UserControl
    {
        private readonly TextBox _log = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Margin = new Thickness(8)
        };

        private readonly DispatcherTimer _timer;
        private string _currentFile = "";
        private long _readPos;

        public LogViewControl()
        {
            // the global TextBox style sets Height=30 (single line); opt out so the
            // log fills the whole area.
            _log.Height = double.NaN;
            _log.MinHeight = 120;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var bar = new Border
            {
                Padding = new Thickness(8, 6, 8, 6),
                Background = (Brush)Application.Current.FindResource("SurfaceBackground")
            };
            Grid.SetRow(bar, 0);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var refresh = new Button { Content = LocalizationManager.Get("btn.refresh"), Width = 84, Margin = new Thickness(0, 0, 8, 0) };
            refresh.Click += (s, e) => { _readPos = 0; _currentFile = ""; _log.Clear(); Tail(); };
            var open = new Button { Content = "Open log folder", Width = 130, Margin = new Thickness(0, 0, 8, 0) };
            open.Click += (s, e) => { try { System.Diagnostics.Process.Start(LogDir()); } catch { } };
            var clear = new Button { Content = "Clear view", Width = 96 };
            clear.Click += (s, e) => _log.Clear();
            sp.Children.Add(refresh);
            sp.Children.Add(open);
            sp.Children.Add(clear);
            bar.Child = sp;

            Grid.SetRow(_log, 1);
            _log.VerticalAlignment = VerticalAlignment.Stretch;

            grid.Children.Add(bar);
            grid.Children.Add(_log);
            Content = grid;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => Tail();
            _timer.Start();
            Unloaded += (s, e) => _timer.Stop();

            Tail();
        }

        private static string LogDir() => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

        private void Tail()
        {
            try
            {
                string dir = LogDir();
                if (!Directory.Exists(dir))
                    return;

                var newest = new DirectoryInfo(dir).GetFiles("*.txt").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
                if (newest == null)
                    return;

                if (newest.FullName != _currentFile)
                {
                    _currentFile = newest.FullName;
                    _readPos = 0;
                    _log.Clear();
                }

                using (var fs = new FileStream(_currentFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (_readPos > fs.Length) _readPos = 0;
                    fs.Seek(_readPos, SeekOrigin.Begin);
                    using (var sr = new StreamReader(fs))
                    {
                        string text = sr.ReadToEnd();
                        if (text.Length > 0)
                        {
                            _log.AppendText(text);
                            _log.ScrollToEnd();
                        }
                        _readPos = fs.Position;
                    }
                }
            }
            catch { }
        }
    }
}
