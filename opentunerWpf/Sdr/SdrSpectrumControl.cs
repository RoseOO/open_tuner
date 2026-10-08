using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using NAudio.Dsp;
using opentuner.ExtraFeatures.SdrSpectrum;
using opentuner.Utilities;

namespace OpenTuner.Wpf.Sdr
{
    /// <summary>
    /// Native WPF SDR spectrum + waterfall. Reuses the SdrDevices/SdrSettings
    /// logic from the core assembly and renders with WPF (DrawingContext +
    /// WriteableBitmap for the waterfall).
    /// </summary>
    public class SdrSpectrumControl : FrameworkElement
    {
        public delegate void SignalSelected(int receiver, uint freqKHz, uint symbolRate);
        public event SignalSelected OnSignalSelected;

        public delegate void StatusChanged(string status);
        public event StatusChanged OnStatus;

        private readonly SdrSettings _settings;
        private ISdrDevice _device;

        // FFT
        private Complex[] _fftBuffer;
        private int _fftSize = 2048;
        private int _fftM;
        private int _fftIndex;
        private float[] _window;
        private float[] _spectrumDb;
        private float[] _displayDb;
        private readonly object _lock = new object();
        private volatile bool _haveSpectrum;

        // sweep
        private bool _sweepActive;
        private float[] _sweepDb;
        private int _sweepBins;
        private double _sweepResHz, _sweepStartHz, _sweepStepHz, _sweepStepCenterHz;
        private int _sweepSteps, _sweepStep, _sweepCycle;
        private DateTime _sweepLastUtc = DateTime.MinValue;

        // render
        private readonly DispatcherTimer _timer;
        private WriteableBitmap _waterfall;
        private int _wfW, _wfH;
        private double _viewCenterHz, _viewSpanHz;
        private int _viewBins;
        private float[] _viewDb;

        // marker / tuning
        private Point _mouse;
        private bool _hasMouse;
        private double _markerHz;
        private float _markerDb;
        private int _selectedReceiver;
        private readonly uint[] _tunedKHz = new uint[8];
        private bool _broadband = true;

        // autotune
        private int _autoTuneMode;
        private int _autoTimedIndex;
        private readonly DispatcherTimer _autoTuneTimer;
        private bool _snapToPeak = true;
        private double _peakSrKsym;

        private readonly Typeface _typeface = new Typeface("Segoe UI");
        private string _status = "Disconnected";
        private bool _connected;

        // narrowband audio demod
        private SdrAudioOutput _audio;
        private double _ncoPhase;
        private double _ncoInc;
        private float _lpI, _lpQ;
        private float _audioLp;
        private float _deemphState;
        private float _dcBlockState;
        private int _decCount;
        private float _decAccum;
        private int _decimation = 50;
        private float _demodLpAlpha;
        private float _audioLpAlpha;
        private float _deemphAlpha;
        private float _dcAlpha;

        public SdrSettings Settings => _settings;

        public SdrSpectrumControl()
        {
            Focusable = true;

            _settings = new SettingsManager<SdrSettings>("sdr_settings").LoadSettings(new SdrSettings());
            _snapToPeak = _settings.SnapToPeak;

            _device = CreateDevice(_settings);

            ConfigureFft(_settings.FftSize);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _timer.Tick += (s, e) => RenderTick();
            _timer.Start();

            _autoTuneTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
            _autoTuneTimer.Tick += (s, e) => AutoTuneTick();

            MouseMove += (s, e) => { _mouse = e.GetPosition(this); _hasMouse = true; UpdateMarker(); InvalidateVisual(); };
            MouseLeave += (s, e) => { _hasMouse = false; InvalidateVisual(); };
            MouseLeftButtonDown += (s, e) => { TuneAt(e.GetPosition(this)); InvalidateVisual(); };
            MouseRightButtonDown += (s, e) => ShowMenu(e.GetPosition(this));

            SizeChanged += (s, e) => { _woodfallReset = true; };

            Loaded += (s, e) => Connect();
            Unloaded += (s, e) => { _timer.Stop(); _autoTuneTimer.Stop(); Disconnect(); };
        }

        private bool _woodfallReset;

