using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace OpenTuner.Wpf
{
    /// <summary>
    /// Rolling signal history graph. Plots SNR/MER and margin on a dB axis and BER
    /// on a logarithmic right axis, for one tuner over a selectable time window.
    /// Rendering is done directly with OnRender so it stays dependency-free.
    /// </summary>
    public class SignalGraphControl : FrameworkElement
    {
        private sealed class Sample
        {
            public double T;
            public double Mer;
            public double Margin;
            public double Ber;
            public bool Locked;
        }

        private readonly Dictionary<int, List<Sample>> _data = new Dictionary<int, List<Sample>>();

        private int _tuner;
        private double _windowSeconds = 300;

        // series toggles
        public bool ShowMer { get; set; } = true;
        public bool ShowMargin { get; set; } = true;
        public bool ShowBer { get; set; } = true;

        public int Tuner
        {
            get => _tuner;
            set { _tuner = value; InvalidateVisual(); }
        }

        public double WindowSeconds
        {
            get => _windowSeconds;
            set { _windowSeconds = Math.Max(10, value); Prune(); InvalidateVisual(); }
        }

        public SignalGraphControl()
        {
            SnapsToDevicePixels = true;
        }

        public void AddSample(int tuner, double mer, double margin, double ber, bool locked)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AddSample(tuner, mer, margin, ber, locked)));
                return;
            }

            if (!_data.TryGetValue(tuner, out var list))
            {
                list = new List<Sample>();
                _data[tuner] = list;
            }

            list.Add(new Sample
            {
                T = DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond,
                Mer = mer,
                Margin = margin,
                Ber = ber,
                Locked = locked
            });

            Prune();

            if (tuner == _tuner)
                InvalidateVisual();
        }

        private void Prune()
        {
            double cutoff = Now() - _windowSeconds - 5;
            foreach (var kv in _data)
            {
                var list = kv.Value;
                int drop = 0;
                while (drop < list.Count && list[drop].T < cutoff)
                    drop++;
                if (drop > 0)
                    list.RemoveRange(0, drop);
            }
        }

        private static double Now() => DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;

        public void Clear()
        {
            _data.Clear();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 8 || h < 8)
                return;

            Brush bg = TryFind("SurfaceBackground", Brushes.Black);
            Brush grid = TryFind("Border", Brushes.Gray);
            Brush text = TryFind("TextSecondary", Brushes.Gray);

            dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));

            double left = 48, right = 56, top = 26, bottom = 22;
            double pw = Math.Max(1, w - left - right);
            double ph = Math.Max(1, h - top - bottom);

            var gridPen = new Pen(grid, 1);
            var axisPen = new Pen(grid, 1);

            double merMax = 10;
            List<Sample> list = _data.ContainsKey(_tuner) ? _data[_tuner] : null;
            if (list != null)
            {
                foreach (var s in list)
                {
                    if (s.Locked)
                        merMax = Math.Max(merMax, Math.Max(s.Mer, s.Margin));
                }
            }
            merMax = Math.Ceiling(merMax / 5.0) * 5.0;   // 0..merMax dB
            if (merMax < 20) merMax = 20;

            // dB grid (left axis)
            for (int i = 0; i <= 4; i++)
            {
                double y = top + ph * i / 4.0;
                dc.DrawLine(gridPen, new Point(left, y), new Point(left + pw, y));
                double val = merMax * (4 - i) / 4.0;
                DrawText(dc, val.ToString("0", CultureInfo.InvariantCulture), text, new Point(6, y - 7), 11);
            }

            // BER grid (right, log 1e-1 .. 1e-6)
            for (int i = 0; i <= 5; i++)
            {
                double y = top + ph * i / 5.0;
                DrawText(dc, "1e-" + i, text, new Point(left + pw + 4, y - 7), 10);
            }

            dc.DrawLine(axisPen, new Point(left, top), new Point(left, top + ph));
            dc.DrawLine(axisPen, new Point(left, top + ph), new Point(left + pw, top + ph));
            dc.DrawLine(axisPen, new Point(left + pw, top), new Point(left + pw, top + ph));

            DrawText(dc, "dB", text, new Point(6, 4), 11);
            DrawText(dc, "BER", text, new Point(left + pw + 4, 4), 11);

            // x axis labels (relative seconds)
            for (int i = 0; i <= 4; i++)
            {
                double x = left + pw * i / 4.0;
                double secs = -_windowSeconds * (4 - i) / 4.0;
                DrawText(dc, secs.ToString("0") + "s", text, new Point(x - 12, top + ph + 4), 10);
            }

            if (list == null || list.Count < 2)
            {
                DrawText(dc, "Waiting for signal data...", text, new Point(left + 10, top + ph / 2), 12);
                return;
            }

            double now = Now();
            double start = now - _windowSeconds;

            var merPen = new Pen(new SolidColorBrush(Color.FromRgb(0x3D, 0xB0, 0x6B)), 1.6);
            var marginPen = new Pen(new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9)), 1.4);
            var berPen = new Pen(new SolidColorBrush(Color.FromRgb(0xE0, 0x9A, 0x30)), 1.4);

            // MER + margin share the dB axis
            if (ShowMer)
                DrawSeries(dc, list, s => s.Locked ? s.Mer : 0, merPen, left, top, pw, ph, start, _windowSeconds, v => v / merMax);
            if (ShowMargin)
                DrawSeries(dc, list, s => s.Locked ? s.Margin : 0, marginPen, left, top, pw, ph, start, _windowSeconds, v => v / merMax);

            // BER on the log right axis (1e-6 .. 1e-1)
            if (ShowBer)
            {
                DrawSeries(dc, list, s => s.Ber, berPen, left, top, pw, ph, start, _windowSeconds, BerNorm);
            }

            // legend / current values
            double lastMer = 0, lastMargin = 0, lastBer = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Locked) { lastMer = list[i].Mer; lastMargin = list[i].Margin; lastBer = list[i].Ber; break; }
            }

            double lx = left + 6;
            if (ShowMer) { DrawText(dc, "SNR/MER " + lastMer.ToString("0.0") + " dB", merPen.Brush, new Point(lx, 6), 11); lx += 130; }
            if (ShowMargin) { DrawText(dc, "Margin " + lastMargin.ToString("0.0") + " dB", marginPen.Brush, new Point(lx, 6), 11); lx += 120; }
            if (ShowBer) DrawText(dc, "BER " + (lastBer > 0 ? lastBer.ToString("0.#E+0") : "-"), berPen.Brush, new Point(lx, 6), 11);
        }

        private static double BerNorm(double ber)
        {
            if (ber <= 0)
                return 0;
            // map 1e-1 -> 1.0 (top), 1e-6 -> 0.0 (bottom), clamp
            double log = Math.Log10(ber);           // -1 .. -6
            double n = (log + 6.0) / 5.0;
            return n < 0 ? 0 : (n > 1 ? 1 : n);
        }

        private static void DrawSeries(DrawingContext dc, List<Sample> list, Func<Sample, double> pick, Pen pen,
                                       double left, double top, double pw, double ph, double start, double window,
                                       Func<double, double> norm)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                bool started = false;
                for (int i = 0; i < list.Count; i++)
                {
                    var s = list[i];
                    if (s.T < start)
                        continue;

                    double x = left + pw * (s.T - start) / window;
                    double n = norm(pick(s));
                    if (n < 0) n = 0; else if (n > 1) n = 1;
                    double y = top + ph - n * ph;
                    var p = new Point(x, y);
                    if (!started) { ctx.BeginFigure(p, false, false); started = true; }
                    else ctx.LineTo(p, true, false);
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }

        private static void DrawText(DrawingContext dc, string s, Brush brush, Point p, double size)
        {
            var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), size, brush, 1.0);
            dc.DrawText(ft, p);
        }

        private Brush TryFind(string key, Brush fallback)
        {
            try
            {
                if (Application.Current != null && Application.Current.Resources.Contains(key))
                    return (Brush)Application.Current.Resources[key];
            }
            catch { }
            return fallback;
        }
    }
}
