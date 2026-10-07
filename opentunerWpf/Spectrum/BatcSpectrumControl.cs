using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using opentuner.ExtraFeatures.BATCSpectrum;

namespace OpenTuner.Wpf.Spectrum
{
    /// <summary>
    /// Native WPF BATC web-spectrum view. Reuses the existing sockets/signal
    /// detection and renders the FFT + detected signals with WPF.
    /// </summary>
    public class BatcSpectrumControl : FrameworkElement
    {
        public delegate void SignalSelected(int receiver, uint freqKHz, uint symbolRate);
        public event SignalSelected OnSignalSelected;

        private readonly opentuner.socket _socket;
        private readonly opentuner.signal _sigs = new opentuner.signal(new object());

        private ushort[] _fft;
        private readonly object _lock = new object();

        private readonly DispatcherTimer _timer;
        private Point _mouse;
        private bool _hasMouse;

        private readonly Typeface _typeface = new Typeface("Segoe UI");

        private readonly int[] _rxCentre = new int[4];
        private readonly int[] _rxWidth = new int[4];
        private int _selectedReceiver;

        private sealed class BpChannel
        {
            public string Name;
            public string TxFreq;
            public double XFreq;
            public uint Sr;
            public int Block;
        }

        private readonly List<BpChannel> _bandplan = new List<BpChannel>();
        private int _bandplanBlocks = 1;
        private const int BandplanH = 30;

        public void SetSelectedReceiver(int rx) => _selectedReceiver = rx;

        // BATC web spectrum covers 10490.5 MHz +/- 4.5 (9 MHz wide)
        private const double StartMHz = 10490.5;
        private const double SpanMHz = 9.0;

        public BatcSpectrumControl()
        {
            _socket = new opentuner.socket();
            _socket.callback += OnFft;
            _socket.start();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _timer.Tick += (s, e) => { if (_fft != null) InvalidateVisual(); };
            _timer.Start();

            MouseMove += (s, e) => { _mouse = e.GetPosition(this); _hasMouse = true; InvalidateVisual(); };
            MouseLeave += (s, e) => { _hasMouse = false; InvalidateVisual(); };
            MouseLeftButtonDown += (s, e) => Select(e.GetPosition(this));
            MouseRightButtonDown += (s, e) => CopyTxFreq(e.GetPosition(this));

            LoadBandplan();

            Unloaded += (s, e) => { _timer.Stop(); try { _socket.stop(); } catch { } };
        }

        public void updateSignalCallsign(string callsign, double freq, float sr)
        {
            try { _sigs.updateCurrentSignal(callsign, freq, sr); } catch { }
        }

        private string _txText = "";

        private void LoadBandplan()
        {
            try
            {
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extra", "bandplan.xml");
                if (!System.IO.File.Exists(path))
                    return;

                var doc = System.Xml.Linq.XDocument.Load(path);
                var blocks = new List<string>();

                foreach (var ch in doc.Root.Elements("channel"))
                {
                    string b = ch.Element("block")?.Value ?? "";
                    if (!blocks.Contains(b)) blocks.Add(b);
                }

                _bandplanBlocks = Math.Max(1, blocks.Count);

                foreach (var ch in doc.Root.Elements("channel"))
                {
                    double.TryParse(ch.Element("x-freq")?.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double xf);
                    uint.TryParse(ch.Element("sr")?.Value, out uint sr);

                    _bandplan.Add(new BpChannel
                    {
                        Name = ch.Element("name")?.Value ?? "",
                        TxFreq = ch.Element("s-freq")?.Value ?? "",
                        XFreq = xf,
                        Sr = sr,
                        Block = blocks.IndexOf(ch.Element("block")?.Value ?? "")
                    });
                }
            }
            catch { }
        }

        private void CopyTxFreq(Point p)
        {
            double w = ActualWidth, h = ActualHeight;
            if (_bandplan.Count == 0 || w < 1)
                return;

            int split = (int)(BandplanH / (double)_bandplanBlocks);

            foreach (BpChannel c in _bandplan)
            {
                double x = (c.XFreq - StartMHz) / SpanMHz * w;
                double width = Math.Max(4, c.Sr / 9000.0 * w * 1.35);
                int yoff = c.Block * split;
                var rect = new Rect(x - width / 2, h - BandplanH + yoff, width, split - 2);

                if (rect.Contains(p) && !string.IsNullOrEmpty(c.TxFreq))
                {
                    try { Clipboard.SetText(c.TxFreq); } catch { }
                    _txText = "TX " + c.TxFreq + " MHz  (" + c.Name + ")";
                    InvalidateVisual();
                    return;
                }
            }
        }

        private void OnFft(ushort[] data)
        {
            lock (_lock)
            {
                _fft = data;
                try { _sigs.detect_signals(data); } catch { }
            }
        }

        private double XToMHz(double x, double w) => StartMHz + (x / w - 0.5) * SpanMHz + SpanMHz / 2;
        private double MHzToX(double mhz, double w) => (mhz - StartMHz) / SpanMHz * w;