        private static ISdrDevice CreateDevice(SdrSettings s)
        {
            switch (s.SourceType)
            {
                case SdrSourceType.RtlSdr: return new RtlSdrDevice(s.DeviceIndex);
                case SdrSourceType.HackRf: return new HackRfDevice();
                case SdrSourceType.Pluto: return new PlutoSdrDevice(s.PlutoUri);
                default: return new RtlTcpDevice(s.RtlTcpHost, s.RtlTcpPort);
            }
        }

        // ------------------------------------------------------------------
        public void Connect()
        {
            Disconnect();

            if (!_device.Open())
            {
                _connected = false;
                _status = _device.Name + " unavailable: " + _device.LastError;
                OnStatus?.Invoke(_status);
                InvalidateVisual();
                return;
            }

            _device.SetSampleRate(_settings.SampleRateHz);
            _device.SetGain(_settings.GainDb, _settings.Agc);
            _device.SetPpm(_settings.PpmCorrection);
            _device.SetCenterFrequency(_settings.CenterFrequencyHz);

            ConfigureSweep();

            _device.Start(OnSamples);

            _connected = true;
            _status = _device.Name + " @ " + (_settings.CenterFrequencyHz / 1e6).ToString("0.000") + " MHz, " +
                      (_settings.SampleRateHz / 1e6).ToString("0.00") + " Msps";
            OnStatus?.Invoke(_status);
            InvalidateVisual();

            SetupAudio();
        }

        private void SetupAudio()
        {
            try
            {
                if (_settings.AudioEnabled && _connected)
                {
                    if (_audio == null)
                        _audio = new SdrAudioOutput();

                    _audio.SetVolume(_settings.AudioVolume);
                    _audio.Open();
                }
                else
                {
                    _audio?.Close();
                }
            }
            catch { }

            ConfigureDemod();
        }

        private void ConfigureDemod()
        {
            double sr = Math.Max(1, _settings.SampleRateHz);
            _decimation = Math.Max(1, (int)Math.Round(sr / SdrAudioOutput.AudioRateHz));
            _ncoInc = 2.0 * Math.PI * _settings.AudioOffsetHz / sr;

            double passCut = Math.Max(1000, _settings.AudioFilterHz * 1.5);
            _demodLpAlpha = (float)(1.0 - Math.Exp(-2.0 * Math.PI * passCut / sr));

            double audioCut = Math.Max(300, _settings.AudioFilterEnabled ? _settings.AudioFilterHz : 8000);
            _audioLpAlpha = (float)(1.0 - Math.Exp(-2.0 * Math.PI * audioCut / SdrAudioOutput.AudioRateHz));

            if (_settings.DeemphasisUs > 0)
            {
                double tau = _settings.DeemphasisUs * 1e-6;
                _deemphAlpha = (float)(1.0 - Math.Exp(-1.0 / (tau * SdrAudioOutput.AudioRateHz)));
            }
            else
            {
                _deemphAlpha = 0f;
            }

            _dcAlpha = (float)(1.0 - Math.Exp(-2.0 * Math.PI * 40.0 / SdrAudioOutput.AudioRateHz));
        }

        public void Disconnect()
        {
            try { _device?.Stop(); } catch { }
            try { _device?.Close(); } catch { }
            try { _audio?.Close(); } catch { }
            _connected = false;
        }

        public void SetSelectedReceiver(int rx) => _selectedReceiver = rx;

        public void ApplyPolarization(int supply)
        {
            if (!_settings.FollowLnbPolarization || supply == 0)
                return;

            _broadband = supply == 2;
            SetCenterFrequency(_broadband ? _settings.BroadbandCenterHz : _settings.NarrowbandCenterHz);
        }

        public void SetCenterFrequency(uint hz)
        {
            _settings.CenterFrequencyHz = hz;
            _device?.SetCenterFrequency(hz);

            if (_connected)
            {
                _status = _device.Name + " @ " + (hz / 1e6).ToString("0.000") + " MHz";
                OnStatus?.Invoke(_status);
            }
        }

        public void ApplySettings()
        {
            ConfigureFft(_settings.FftSize);

            // the device type / connection may have changed - rebuild it
            Disconnect();
            try { _device?.Dispose(); } catch { }
            _device = CreateDevice(_settings);

            Connect();

            new SettingsManager<SdrSettings>("sdr_settings").SaveSettings(_settings);
        }

