using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace OpenTuner.Wpf
{
    public enum ToastKind
    {
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// Lightweight in-app toast notifications anchored to the top-right of the
    /// owner window. Thread-safe: call Show() from any thread.
    /// </summary>
    public static class ToastService
    {
        private static Window _owner;
        private static StackPanel _panel;
        private static Popup _popup;

        public static void Attach(Window owner)
        {
            _owner = owner;

            _panel = new StackPanel
            {
                Margin = new Thickness(0, 44, 12, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false
            };

            _popup = new Popup
            {
                AllowsTransparency = true,
                StaysOpen = true,
                IsHitTestVisible = false,
                PlacementTarget = owner,
                Placement = PlacementMode.Relative,
                HorizontalOffset = Math.Max(0, owner.Width - 340),
                VerticalOffset = 0,
                Child = _panel
            };

            owner.LocationChanged += (s, e) => UpdateOffset();
            owner.SizeChanged += (s, e) => UpdateOffset();
            owner.Closed += (s, e) => _popup.IsOpen = false;
            UpdateOffset();
            _popup.IsOpen = true;
        }

        private static void UpdateOffset()
        {
            if (_popup == null || _owner == null)
                return;

            _popup.HorizontalOffset = Math.Max(0, _owner.ActualWidth - 340);
        }

        public static void Show(string message, ToastKind kind = ToastKind.Info, int seconds = 5)
        {
            if (_owner == null)
                return;

            if (!_owner.Dispatcher.CheckAccess())
            {
                _owner.Dispatcher.BeginInvoke(new Action(() => Show(message, kind, seconds)));
                return;
            }

            try
            {
                Color accent;
                switch (kind)
                {
                    case ToastKind.Success: accent = Color.FromRgb(0x3D, 0xB0, 0x6B); break;
                    case ToastKind.Warning: accent = Color.FromRgb(0xE0, 0xA0, 0x30); break;
                    case ToastKind.Error: accent = Color.FromRgb(0xD9, 0x53, 0x4F); break;
                    default: accent = Color.FromRgb(0x4A, 0x90, 0xD9); break;
                }

                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x2B, 0x2F, 0x34)),
                    BorderBrush = new SolidColorBrush(accent),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 6, 0, 0),
                    MaxWidth = 320,
                    Opacity = 0,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 8,
                        ShadowDepth = 1,
                        Opacity = 0.4
                    }
                };

                border.Child = new TextBlock
                {
                    Text = message,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12
                };

                _panel.Children.Add(border);

                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
                border.BeginAnimation(UIElement.OpacityProperty, fadeIn);

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260));
                    fadeOut.Completed += (s2, e2) => { try { _panel.Children.Remove(border); } catch { } };
                    border.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                };
                timer.Start();
            }
            catch { }
        }
    }
}
