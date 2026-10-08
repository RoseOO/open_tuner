using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenTuner.Wpf.Dialogs
{
    /// <summary>Thumbnail gallery of snapshots in the media path.</summary>
    public class GalleryWindow : Window
    {
        private readonly string _folder;
        private WrapPanel _wrap;
        private TextBlock _status;

        public GalleryWindow(string folder)
        {
            _folder = folder;

            Title = LocalizationManager.Get("dt.gallery");
            Width = 900; Height = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)Application.Current.FindResource("WindowBackground");
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

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
            var openFolder = new Button { Content = "Open folder" };
            openFolder.Click += (s, e) => { try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + _folder + "\"")); } catch { } };
            sp.Children.Add(refresh);
            sp.Children.Add(openFolder);
            top.Child = sp;
            dock.Children.Add(top);

            _status = new TextBlock { Padding = new Thickness(12, 6, 12, 6), Foreground = (Brush)Application.Current.FindResource("TextSecondary") };
            DockPanel.SetDock(_status, Dock.Bottom);
            dock.Children.Add(_status);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12) };
            _wrap = new WrapPanel();
            scroll.Content = _wrap;
            dock.Children.Add(scroll);

            Content = dock;

            Loaded += (s, e) => Refresh();
        }

        private void Refresh()
        {
            _wrap.Children.Clear();

            try
            {
                if (!Directory.Exists(_folder))
                {
                    _status.Text = "Folder does not exist: " + _folder;
                    return;
                }

                var files = Directory.GetFiles(_folder, "*.png")
                    .Concat(Directory.GetFiles(_folder, "*.jpg"))
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .ToList();

                foreach (var f in files)
                {
                    var img = new Image
                    {
                        Width = 200,
                        Height = 120,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(4),
                        ToolTip = Path.GetFileName(f)
                    };

                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.UriSource = new Uri(f);
                        bmp.DecodePixelWidth = 200;
                        bmp.EndInit();
                        bmp.Freeze();
                        img.Source = bmp;
                    }
                    catch { }

                    var border = new Border
                    {
                        Background = (Brush)Application.Current.FindResource("SurfaceBackground"),
                        BorderBrush = (Brush)Application.Current.FindResource("Border"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(2),
                        Margin = new Thickness(4),
                        Child = img,
                        ToolTip = Path.GetFileName(f) + "\n" + File.GetLastWriteTime(f).ToString("yyyy-MM-dd HH:mm:ss")
                    };

                    string path = f;
                    border.MouseLeftButtonDown += (s, e) =>
                    {
                        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
                    };
                    border.MouseRightButtonUp += (s, e) =>
                    {
                        if (MessageBox.Show("Delete " + Path.GetFileName(path) + "?", "Gallery", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        {
                            try { File.Delete(path); Refresh(); } catch { }
                        }
                    };

                    _wrap.Children.Add(border);
                }

                _status.Text = files.Count + " snapshot(s) in " + _folder + "   (click to open, right-click to delete)";
            }
            catch (Exception ex)
            {
                _status.Text = "Refresh failed: " + ex.Message;
            }
        }
    }
}