        // ------------------------------------------------------------------
        private void ConfigureFft(int size)
        {
            int s = 256;
            while (s < size && s < 32768) s <<= 1;

            _fftSize = s;
            _fftM = (int)Math.Round(Math.Log(_fftSize, 2));
            _fftBuffer = new Complex[_fftSize];

            lock (_lock)
            {
                _spectrumDb = new float[_fftSize];
                _displayDb = new float[_fftSize];
            }

            _window = new float[_fftSize];
            for (int i = 0; i < _fftSize; i++)
                _window[i] = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * i / (_fftSize - 1))));

            _fftIndex = 0;
        }

        private void ConfigureSweep()
        {
            _sweepActive = _settings.SweepEnabled;

            if (!_sweepActive)
            {
                _sweepDb = null;
                _sweepBins = 0;
                return;
            }

            _sweepResHz = (double)_settings.SampleRateHz / _fftSize;
            _sweepBins = Math.Max(64, (int)Math.Ceiling(_settings.SweepSpanHz / _sweepResHz));
            lock (_lock) { _sweepDb = new float[_sweepBins]; }
            _sweepStartHz = (double)_settings.SweepCenterHz - _settings.SweepSpanHz / 2.0;
            _sweepStepHz = _settings.SampleRateHz * 0.85;
            _sweepSteps = Math.Max(1, (int)Math.Ceiling(_settings.SweepSpanHz / _sweepStepHz));
            _sweepStep = 0;
            _sweepCycle = 0;
            _sweepLastUtc = DateTime.MinValue;
            ApplySweepStep();
        }

        private void ApplySweepStep()
        {
            _sweepStepCenterHz = _sweepStartHz + _settings.SampleRateHz / 2.0 + _sweepStep * _sweepStepHz;
            _device?.SetCenterFrequency((uint)Math.Round(_sweepStepCenterHz));
            _fftIndex = 0;
        }

        private void PumpSweep()
        {
            if (!_sweepActive || _sweepDb == null) return;
            if ((DateTime.UtcNow - _sweepLastUtc).TotalMilliseconds < _settings.SweepDwellMs) return;

            _sweepLastUtc = DateTime.UtcNow;
            _sweepStep++;
            if (_sweepStep >= _sweepSteps) { _sweepStep = 0; _sweepCycle++; }
            ApplySweepStep();
        }

        private void OnSamples(byte[] data, int count)
        {
            try
            {
                SdrSampleFormat fmt = _device.SampleFormat;

                if (fmt == SdrSampleFormat.Signed16)
                {
                    for (int i = 0; i + 3 < count; i += 4)
                        ProcessSample(BitConverter.ToInt16(data, i) / 32768f,
                                      BitConverter.ToInt16(data, i + 2) / 32768f);
                }
                else
                {
                    bool s = fmt == SdrSampleFormat.Signed8;
                    for (int i = 0; i + 1 < count; i += 2)
                        ProcessSample(Conv(data[i], s), Conv(data[i + 1], s));
                }
            }
            catch { }
        }

        private static float Conv(byte b, bool signed) => signed ? (sbyte)b / 128f : (b - 127.5f) / 127.5f;

        private void ProcessSample(float ii, float qq)
        {
            if (_audio != null && _audio.IsOpen)
                Demodulate(ii, qq);

            _fftBuffer[_fftIndex].X = ii * _window[_fftIndex];
            _fftBuffer[_fftIndex].Y = qq * _window[_fftIndex];
            _fftIndex++;

            if (_fftIndex >= _fftSize)
            {
                _fftIndex = 0;
                ComputeFft();
            }
        }

        private void Demodulate(float ii, float qq)
        {
            // down-convert by the BFO offset so the wanted signal sits at DC
            double c = Math.Cos(_ncoPhase);
            double s = Math.Sin(_ncoPhase);
            _ncoPhase += _ncoInc;
            if (_ncoPhase > 2.0 * Math.PI) _ncoPhase -= 2.0 * Math.PI;

            float mi = (float)(ii * c + qq * s);
            float mq = (float)(-ii * s + qq * c);

            // channel low-pass (one-pole)
            _lpI += _demodLpAlpha * (mi - _lpI);
            _lpQ += _demodLpAlpha * (mq - _lpQ);

            float demod;
            switch (_settings.DemodMode)
            {
                case SdrDemodMode.AM:
                    demod = (float)Math.Sqrt(_lpI * _lpI + _lpQ * _lpQ);
                    break;
                case SdrDemodMode.LSB:
                    demod = (_lpI + _lpQ) * 0.5f;
                    break;
                default: // USB
                    demod = (_lpI - _lpQ) * 0.5f;
                    break;
            }

            // decimate to the audio rate
            _decAccum += demod;
            _decCount++;
            if (_decCount < _decimation)
                return;

            float audio = _decAccum / _decCount;
            _decAccum = 0f;
            _decCount = 0;

            // de-emphasis (optional)
            if (_deemphAlpha > 0f)
            {
                _deemphState += _deemphAlpha * (audio - _deemphState);
                audio = _deemphState;
            }

            // audio low-pass
            _audioLp += _audioLpAlpha * (audio - _audioLp);
            audio = _audioLp;

            // DC block (high-pass)
            _dcBlockState += _dcAlpha * (audio - _dcBlockState);
            audio -= _dcBlockState;

            // compensate AM offset and scale
            audio *= _settings.DemodMode == SdrDemodMode.AM ? 4f : 2f;

            _audio.Write(audio);
        }

        private void ComputeFft()
        {
            FastFourierTransform.FFT(true, _fftM, _fftBuffer);

            lock (_lock)
            {
                if (_spectrumDb == null) return;

                for (int k = 0; k < _fftSize; k++)
                {
                    int idx = (k + _fftSize / 2) % _fftSize;
                    Complex c = _fftBuffer[idx];
                    float mag = (float)Math.Sqrt(c.X * c.X + c.Y * c.Y) / _fftSize;
                    float db = 20f * (float)Math.Log10(mag + 1e-9f);
                    _spectrumDb[k] = (_spectrumDb[k] == 0f) ? db : _spectrumDb[k] * 0.75f + db * 0.25f;
                }

                Array.Copy(_spectrumDb, _displayDb, _fftSize);

                if (_sweepActive && _sweepDb != null)
                {
                    double binHz = (double)_settings.SampleRateHz / _fftSize;
                    for (int k = 0; k < _fftSize; k++)
                    {
                        double freq = _sweepStepCenterHz + (k - _fftSize / 2.0) * binHz;
                        int idx = (int)Math.Round((freq - _sweepStartHz) / _sweepResHz);
                        if (idx >= 0 && idx < _sweepBins)
                            _sweepDb[idx] = _displayDb[k];
                    }
                }

                _haveSpectrum = true;
            }
        }

        // ------------------------------------------------------------------
        // view mapping
        // ------------------------------------------------------------------
        private bool SweepDisplay => _sweepActive && _sweepDb != null;
        private int ViewBins => SweepDisplay ? _sweepBins : _fftSize;
        private double ViewCenterHz => SweepDisplay ? _settings.SweepCenterHz : _settings.CenterFrequencyHz;
        private double ViewSpanHz => SweepDisplay ? _settings.SweepSpanHz : _settings.SampleRateHz;

        private float[] Snapshot()
        {
            lock (_lock)
            {
                if (SweepDisplay) return (float[])_sweepDb.Clone();
                return _displayDb != null ? (float[])_displayDb.Clone() : null;
            }
        }

        private double HzToX(double hz, double w) => ((hz - ViewCenterHz) / ViewSpanHz + 0.5) * w;
        private double XToHz(double x, double w) => ViewCenterHz + (x / w - 0.5) * ViewSpanHz;
        private double ViewBinHz_(double bin) => ViewCenterHz + (bin / ViewBins - 0.5) * ViewSpanHz;

        private float Level(float db)
        {
            float v = (db - _settings.MinDb) / (float)(_settings.MaxDb - _settings.MinDb);
            return v < 0 ? 0 : (v > 1 ? 1 : v);
        }

        // ------------------------------------------------------------------
        // rendering
        // ------------------------------------------------------------------
        private void RenderTick()
        {
            if (_sweepActive) PumpSweep();
            if (_haveSpectrum) InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 1 || h <= 1) return;

            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));

            if (!_haveSpectrum)
            {
                DrawText(dc, "SDR: " + _status, new Point(12, 12), Brushes.Orange, 12);
                return;
            }

            float[] db = Snapshot();
            if (db == null) return;

            int bins = ViewBins;
            double specH = h * 0.62;

            // ---- waterfall ----
            EnsureWaterfall((int)w, (int)h);
            UpdateWaterfall(db, bins);
            dc.DrawImage(_waterfall, new Rect(0, 0, w, h));

            // ---- spectrum ----
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                var pts = new List<Point>(bins / 2 + 2);
                for (int x = 0; x <= (int)w; x++)
                {
                    int bin = (int)((double)x / w * (bins - 1));
                    double y = specH - Level(db[bin]) * (specH - 2);
                    pts.Add(new Point(x, y));
                }
                ctx.BeginFigure(new Point(0, specH), true, true);
                ctx.PolyLineTo(pts, true, false);
                ctx.LineTo(new Point(w, specH), true, false);
            }
            geo.Freeze();

            var fill = new LinearGradientBrush(Color.FromArgb(220, 255, 190, 60), Color.FromArgb(220, 40, 90, 200), 90);
            fill.Freeze();
            dc.DrawGeometry(fill, null, geo);

            // ---- grid + labels ----
            var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 1);
            for (int i = 0; i <= 8; i++)
            {
                double x = i * w / 8;
                dc.DrawLine(gridPen, new Point(x, 0), new Point(x, specH));
                double f = XToHz(x, w);
                DrawText(dc, (f / 1e6).ToString("0.000") + " MHz", new Point(x + 3, 2), Brushes.LightGray, 10);
            }

            // ---- peak ----
            int pb = 0; float pv = db[0];
            for (int i = 1; i < bins; i++) if (db[i] > pv) { pv = db[i]; pb = i; }

            double dispHz = ViewBinHz_(pb);
            float dispDb = pv;
            string sr = "";
            _peakSrKsym = 0;
            if (_settings.EstimateSymbolRate && _broadband &&
                TryEstimateSignal(db, bins, ViewCenterHz, ViewSpanHz, pb, out double ehz, out double esr, out float edb))
            {
                _peakSrKsym = esr; dispHz = ehz; dispDb = edb; sr = "  SR ~" + esr.ToString("0") + " ksym";
            }

            double px = HzToX(dispHz, w);
            dc.DrawLine(new Pen(Brushes.IndianRed, 1), new Point(px, 0), new Point(px, specH));
            DrawText(dc, "Peak " + (dispHz / 1e6).ToString("0.0000") + " MHz  " + dispDb.ToString("0.0") + " dB" + sr,
                     new Point(6, specH - 18), Brushes.Salmon, 11);

            // ---- marker ----
            if (_hasMouse)
            {
                double mx = _mouse.X;
                dc.DrawLine(new Pen(Brushes.DeepSkyBlue, 1), new Point(mx, 0), new Point(mx, specH));
                DrawText(dc, "Marker " + (_markerHz / 1e6).ToString("0.0000") + " MHz  " + _markerDb.ToString("0.0") + " dB",
                         new Point(6, specH - 34), Brushes.DeepSkyBlue, 11);
            }

            // ---- receiver markers ----
            for (int r = 0; r < _tunedKHz.Length; r++)
            {
                if (_tunedKHz[r] == 0) continue;
                double ifHz = ((double)_tunedKHz[r] - _settings.LnbLoKHz) * 1000.0;
                double rx = HzToX(ifHz, w);
                if (rx < 0 || rx > w) continue;
                bool sel = r == _selectedReceiver;
                dc.DrawLine(new Pen(sel ? Brushes.Cyan : Brushes.LightGreen, 1), new Point(rx, 0), new Point(rx, specH));
                DrawText(dc, "RX" + (r + 1), new Point(rx + 2, 3), sel ? Brushes.Cyan : Brushes.LightGreen, 11);
            }

            if (SweepDisplay)
                DrawText(dc, "SWEEP " + (ViewSpanHz / 1e6).ToString("0.0") + " MHz  step " + (_sweepStep + 1) + "/" + _sweepSteps,
                         new Point(w - 220, 3), Brushes.Gold, 11);

            DrawText(dc, _status, new Point(6, h - 16), Brushes.Gainsboro, 11);
        }

        private void DrawText(DrawingContext dc, string text, Point at, Brush brush, double size)
        {
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                       _typeface, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, at);
        }

        private void EnsureWaterfall(int w, int h)
        {
            if (_waterfall == null || _wfW != w || _wfH != h || _woodfallReset)
            {
                _wfW = w; _wfH = h;
                _waterfall = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
                ClearWaterfall();
                _woodfallReset = false;
            }
        }

        private void ClearWaterfall()
        {
            int[] row = new int[_wfW];
            for (int y = 0; y < _wfH; y++)
                _waterfall.WritePixels(new Int32Rect(0, y, _wfW, 1), row, _wfW * 4, 0);
        }

        private int _lastWfCycle = -1;

        private void UpdateWaterfall(float[] db, int bins)
        {
            bool scroll = !SweepDisplay || _sweepCycle != _lastWfCycle;
            if (!scroll) return;
            if (SweepDisplay) _lastWfCycle = _sweepCycle;

            // shift up by one row
            var pixels = new int[_wfW * _wfH];
            _waterfall.CopyPixels(pixels, _wfW * 4, 0);
            Array.Copy(pixels, _wfW, pixels, 0, _wfW * (_wfH - 1));

            int y0 = _wfH - 1;
            for (int x = 0; x < _wfW; x++)
            {
                int bin = (int)((double)x / _wfW * (bins - 1));
                pixels[y0 * _wfW + x] = WaterfallColor(Level(db[bin]));
            }

            _waterfall.WritePixels(new Int32Rect(0, 0, _wfW, _wfH), pixels, _wfW * 4, 0);
        }

        private static int WaterfallColor(float v)
        {
            int i = (int)(v * 255);
            if (i < 0) i = 0; if (i > 255) i = 255;
            float t = i / 255f;
            Color c;
            if (t < 0.33f) c = Lerp(Colors.Black, Color.FromRgb(0, 0, 180), t / 0.33f);
            else if (t < 0.66f) c = Lerp(Color.FromRgb(0, 0, 180), Colors.Cyan, (t - 0.33f) / 0.33f);
            else c = Lerp(Colors.Cyan, Colors.Red, (t - 0.66f) / 0.34f);
            return (c.R << 16) | (c.G << 8) | c.B;
        }

        private static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        // ------------------------------------------------------------------
        // tuning / peaks / autotune
        // ------------------------------------------------------------------
        private void UpdateMarker()
        {
            _markerHz = XToHz(_mouse.X, ActualWidth);

            float[] db = Snapshot();
            if (db != null)
            {
                int bins = ViewBins;
                int bin = (int)(_mouse.X / Math.Max(1, ActualWidth) * (bins - 1));
                if (bin >= 0 && bin < db.Length) _markerDb = db[bin];
            }
        }

        private void TuneAt(Point p)
        {
            double ifHz = XToHz(p.X, ActualWidth);
            uint sr = _broadband ? _settings.BroadbandSymbolRate : _settings.NarrowbandSymbolRate;

            if (_broadband && (_snapToPeak || _settings.EstimateSymbolRate))
            {
                List<SdrPeak> peaks = DetectPeaks();
                SdrPeak nearest = null; double nd = double.MaxValue;
                foreach (var pk in peaks) { double d = Math.Abs(pk.CenterHz - ifHz); if (d < nd) { nd = d; nearest = pk; } }

                double window = Math.Max(25000, ViewSpanHz * 0.03);

                if (_snapToPeak && nearest != null && nd <= window)
                {
                    ifHz = nearest.CenterHz;
                    sr = (uint)Math.Max(1, Math.Round(nearest.SrKsym));
                }
                else if (_settings.EstimateSymbolRate)
                {
                    int bins = ViewBins;
                    int bin = (int)(p.X / Math.Max(1, ActualWidth) * (bins - 1));
                    if (TryEstimateSignal(Snapshot(), bins, ViewCenterHz, ViewSpanHz, bin, out double ehz, out double esr, out _))
                    {
                        ifHz = ehz; sr = (uint)Math.Max(1, Math.Round(esr));
                    }
                }
            }

            uint satKHz = (uint)Math.Round(ifHz / 1000.0) + _settings.LnbLoKHz;
            if (_selectedReceiver >= 0 && _selectedReceiver < _tunedKHz.Length)
                _tunedKHz[_selectedReceiver] = satKHz;

            OnSignalSelected?.Invoke(_selectedReceiver, satKHz, sr);
        }

        public class SdrPeak
        {
            public double CenterHz;
            public float PowerDb;
            public double BwHz;
            public double SrKsym;
        }

        public List<SdrPeak> DetectPeaks()
        {
            float[] db = Snapshot();
            var peaks = new List<SdrPeak>();
            if (db == null) return peaks;

            int bins = ViewBins;
            if (bins < 32) return peaks;

            double center = ViewCenterHz, span = ViewSpanHz, binHz = span / bins;

            float[] sorted = (float[])db.Clone();
            Array.Sort(sorted);
            float floor = sorted[(int)(bins * 0.25)];
            float thr = floor + 6f;

            for (int i = 2; i < bins - 2; i++)
            {
                if (db[i] < thr) continue;
                if (db[i] < db[i - 1] || db[i] < db[i + 1] || db[i] <= db[i - 2] || db[i] <= db[i + 2]) continue;

                float pk = db[i];
                float t = Math.Max(floor + 3f, pk - 3f);
                int l = i; while (l - 1 >= 0 && db[l - 1] > t) l--;
                int r = i; while (r + 1 < bins && db[r + 1] > t) r++;
                double bw = (r - l) * binHz;
                double sr = bw / (1.0 + _settings.Rolloff) / 1000.0;
                if (sr < 1 || sr > 100000) continue;

                peaks.Add(new SdrPeak { CenterHz = center + (((l + r) / 2.0) / bins - 0.5) * span, PowerDb = pk, BwHz = bw, SrKsym = sr });
            }

            peaks.Sort((a, b) => b.PowerDb.CompareTo(a.PowerDb));
            var kept = new List<SdrPeak>();
            foreach (var p in peaks)
            {
                bool over = false;
                foreach (var k in kept) if (Math.Abs(k.CenterHz - p.CenterHz) < Math.Max(30000, (k.BwHz + p.BwHz) / 2)) { over = true; break; }
                if (!over) kept.Add(p);
                if (kept.Count >= 32) break;
            }
            return kept;
        }

        private bool TryEstimateSignal(float[] db, int bins, double center, double span, int seed, out double centerHz, out double srKsym, out float peakDb)
        {
            centerHz = 0; srKsym = 0; peakDb = -999;
            if (db == null || bins < 32) return false;

            if (seed < 0) seed = 0; if (seed >= bins) seed = bins - 1;
            double binHz = span / bins;

            float[] sorted = (float[])db.Clone();
            Array.Sort(sorted);
            float floor = sorted[(int)(bins * 0.2)];

            int lo = Math.Max(0, seed - 80), hi = Math.Min(bins - 1, seed + 80);
            int peak = lo; float pv = db[lo];
            for (int i = lo; i <= hi; i++) if (db[i] > pv) { pv = db[i]; peak = i; }
            if (pv < floor + 6f) return false;

            float t = Math.Max(floor + 3f, pv - 3f);
            int l = peak; while (l - 1 >= 0 && db[l - 1] > t) l--;
            int r = peak; while (r + 1 < bins && db[r + 1] > t) r++;
            double bw = (r - l) * binHz;
            if (bw <= 0) return false;
            double sr = bw / (1.0 + _settings.Rolloff) / 1000.0;
            if (sr < 1 || sr > 100000) return false;

            centerHz = center + (((l + r) / 2.0) / bins - 0.5) * span;
            srKsym = sr; peakDb = pv;
            return true;
        }

        public void TuneStrongest()
        {
            var peaks = DetectPeaks();
            if (peaks.Count == 0) return;

            var p = peaks[0];
            uint satKHz = (uint)Math.Round(p.CenterHz / 1000.0) + _settings.LnbLoKHz;
            uint sr = (uint)Math.Max(1, Math.Round(p.SrKsym));
            if (_selectedReceiver >= 0 && _selectedReceiver < _tunedKHz.Length) _tunedKHz[_selectedReceiver] = satKHz;
            OnSignalSelected?.Invoke(_selectedReceiver, satKHz, sr);
        }

        public void SetAutoTuneMode(int mode)
        {
            _autoTuneMode = mode;
            if (mode == 0) _autoTuneTimer.Stop(); else _autoTuneTimer.Start();
        }

        private void AutoTuneTick()
        {
            try
            {
                var peaks = DetectPeaks();
                if (peaks.Count == 0) return;

                if (_autoTuneMode == 1)
                {
                    uint cur = _tunedKHz[_selectedReceiver];
                    bool there = false;
                    foreach (var p in peaks) { uint k = (uint)Math.Round(p.CenterHz / 1000.0) + _settings.LnbLoKHz; if (Math.Abs((long)k - cur) < 50) { there = true; break; } }
                    if (!there) { var np = peaks[0]; uint k = (uint)Math.Round(np.CenterHz / 1000.0) + _settings.LnbLoKHz; _tunedKHz[_selectedReceiver] = k; OnSignalSelected?.Invoke(_selectedReceiver, k, (uint)Math.Max(1, Math.Round(np.SrKsym))); }
                }
                else if (_autoTuneMode == 2)
                {
                    _autoTimedIndex = (_autoTimedIndex + 1) % peaks.Count;
                    var p = peaks[_autoTimedIndex];
                    uint k = (uint)Math.Round(p.CenterHz / 1000.0) + _settings.LnbLoKHz;
                    _tunedKHz[_selectedReceiver] = k;
                    OnSignalSelected?.Invoke(_selectedReceiver, k, (uint)Math.Max(1, Math.Round(p.SrKsym)));
                }
                else if (_autoTuneMode == 3)
                {
                    int n = Math.Min(4, peaks.Count);
                    var chosen = new List<SdrPeak>();
                    for (int i = 0; i < peaks.Count && chosen.Count < n; i++) chosen.Add(peaks[i]);
                    chosen.Sort((a, b) => a.CenterHz.CompareTo(b.CenterHz));
                    for (int r = 0; r < chosen.Count && r < _tunedKHz.Length; r++)
                    {
                        uint k = (uint)Math.Round(chosen[r].CenterHz / 1000.0) + _settings.LnbLoKHz;
                        if (Math.Abs((long)k - _tunedKHz[r]) < 30) continue;
                        _tunedKHz[r] = k;
                        OnSignalSelected?.Invoke(r, k, (uint)Math.Max(1, Math.Round(chosen[r].SrKsym)));
                    }
                }
            }
            catch { }
        }

        private void ShowMenu(Point p)
        {
            var menu = new System.Windows.Controls.ContextMenu();

            var strongest = new System.Windows.Controls.MenuItem { Header = "Tune to strongest peak" };
            strongest.Click += (s, e) => TuneStrongest();
            menu.Items.Add(strongest);

            var snap = new System.Windows.Controls.MenuItem { Header = "Snap clicks to nearest peak", IsCheckable = true, IsChecked = _snapToPeak };
            snap.Click += (s, e) => { _snapToPeak = snap.IsChecked; _settings.SnapToPeak = snap.IsChecked; new SettingsManager<SdrSettings>("sdr_settings").SaveSettings(_settings); };
            menu.Items.Add(snap);

            var auto = new System.Windows.Controls.MenuItem { Header = "Auto-tune" };
            AddAuto(auto, "Off (manual)", 0);
            AddAuto(auto, "Auto-wait", 1);
            AddAuto(auto, "Auto-timed (2s)", 2);
            AddAuto(auto, "All RX (spread)", 3);
            menu.Items.Add(auto);

            menu.Items.Add(new System.Windows.Controls.Separator());
            var bb = new System.Windows.Controls.MenuItem { Header = "Broadband preset (741.5 MHz)" };
            bb.Click += (s, e) => SetCenterFrequency(_settings.BroadbandCenterHz);
            menu.Items.Add(bb);
            var nb = new System.Windows.Controls.MenuItem { Header = "Narrowband preset (739.5 MHz)" };
            nb.Click += (s, e) => SetCenterFrequency(_settings.NarrowbandCenterHz);
            menu.Items.Add(nb);

            var set = new System.Windows.Controls.MenuItem { Header = "SDR Settings..." };
            set.Click += (s, e) => OnRequestSettings?.Invoke();
            menu.Items.Add(set);

            menu.IsOpen = true;
            menu.PlacementTarget = this;
        }

        private void AddAuto(System.Windows.Controls.MenuItem parent, string text, int mode)
        {
            var item = new System.Windows.Controls.MenuItem { Header = text, IsCheckable = true, IsChecked = _autoTuneMode == mode };
            item.Click += (s, e) => SetAutoTuneMode(mode);
            parent.Items.Add(item);
        }

        public event Action OnRequestSettings;
    }
}
