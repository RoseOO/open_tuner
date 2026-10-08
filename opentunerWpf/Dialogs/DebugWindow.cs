using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace OpenTuner.Wpf.Dialogs
{
    /// <summary>Native WPF debug / log window (replaces the hidden debug page).</summary>
    public class DebugWindow : Window
    {
        private readonly TextBox _log = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            Height = 460
        };

        private readonly DispatcherTimer _timer;
        private string _currentFile = "";
        private long _readPos;

        public DebugWindow()
        {
            Title = LocalizationManager.Get("dt.debug");
            Width = 760; Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)Application.Current.FindResource("WindowBackground");
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

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
            var refresh = new Button { Content = LocalizationManager.Get("btn.refresh"), Width = 96, Margin = new Thickness(0, 0, 10, 0) };
            refresh.Click += (s, e) => { _readPos = 0; _currentFile = ""; _log.Clear(); Tail(); };
            var open = new Button { Content = "Open Folder", Width = 110, Margin = new Thickness(0, 0, 10, 0) };
            open.Click += (s, e) => { try { System.Diagnostics.Process.Start(LogDir()); } catch { } };
            var close = new Button { Content = LocalizationManager.Get("btn.close"), Width = 96 };
            close.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
            close.Click += (s, e) => Close();
            sp.Children.Add(refresh);
            sp.Children.Add(open);
            sp.Children.Add(close);
            buttons.Child = sp;
            dock.Children.Add(buttons);

            _log.Margin = new Thickness(12);
            dock.Children.Add(_log);

            Content = dock;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => Tail();
            _timer.Start();
            Closed += (s, e) => _timer.Stop();

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