        private void Select(Point p)
        {
            lock (_lock)
            {
                if (_sigs.signals == null) return;

                double xScale = ActualWidth / 922.0;

                foreach (opentuner.signal.Sig s in _sigs.signals)
                {
                    double sx = s.fft_centre * xScale;
                    if (Math.Abs(sx - p.X) < Math.Max(20, (s.fft_stop - s.fft_start) * xScale / 2))
                    {
                        _sigs.set_tuned(s, _selectedReceiver);

                        if (_selectedReceiver >= 0 && _selectedReceiver < 4)
                        {
                            _rxCentre[_selectedReceiver] = s.fft_centre;
                            _rxWidth[_selectedReceiver] = s.fft_stop - s.fft_start;
                        }

                        OnSignalSelected?.Invoke(_selectedReceiver, (uint)(s.frequency * 1000), (uint)(s.sr * 1000));
                        return;
                    }
                }
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 1 || h <= 1) return;

            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));

            ushort[] fft;
            List<opentuner.signal.Sig> signals;
            lock (_lock)
            {
                fft = _fft;
                signals = _sigs.signals != null ? new List<opentuner.signal.Sig>(_sigs.signals) : new List<opentuner.signal.Sig>();
            }

            if (fft == null)
            {
                DrawText(dc, _socket.connected ? "Waiting for FFT..." : "FFT Service Disconnected",
                         new Point(12, 12), Brushes.Orange, 12);
                return;
            }

            // spectrum fill
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                var pts = new List<Point>(fft.Length);
                for (int i = 1; i < fft.Length - 2; i++)
                {
                    double x = i / (double)fft.Length * w;
                    double y = 255 - fft[i] / 255.0;
                    pts.Add(new Point(x, y / 255.0 * h));
                }
                ctx.BeginFigure(new Point(0, h), true, true);
                ctx.PolyLineTo(pts, true, false);
                ctx.LineTo(new Point(w, h), true, false);
            }
            geo.Freeze();

            var fill = new LinearGradientBrush(
                Color.FromArgb(255, 255, 99, 132), Color.FromArgb(255, 54, 162, 235), 90);
            fill.Freeze();
            dc.DrawGeometry(fill, null, geo);

            // detected signals - box + details (callsign / frequency / symbol rate)
            double xScale = w / 922.0;
            foreach (opentuner.signal.Sig s in signals)
            {
                double x1 = s.fft_start * xScale;
                double x2 = s.fft_stop * xScale;
                double bw = Math.Max(8, x2 - x1);
                double cx = s.fft_centre * xScale;
                double top = Math.Max(0, h - (s.fft_strength + 50));

                Brush box = s.overpower
                    ? new SolidColorBrush(Color.FromArgb(70, 255, 40, 40))
                    : new SolidColorBrush(Color.FromArgb(45, 255, 255, 255));

                dc.DrawRoundedRectangle(box, null, new Rect(cx - bw / 2, top, bw, Math.Max(4, h - top - 30)), 3, 3);

                string txt = (string.IsNullOrEmpty(s.callsign) ? "" : s.callsign + "\n")
                             + s.frequency.ToString("0.000") + "\n"
                             + (s.sr * 1000).ToString("0") + " Ks";
                DrawText(dc, txt, new Point(cx - bw / 2 + 2, top + 2), Brushes.White, 10);
            }

            // tuned receiver markers
            for (int r = 0; r < _rxCentre.Length; r++)
            {
                if (_rxWidth[r] <= 0) continue;
                double bx = _rxCentre[r] * xScale;
                double bwid = _rxWidth[r] * xScale;
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(50, Colors.Blue.R, Colors.Blue.G, Colors.Blue.B)), new Pen(Brushes.Cyan, 1),
                                 new Rect(bx - bwid / 2, 0, bwid, h));
                DrawText(dc, "RX" + (r + 1), new Point(bx - bwid / 2 + 2, 3), Brushes.Cyan, 11);
            }

            // bandplan strip at the bottom
            if (_bandplan.Count > 0)
            {
                int split = (int)(BandplanH / (double)_bandplanBlocks);
                foreach (BpChannel c in _bandplan)
                {
                    double x = (c.XFreq - StartMHz) / SpanMHz * w;
                    double width = Math.Max(4, c.Sr / 9000.0 * w * 1.35);
                    int yoff = c.Block * split;
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(200, 250, 250, 255)), null,
                                     new Rect(x - width / 2, h - BandplanH + yoff, width, split - 2));
                }
            }

            if (!string.IsNullOrEmpty(_txText))
                DrawText(dc, _txText, new Point(6, h - 46), Brushes.Yellow, 11);

            if (_hasMouse)
            {
                double mhz = XToMHz(_mouse.X, w);
                dc.DrawLine(new Pen(Brushes.DeepSkyBlue, 1), new Point(_mouse.X, 0), new Point(_mouse.X, h));
                DrawText(dc, mhz.ToString("0.0000") + " MHz", new Point(6, h - 18), Brushes.DeepSkyBlue, 11);
            }
        }

        private void DrawText(DrawingContext dc, string text, Point at, Brush brush, double size)
        {
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                       _typeface, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, at);
        }
    }
}



