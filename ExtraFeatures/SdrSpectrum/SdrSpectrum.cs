using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using NAudio.Dsp;
using NAudio.Wave;
using opentuner.Utilities;
using Serilog;

namespace opentuner.ExtraFeatures.SdrSpectrum
{
    /// <summary>
    /// Local/remote SDR spectrum and waterfall display (RTL-SDR, rtl_tcp, HackRF).
    /// Renders into a PictureBox and raises OnSignalSelected when the user tunes.
    /// </summary>
    public class SdrSpectrum
    {
        public delegate void SignalSelected(int Receiver, uint Freq, uint SymbolRate);
        public event SignalSelected OnSignalSelected;

        public delegate void StatusChanged(string status);
        public event StatusChanged OnStatus;

        private readonly PictureBox _display;
        private readonly int _tuners;

        private readonly SdrSettings _settings;
        private readonly SettingsManager<SdrSettings> _settingsManager;

        private ISdrDevice _device;

        // FFT
        private Complex[] _fftBuffer;
        private int _fftSize = 2048;
        private int _fftM;
        private int _fftIndex;
        private float[] _window;
        private float[] _spectrumDb;
        private float[] _displayDb;
        private volatile bool _haveSpectrum;

        private readonly object _spectrumLock = new object();

        // rendering
        private Bitmap _bmp;
        private Graphics _gfx;
        private Bitmap _waterfall;
        private Graphics _waterfallGfx;
        private System.Windows.Forms.Timer _renderTimer;

        private Font _fontAxis;
        private Font _fontReadout;
        private Font _fontStatus;
        private Font _fontMessage;
        private Pen _peakPen;
        private Pen _markerPen;
        private Pen _gridPen;
        private Pen _spectrumPen;
        private Pen _rxSelPen;
        private Pen _rxPen;
        private SolidBrush _spectrumBackBrush;

        private int _markerX = -1;
        private double _markerFreqHz;
        private float _markerPowerDb;
        private double _peakFreqHz;
        private float _peakPowerDb = -999;
        private int _peakBin;
        private double _peakSrKsym;

        private volatile bool _connected;
        private string _status = "Disconnected";

        private int _selectedReceiver = 0;
        private uint _lastTunedKHz;
        private bool _broadband = true;
        private readonly uint[] _tunedKHz = new uint[8];   // satellite kHz tuned per receiver

        // sweep / stitching
        private bool _sweepActive;
        private float[] _sweepDb;
        private int _sweepBins;
        private double _sweepResHz;
        private double _sweepStartHz;
        private int _sweepSteps;
        private int _sweepStep;
        private double _sweepStepCenterHz;
        private DateTime _sweepLastStepUtc = DateTime.MinValue;
        private int _sweepCycle;
        private int _sweepLastWaterfallCycle = -1;
        private double _sweepStepHz;

        // autotune / peak snapping
        public sealed class SdrPeak
        {
            public double CenterHz;
            public float PowerDb;
            public double BwHz;
            public double SrKsym;
        }

        private System.Windows.Forms.Timer _autoTuneTimer;
        private int _autoTuneMode = 0;    // 0 manual, 1 auto-wait, 2 auto-timed, 3 all-RX spread
        private bool _snapToPeak = true;
        private readonly DateTime[] _rxTunedTime = new DateTime[8];
        private int _autoTimedIndex;
        private const int AutoTuneTickMs = 2000;

        // audio
        private WaveOut _waveOut;
        private BufferedWaveProvider _audioProvider;
        private readonly int _audioSampleRate = 12000;
        private double _audioPhase;
        private double _audioPhaseInc;
        private float _audioAccI, _audioAccQ;
        private int _audioAccCount;
        private int _audioDecim = 200;
        private float _audioDc;
        private readonly object _audioLock = new object();

        public SdrSpectrum(PictureBox display, int tuners)
        {
            _display = display;
            _tuners = tuners;

            _settings = new SdrSettings();
            _settingsManager = new SettingsManager<SdrSettings>("sdr_settings");
            _settings = _settingsManager.LoadSettings(_settings);

            _snapToPeak = _settings.SnapToPeak;

            _autoTuneTimer = new System.Windows.Forms.Timer { Interval = AutoTuneTickMs };
            _autoTuneTimer.Tick += (s, e) => AutoTuneTick();

            _display.Click += Display_Click;
            _display.MouseMove += Display_MouseMove;
            _display.MouseLeave += Display_MouseLeave;
            _display.MouseDoubleClick += Display_MouseDoubleClick;
            _display.SizeChanged += Display_SizeChanged;

            ConfigureFft(_settings.FftSize);
            CreateDrawingResources();

            _renderTimer = new System.Windows.Forms.Timer();
            _renderTimer.Interval = 40;   // ~25 fps
            _renderTimer.Tick += RenderTimer_Tick;
            _renderTimer.Enabled = true;

            RecreateBitmaps();

            Connect();
        }

        public SdrSettings Settings => _settings;

        public void Connect()
        {
            Disconnect();

            switch (_settings.SourceType)
            {
                case SdrSourceType.RtlTcp:
                    _device = new RtlTcpDevice(_settings.RtlTcpHost, _settings.RtlTcpPort);
                    break;
                case SdrSourceType.RtlSdr:
                    _device = new RtlSdrDevice(_settings.DeviceIndex);
                    break;
                case SdrSourceType.HackRf:
                    _device = new HackRfDevice();
                    break;
                case SdrSourceType.Pluto:
                    _device = new PlutoSdrDevice(_settings.PlutoUri);
                    break;
                default:
                    _device = new RtlTcpDevice(_settings.RtlTcpHost, _settings.RtlTcpPort);
                    break;
            }

            _status = "Connecting to " + _device.Name + " ...";
            OnStatus?.Invoke(_status);

            if (!_device.Open())
            {
                _connected = false;
                _status = _device.Name + " unavailable: " + _device.LastError;
                OnStatus?.Invoke(_status);
                Log.Warning("SDR: " + _status);
                return;
            }

            _device.SetSampleRate(_settings.SampleRateHz);
            _device.SetGain(_settings.GainDb, _settings.Agc);
            _device.SetPpm(_settings.PpmCorrection);
            _device.SetCenterFrequency(_settings.CenterFrequencyHz);

            ConfigureSweep();

            _device.Start(OnSamples);

            ApplyAudioSettings();
            UpdateAudioTuning();

            _connected = true;
            _status = _device.Name + " @ " + (_settings.CenterFrequencyHz / 1e6).ToString("0.000") + " MHz, " +
                      (_settings.SampleRateHz / 1e6).ToString("0.00") + " Msps";
            OnStatus?.Invoke(_status);

            Log.Information("SDR: " + _status);
        }

        public void Disconnect()
        {
            try { _device?.Stop(); } catch { }
            try { _device?.Close(); } catch { }
            try { _device?.Dispose(); } catch { }
            _device = null;
            _connected = false;

            StopAudio();
        }

        public void SetCenterFrequency(uint hz)
        {
            _settings.CenterFrequencyHz = hz;
            _device?.SetCenterFrequency(hz);

            if (_connected)
            {
                _status = _device.Name + " @ " + (hz / 1e6).ToString("0.000") + " MHz, " +
                          (_settings.SampleRateHz / 1e6).ToString("0.00") + " Msps";
                OnStatus?.Invoke(_status);
            }
        }

        public void SetSampleRate(uint hz)
        {
            _settings.SampleRateHz = hz;
            _device?.SetSampleRate(hz);
        }

        /// <summary>
        /// Follows the tuner's LNB power selection: horizontal = wideband (741.5 MHz),
        /// vertical = narrowband (739.5 MHz). supply: 0 = off, 1 = vertical, 2 = horizontal.
        /// </summary>
        public void ApplyPolarization(int supply)
        {
            if (!_settings.FollowLnbPolarization)
                return;

            if (supply == 0)
                return;

            _broadband = supply == 2;
            uint center = _broadband ? _settings.BroadbandCenterHz : _settings.NarrowbandCenterHz;

            Log.Information("SDR: following LNB power -> " + (_broadband ? "wideband" : "narrowband") +
                            " (" + (center / 1e6).ToString("0.000") + " MHz)");

            SetCenterFrequency(center);
        }

        public void SetGain(int tenthsDb, bool agc)
        {
            _settings.GainDb = tenthsDb;
            _settings.Agc = agc;
            _device?.SetGain(tenthsDb, agc);
        }

        public void ApplySettings()
        {
            if (_device != null)
            {
                _device.SetSampleRate(_settings.SampleRateHz);
                _device.SetGain(_settings.GainDb, _settings.Agc);
                _device.SetPpm(_settings.PpmCorrection);
                _device.SetCenterFrequency(_settings.CenterFrequencyHz);
            }

            ConfigureFft(_settings.FftSize);
            ConfigureSweep();
            RecreateBitmaps();
            ApplyAudioSettings();
            _settingsManager.SaveSettings(_settings);
        }

        public void Save()
        {
            _settingsManager.SaveSettings(_settings);
        }

        public void Close()
        {
            _renderTimer?.Stop();
            _renderTimer?.Dispose();
            Disconnect();
            Save();

            try { _gfx?.Dispose(); } catch { }
            try { _bmp?.Dispose(); } catch { }
            try { _waterfallGfx?.Dispose(); } catch { }
            try { _waterfall?.Dispose(); } catch { }
            try { _fontAxis?.Dispose(); } catch { }
            try { _fontReadout?.Dispose(); } catch { }
            try { _fontStatus?.Dispose(); } catch { }
            try { _fontMessage?.Dispose(); } catch { }
            try { _peakPen?.Dispose(); } catch { }
            try { _markerPen?.Dispose(); } catch { }
            try { _gridPen?.Dispose(); } catch { }
            try { _spectrumPen?.Dispose(); } catch { }
            try { _rxSelPen?.Dispose(); } catch { }
            try { _rxPen?.Dispose(); } catch { }
            try { _spectrumBackBrush?.Dispose(); } catch { }
        }

        // --------------------------------------------------------------
        // FFT / sample processing
        // --------------------------------------------------------------
        private void ConfigureFft(int size)
        {
            // round to a power of two
            int s = 256;
            while (s < size && s < 32768)
                s <<= 1;

            _fftSize = s;
            _fftM = (int)Math.Round(Math.Log(_fftSize, 2));
            _fftBuffer = new Complex[_fftSize];
            lock (_spectrumLock)
            {
                _spectrumDb = new float[_fftSize];
                _displayDb = new float[_fftSize];
            }

            _window = new float[_fftSize];
            for (int i = 0; i < _fftSize; i++)
                _window[i] = (float)(0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (_fftSize - 1))));

            _fftIndex = 0;
        }

        private void OnSamples(byte[] data, int count)
        {
            try
            {
                SdrSampleFormat fmt = _device?.SampleFormat ?? SdrSampleFormat.Unsigned8;

                if (fmt == SdrSampleFormat.Signed16)
                {
                    for (int i = 0; i + 3 < count; i += 4)
                    {
                        float ii = BitConverter.ToInt16(data, i) / 32768f;
                        float qq = BitConverter.ToInt16(data, i + 2) / 32768f;
                        ProcessSample(ii, qq);
                    }
                }
                else
                {
                    bool signed = fmt == SdrSampleFormat.Signed8;

                    for (int i = 0; i + 1 < count; i += 2)
                    {
                        float ii = ConvertSample(data[i], signed);
                        float qq = ConvertSample(data[i + 1], signed);
                        ProcessSample(ii, qq);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SDR: sample processing error");
            }
        }

        private void ProcessSample(float ii, float qq)
        {
            if (_settings.AudioEnabled && !_sweepActive)
                ProcessAudio(ii, qq);

            _fftBuffer[_fftIndex].X = ii * _window[_fftIndex];
            _fftBuffer[_fftIndex].Y = qq * _window[_fftIndex];
            _fftIndex++;

            if (_fftIndex >= _fftSize)
            {
                _fftIndex = 0;
                ComputeFft();
            }
        }

        private static float ConvertSample(byte b, bool signed)
        {
            if (signed)
                return (sbyte)b / 128f;
            return (b - 127.5f) / 127.5f;
        }

        private void ComputeFft()
        {
            FastFourierTransform.FFT(true, _fftM, _fftBuffer);

            lock (_spectrumLock)
            {
                if (_spectrumDb == null)
                    return;

                float peakDb = -999;
                double peakBin = 0;

                for (int k = 0; k < _fftSize; k++)
                {
                    int idx = (k + _fftSize / 2) % _fftSize;   // fftshift
                    Complex c = _fftBuffer[idx];
                    float mag = (float)Math.Sqrt(c.X * c.X + c.Y * c.Y) / _fftSize;
                    float db = 20f * (float)Math.Log10(mag + 1e-9f);

                    _spectrumDb[k] = (_spectrumDb[k] == 0f) ? db : (_spectrumDb[k] * 0.75f + db * 0.25f);

                    if (_spectrumDb[k] > peakDb)
                    {
                        peakDb = _spectrumDb[k];
                        peakBin = k;
                    }
                }

                _peakPowerDb = peakDb;
                _peakFreqHz = BinToHz(peakBin);
                _peakBin = (int)peakBin;

                Array.Copy(_spectrumDb, _displayDb, _fftSize);

                // stitch this step's FFT into the sweep composite
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

        // ---- view mapping (single FFT, or the stitched sweep) ----
        private bool SweepDisplay => _sweepActive && _sweepDb != null;
        private int ViewBins => SweepDisplay ? _sweepBins : _fftSize;
        private double ViewCenterHz => SweepDisplay ? _settings.SweepCenterHz : _settings.CenterFrequencyHz;
        private double ViewSpanHz => SweepDisplay ? _settings.SweepSpanHz : _settings.SampleRateHz;

        private double BinToHz(double bin)
        {
            double frac = bin / _fftSize;
            return _settings.CenterFrequencyHz + (frac - 0.5) * _settings.SampleRateHz;
        }

        private double ViewBinToHz(double bin)
        {
            return ViewCenterHz + (bin / ViewBins - 0.5) * ViewSpanHz;
        }

        private double HzToX(double hz, int width)
        {
            double frac = (hz - ViewCenterHz) / ViewSpanHz + 0.5;
            return frac * width;
        }

        private double XToHz(double x, int width)
        {
            return ViewCenterHz + (x / width - 0.5) * ViewSpanHz;
        }

        private float[] SnapshotView()
        {
            lock (_spectrumLock)
            {
                if (SweepDisplay)
                    return (float[])_sweepDb.Clone();
                return _displayDb != null ? (float[])_displayDb.Clone() : null;
            }
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

            lock (_spectrumLock)
            {
                _sweepDb = new float[_sweepBins];
            }

            _sweepStartHz = (double)_settings.SweepCenterHz - _settings.SweepSpanHz / 2.0;
            _sweepStepHz = _settings.SampleRateHz * 0.85;
            _sweepSteps = Math.Max(1, (int)Math.Ceiling(_settings.SweepSpanHz / _sweepStepHz));
            _sweepStep = 0;
            _sweepCycle = 0;
            _sweepLastWaterfallCycle = -1;
            _sweepLastStepUtc = DateTime.MinValue;
            ApplySweepStep();

            Log.Information("SDR: sweep enabled - " + (_settings.SweepSpanHz / 1e6).ToString("0.0") +
                            " MHz in " + _sweepSteps + " steps of " + (_sweepStepHz / 1e6).ToString("0.00") + " MHz");
        }

        private void ApplySweepStep()
        {
            double center = _sweepStartHz + _settings.SampleRateHz / 2.0 + _sweepStep * _sweepStepHz;
            _sweepStepCenterHz = center;
            _device?.SetCenterFrequency((uint)Math.Round(center));
            _fftIndex = 0;
        }

        private void PumpSweep()
        {
            if (!_sweepActive || _sweepDb == null)
                return;

            if ((DateTime.UtcNow - _sweepLastStepUtc).TotalMilliseconds < _settings.SweepDwellMs)
                return;

            _sweepLastStepUtc = DateTime.UtcNow;
            _sweepStep++;

            if (_sweepStep >= _sweepSteps)
            {
                _sweepStep = 0;
                _sweepCycle++;
            }

            ApplySweepStep();
        }

        // --------------------------------------------------------------
        // Rendering
        // --------------------------------------------------------------
        private void RecreateBitmaps()
        {
            int w = Math.Max(64, _display.Width);
            int h = Math.Max(64, _display.Height);

            try { _gfx?.Dispose(); } catch { }
            try { _bmp?.Dispose(); } catch { }
            try { _waterfallGfx?.Dispose(); } catch { }
            try { _waterfall?.Dispose(); } catch { }

            _bmp = new Bitmap(w, h);
            _gfx = Graphics.FromImage(_bmp);
            _gfx.SmoothingMode = SmoothingMode.AntiAlias;

            _waterfall = new Bitmap(w, h);
            _waterfallGfx = Graphics.FromImage(_waterfall);
        }

        private void Display_SizeChanged(object sender, EventArgs e)
        {
            try { RecreateBitmaps(); } catch { }
        }

        private void RenderTimer_Tick(object sender, EventArgs e)
        {
            if (_sweepActive)
                PumpSweep();

            if (!_haveSpectrum)
            {
                if (!_connected)
                    DrawMessage("SDR: " + _status + "\n(right-click the panel for settings)");

                UpdateDrawing();
                return;
            }

            Draw();
            UpdateDrawing();
        }

        private void DrawMessage(string message)
        {
            try
            {
                _gfx.Clear(Color.Black);
                _gfx.DrawString(message, _fontMessage, Brushes.Orange, new PointF(12, 12));
            }
            catch { }
        }

        private void Draw()
        {
            int w = _bmp.Width;
            int h = _bmp.Height;
            int specH = (int)(h * 0.62);

            float[] db = SnapshotView();
            if (db == null)
                return;

            int bins = ViewBins;

            // When sweeping, only scroll the waterfall once per full sweep cycle.
            bool scrollWaterfall = true;
            if (SweepDisplay)
            {
                scrollWaterfall = _sweepCycle != _sweepLastWaterfallCycle;
                if (scrollWaterfall)
                    _sweepLastWaterfallCycle = _sweepCycle;
            }

            if (scrollWaterfall)
            {
                using (Graphics wf = Graphics.FromImage(_waterfall))
                {
                    wf.DrawImageUnscaled(_waterfall, 0, -1);

                    for (int x = 0; x < w; x++)
                    {
                        int bin = (int)((double)x / w * (bins - 1));
                        wf.DrawRectangle(GetWaterfallPen(Level(db[bin])), x, h - 1, 1, 1);
                    }
                }
            }

            _gfx.Clear(Color.Black);
            _gfx.DrawImageUnscaled(_waterfall, 0, 0);

            // --- spectrum on the upper part ---
            _gfx.FillRectangle(_spectrumBackBrush, 0, 0, w, specH);

            PointF[] points = new PointF[w];
            for (int x = 0; x < w; x++)
            {
                int bin = (int)((double)x / w * (bins - 1));
                points[x] = new PointF(x, specH - Level(db[bin]) * (specH - 2));
            }

            using (LinearGradientBrush fill = new LinearGradientBrush(
                new Point(0, 0), new Point(0, specH),
                Color.FromArgb(225, 255, 190, 60),
                Color.FromArgb(225, 40, 90, 200)))
            {
                _gfx.FillPolygon(fill, points);
            }
            _gfx.DrawLines(_spectrumPen, points);

            // --- grid + frequency labels ---
            for (int i = 0; i <= 8; i++)
            {
                int x = i * w / 8;
                _gfx.DrawLine(_gridPen, x, 0, x, specH);
                double f = XToHz(x, w);
                string label = (f / 1e6).ToString("0.000") + " MHz";
                _gfx.DrawString(label, _fontAxis, Brushes.LightGray, x + 2, 2);
            }

            // --- peak (measured from the current view) ---
            int peakBin = 0;
            float peakVal = db[0];
            for (int i = 1; i < bins; i++)
            {
                if (db[i] > peakVal) { peakVal = db[i]; peakBin = i; }
            }

            double dispFreq = ViewBinToHz(peakBin);
            float dispPow = peakVal;
            string srText = "";
            _peakSrKsym = 0;

            if (_settings.EstimateSymbolRate && _broadband &&
                TryEstimateSignal(db, bins, ViewCenterHz, ViewSpanHz, peakBin,
                                  out double estHz, out double estSr, out float estDb))
            {
                _peakSrKsym = estSr;
                dispFreq = estHz;
                dispPow = estDb;
                srText = "  SR ~" + estSr.ToString("0") + " ksym";
            }

            int px = (int)HzToX(dispFreq, w);
            _gfx.DrawLine(_peakPen, px, 0, px, specH);
            _gfx.DrawString("Peak " + (dispFreq / 1e6).ToString("0.0000") + " MHz  " +
                            dispPow.ToString("0.0") + " dB" + srText, _fontReadout, Brushes.Salmon, 6, specH - 18);

            if (_markerX >= 0)
            {
                _gfx.DrawLine(_markerPen, _markerX, 0, _markerX, specH);

                string info = "Marker " + (_markerFreqHz / 1e6).ToString("0.0000") + " MHz  " +
                              _markerPowerDb.ToString("0.0") + " dB";
                _gfx.DrawString(info, _fontReadout, Brushes.DeepSkyBlue, 6, specH - 32);
            }

            // per-receiver tuned-frequency markers
            for (int r = 0; r < _tunedKHz.Length; r++)
            {
                if (_tunedKHz[r] == 0)
                    continue;

                double ifHz = ((double)_tunedKHz[r] - _settings.LnbLoKHz) * 1000.0;  // kHz -> Hz
                int rx = (int)HzToX(ifHz, w);

                if (rx < 0 || rx > w)
                    continue;

                bool sel = r == _selectedReceiver;
                _gfx.DrawLine(sel ? _rxSelPen : _rxPen, rx, 0, rx, specH);
                _gfx.DrawString("RX" + (r + 1).ToString(), _fontReadout,
                                sel ? Brushes.Cyan : Brushes.LightGreen, rx + 2, 3);
            }

            if (SweepDisplay)
            {
                _gfx.DrawString("SWEEP " + (ViewSpanHz / 1e6).ToString("0.0") + " MHz  step " +
                                (_sweepStep + 1).ToString() + "/" + _sweepSteps.ToString(),
                                _fontReadout, Brushes.Gold, w - 230, 3);
            }

            _gfx.DrawString(_status, _fontStatus, Brushes.Gainsboro, 6, h - 16);
        }

        private void CreateDrawingResources()
        {
            _fontAxis = new Font("Segoe UI", 7.5f);
            _fontReadout = new Font("Segoe UI", 8f);
            _fontStatus = new Font("Segoe UI", 8f);
            _fontMessage = new Font("Segoe UI", 11f);
            _peakPen = new Pen(Color.FromArgb(200, 255, 60, 60));
            _markerPen = new Pen(Color.FromArgb(200, 120, 220, 255));
            _gridPen = new Pen(Color.FromArgb(60, 255, 255, 255));
            _spectrumPen = new Pen(Color.FromArgb(255, 255, 235, 120));
            _rxSelPen = new Pen(Color.Cyan);
            _rxPen = new Pen(Color.LightGreen);
            _spectrumBackBrush = new SolidBrush(Color.FromArgb(255, 8, 9, 14));
        }

        private float Level(float db)
        {
            float v = (db - _settings.MinDb) / (float)(_settings.MaxDb - _settings.MinDb);
            if (v < 0) v = 0;
            if (v > 1) v = 1;
            return v;
        }

        /// <summary>
        /// Estimates a signal's centre frequency, power and symbol rate from the
        /// width of the peak near the given bin. Occupied bandwidth is measured at
        /// -3 dB and converted: symbol rate = bandwidth / (1 + rolloff).
        /// </summary>
        private bool TryEstimateSignal(float[] db, int bins, double viewCenterHz, double viewSpanHz,
                                       int seedBin, out double centerHz, out double srKsym, out float peakDb)
        {
            centerHz = 0;
            srKsym = 0;
            peakDb = -999;

            if (db == null)
                return false;

            int n = bins;
            if (n < 32)
                return false;

            if (seedBin < 0) seedBin = 0;
            if (seedBin >= n) seedBin = n - 1;

            double binHz = viewSpanHz / n;

            // noise floor from the 20th percentile
            float[] sorted = (float[])db.Clone();
            Array.Sort(sorted);
            float noiseFloor = sorted[(int)(n * 0.2)];

            int lo = Math.Max(0, seedBin - 80);
            int hi = Math.Min(n - 1, seedBin + 80);

            int peak = lo;
            float peakV = db[lo];
            for (int i = lo; i <= hi; i++)
            {
                if (db[i] > peakV) { peakV = db[i]; peak = i; }
            }

            if (peakV < noiseFloor + 6f)
                return false;

            float threshold = Math.Max(noiseFloor + 3f, peakV - 3f);

            int left = peak;
            while (left - 1 >= 0 && db[left - 1] > threshold) left--;

            int right = peak;
            while (right + 1 < n && db[right + 1] > threshold) right++;

            double bwHz = (right - left) * binHz;
            if (bwHz <= 0)
                return false;

            double sr = bwHz / (1.0 + _settings.Rolloff) / 1000.0;   // ksym/s

            if (sr < 1 || sr > 100000)
                return false;

            centerHz = viewCenterHz + (((left + right) / 2.0) / n - 0.5) * viewSpanHz;
            srKsym = sr;
            peakDb = peakV;
            return true;
        }

        private static readonly Pen[] _waterfallPens = BuildWaterfallPens();

        private static Pen[] BuildWaterfallPens()
        {
            Pen[] pens = new Pen[256];
            for (int i = 0; i < 256; i++)
            {
                float t = i / 255f;
                Color c;
                if (t < 0.33f) c = Lerp(Color.Black, Color.FromArgb(0, 0, 180), t / 0.33f);
                else if (t < 0.66f) c = Lerp(Color.FromArgb(0, 0, 180), Color.Cyan, (t - 0.33f) / 0.33f);
                else c = Lerp(Color.Cyan, Color.Red, (t - 0.66f) / 0.34f);
                pens[i] = new Pen(c);
            }
            return pens;
        }

        private static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private static Pen GetWaterfallPen(float v)
        {
            int idx = (int)(v * 255);
            if (idx < 0) idx = 0;
            if (idx > 255) idx = 255;
            return _waterfallPens[idx];
        }

        private void UpdateDrawing()
        {
            try
            {
                _display.Parent?.Invoke(new MethodInvoker(delegate ()
                {
                    try
                    {
                        _display.Image = _bmp;
                        _display.Update();
                    }
                    catch { }
                }));
            }
            catch { }
        }

        // --------------------------------------------------------------
        // Mouse / tuning
        // --------------------------------------------------------------
        private void Display_MouseMove(object sender, MouseEventArgs e)
        {
            _markerX = e.X;
            _markerFreqHz = XToHz(e.X, _display.Width);

            if (_haveSpectrum)
            {
                int width = Math.Max(1, _display.Width);
                int bins = ViewBins;
                int bin = (int)((double)e.X / width * (bins - 1));

                lock (_spectrumLock)
                {
                    float[] src = SweepDisplay ? _sweepDb : _displayDb;

                    if (src != null && bin >= 0 && bin < src.Length)
                        _markerPowerDb = src[bin];
                }
            }
        }

        private void Display_MouseLeave(object sender, EventArgs e)
        {
            _markerX = -1;
        }

        private void Display_Click(object sender, EventArgs e)
        {
            MouseEventArgs me = (MouseEventArgs)e;

            if (me.Button == MouseButtons.Right)
            {
                ShowContextMenu();
                return;
            }

            TuneTo(e);
        }

        private void Display_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                TuneTo(e);
        }

        private void TuneTo(EventArgs e)
        {
            MouseEventArgs me = (MouseEventArgs)e;
            int width = Math.Max(1, _display.Width);
            double ifHz = XToHz(me.X, width);

            uint symbolRate = _broadband ? _settings.BroadbandSymbolRate : _settings.NarrowbandSymbolRate;

            // Wideband: snap to the nearest detected peak, and/or estimate the symbol
            // rate from the measured peak width (occupied BW = SR * (1 + rolloff)).
            if (_broadband && (_snapToPeak || _settings.EstimateSymbolRate))
            {
                List<SdrPeak> peaks = DetectPeaks();
                SdrPeak nearest = null;
                double nearestDist = double.MaxValue;

                foreach (SdrPeak p in peaks)
                {
                    double d = Math.Abs(p.CenterHz - ifHz);
                    if (d < nearestDist) { nearestDist = d; nearest = p; }
                }

                double snapWindow = Math.Max(25000, ViewSpanHz * 0.03);

                if (_snapToPeak && nearest != null && nearestDist <= snapWindow)
                {
                    ifHz = nearest.CenterHz;
                    symbolRate = (uint)Math.Max(1, Math.Round(nearest.SrKsym));

                    Log.Information("SDR: snapped to peak " + (ifHz / 1e6).ToString("0.0000") + " MHz, SR ~" +
                                    symbolRate + " ksym");
                }
                else if (_settings.EstimateSymbolRate)
                {
                    float[] db = SnapshotView();
                    int bins = ViewBins;
                    int clickBin = (int)((double)me.X / width * (bins - 1));

                    if (TryEstimateSignal(db, bins, ViewCenterHz, ViewSpanHz, clickBin,
                                          out double estHz, out double estSr, out float estDb))
                    {
                        ifHz = estHz;
                        symbolRate = (uint)Math.Max(1, Math.Round(estSr));

                        Log.Information("SDR: detected signal " + (estHz / 1e6).ToString("0.0000") + " MHz, SR ~" +
                                        symbolRate + " ksym (" + estDb.ToString("0.0") + " dB)");
                    }
                }
            }

            // The tuner expects the satellite frequency; add the LNB LO offset.
            uint satelliteKHz = (uint)Math.Round(ifHz / 1000.0) + _settings.LnbLoKHz;

            _lastTunedKHz = satelliteKHz;

            if (_selectedReceiver >= 0 && _selectedReceiver < _tunedKHz.Length)
                _tunedKHz[_selectedReceiver] = satelliteKHz;

            // keep the audio demodulator centred on the clicked signal
            _settings.AudioOffsetHz = (int)(ifHz - _settings.CenterFrequencyHz);
            UpdateAudioTuning();

            Log.Information("SDR: tuning to IF " + (ifHz / 1e6).ToString("0.000") +
                            " MHz => " + (satelliteKHz / 1000.0).ToString("0.000") + " MHz, SR " + symbolRate);

            OnSignalSelected?.Invoke(_selectedReceiver, satelliteKHz, symbolRate);
        }

        private ContextMenuStrip _menu;

        private void ShowContextMenu()
        {
            if (_menu == null)
                _menu = new ContextMenuStrip();

            _menu.Items.Clear();

            _menu.Items.Add("Tune to strongest peak", null, (s, e) => TuneStrongest());

            ToolStripMenuItem snap = new ToolStripMenuItem("Snap clicks to nearest peak")
            {
                CheckOnClick = true,
                Checked = _snapToPeak
            };
            snap.CheckedChanged += (s, e) => { _snapToPeak = snap.Checked; _settings.SnapToPeak = snap.Checked; Save(); };
            _menu.Items.Add(snap);

            ToolStripMenuItem auto = new ToolStripMenuItem("Auto-tune");
            auto.DropDownItems.Add(MakeAutoTuneItem("Off (manual)", 0));
            auto.DropDownItems.Add(MakeAutoTuneItem("Auto-wait (keep RX on a live signal)", 1));
            auto.DropDownItems.Add(MakeAutoTuneItem("Auto-timed (rotate every " + (AutoTuneTickMs / 1000) + "s)", 2));
            auto.DropDownItems.Add(MakeAutoTuneItem("All RX (spread across peaks)", 3));
            _menu.Items.Add(auto);

            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("Broadband preset (741.5 MHz)", null, (s, e) => SetCenterFrequency(_settings.BroadbandCenterHz));
            _menu.Items.Add("Narrowband preset (739.5 MHz)", null, (s, e) => SetCenterFrequency(_settings.NarrowbandCenterHz));
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("SDR Settings ...", null, (s, e) => OnRequestSettings?.Invoke());

            _menu.Show(_display, _display.PointToClient(Cursor.Position));
        }

        private ToolStripMenuItem MakeAutoTuneItem(string text, int mode)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text)
            {
                Checked = _autoTuneMode == mode
            };
            item.Click += (s, e) => SetAutoTuneMode(mode);
            return item;
        }

        public event Action OnRequestSettings;

        // --------------------------------------------------------------
        // Peak detection / snapping / autotune
        // --------------------------------------------------------------
        public List<SdrPeak> DetectPeaks()
        {
            float[] db = SnapshotView();

            List<SdrPeak> peaks = new List<SdrPeak>();
            if (db == null)
                return peaks;

            int bins = ViewBins;
            if (bins < 32)
                return peaks;

            double center = ViewCenterHz;
            double span = ViewSpanHz;
            double binHz = span / bins;

            float[] sorted = (float[])db.Clone();
            Array.Sort(sorted);
            float noiseFloor = sorted[(int)(bins * 0.25)];
            float threshold = noiseFloor + 6f;

            for (int i = 2; i < bins - 2; i++)
            {
                if (db[i] < threshold)
                    continue;

                // local maximum
                if (db[i] < db[i - 1] || db[i] < db[i + 1] || db[i] <= db[i - 2] || db[i] <= db[i + 2])
                    continue;

                float pk = db[i];
                float t = Math.Max(noiseFloor + 3f, pk - 3f);

                int l = i;
                while (l - 1 >= 0 && db[l - 1] > t) l--;
                int r = i;
                while (r + 1 < bins && db[r + 1] > t) r++;

                double bw = (r - l) * binHz;
                double sr = bw / (1.0 + _settings.Rolloff) / 1000.0;

                if (sr < 1 || sr > 100000)
                    continue;

                peaks.Add(new SdrPeak
                {
                    CenterHz = center + (((l + r) / 2.0) / bins - 0.5) * span,
                    PowerDb = pk,
                    BwHz = bw,
                    SrKsym = sr
                });
            }

            // keep the strongest, dropping any that overlap a stronger peak
            peaks.Sort((a, b) => b.PowerDb.CompareTo(a.PowerDb));
            const double minSepHz = 30000;

            List<SdrPeak> kept = new List<SdrPeak>();
            foreach (SdrPeak p in peaks)
            {
                bool overlaps = false;
                foreach (SdrPeak k in kept)
                {
                    if (Math.Abs(k.CenterHz - p.CenterHz) < Math.Max(minSepHz, (k.BwHz + p.BwHz) / 2.0))
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (!overlaps)
                    kept.Add(p);

                if (kept.Count >= 32)
                    break;
            }

            return kept;
        }

        public void TuneStrongest()
        {
            List<SdrPeak> peaks = DetectPeaks();

            if (peaks.Count == 0)
            {
                Log.Information("SDR: tune strongest - no peaks found");
                return;
            }

            TuneToPeak(peaks[0], _selectedReceiver, true);
            Log.Information("SDR: tuned strongest peak " + (peaks[0].CenterHz / 1e6).ToString("0.0000") +
                            " MHz SR ~" + peaks[0].SrKsym.ToString("0") + " ksym");
        }

        private void TuneToPeak(SdrPeak peak, int rx, bool force)
        {
            if (rx < 0 || rx >= _tunedKHz.Length)
                return;

            uint satKHz = (uint)Math.Round(peak.CenterHz / 1000.0) + _settings.LnbLoKHz;
            uint sr = (uint)Math.Max(1, Math.Round(peak.SrKsym));

            // avoid constant retuning when the peak has barely moved
            if (!force && _tunedKHz[rx] != 0 && Math.Abs((long)satKHz - _tunedKHz[rx]) < 30)
                return;

            _tunedKHz[rx] = satKHz;
            _rxTunedTime[rx] = DateTime.UtcNow;

            OnSignalSelected?.Invoke(rx, satKHz, sr);
        }

        /// <summary>0 = off, 1 = auto-wait, 2 = auto-timed, 3 = all-RX spread.</summary>
        public void SetAutoTuneMode(int mode)
        {
            _autoTuneMode = mode;

            if (mode == 0)
            {
                _autoTuneTimer?.Stop();
            }
            else
            {
                _autoTuneTimer?.Start();
            }

            Log.Information("SDR: auto-tune mode = " + mode);
        }

        public int AutoTuneMode => _autoTuneMode;

        private void AutoTuneTick()
        {
            try
            {
                List<SdrPeak> peaks = DetectPeaks();

                if (peaks.Count == 0)
                    return;

                switch (_autoTuneMode)
                {
                    case 1: // auto-wait: keep the selected RX on a live signal
                        {
                            uint current = _tunedKHz[_selectedReceiver];
                            bool stillThere = false;

                            foreach (SdrPeak p in peaks)
                            {
                                uint pKHz = (uint)Math.Round(p.CenterHz / 1000.0) + _settings.LnbLoKHz;
                                if (Math.Abs((long)pKHz - current) < 50)
                                {
                                    stillThere = true;
                                    break;
                                }
                            }

                            if (!stillThere)
                            {
                                Tuple<SdrPeak, int> best = FindUnusedPeak(peaks, _selectedReceiver);
                                if (best != null)
                                    TuneToPeak(best.Item1, _selectedReceiver, true);
                            }
                        }
                        break;

                    case 2: // auto-timed: rotate the selected RX through the peaks
                        {
                            _autoTimedIndex = (_autoTimedIndex + 1) % peaks.Count;
                            TuneToPeak(peaks[_autoTimedIndex], _selectedReceiver, true);
                        }
                        break;

                    case 3: // all-RX spread: distribute peaks across the receivers
                        {
                            int n = Math.Min(Math.Max(1, _tuners), peaks.Count);

                            // strongest n, then laid out left-to-right across the band
                            List<SdrPeak> chosen = new List<SdrPeak>();
                            for (int i = 0; i < peaks.Count && chosen.Count < n; i++)
                                chosen.Add(peaks[i]);
                            chosen.Sort((a, b) => a.CenterHz.CompareTo(b.CenterHz));

                            for (int r = 0; r < chosen.Count && r < _tunedKHz.Length; r++)
                                TuneToPeak(chosen[r], r, false);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SDR: auto-tune tick failed");
            }
        }

        private Tuple<SdrPeak, int> FindUnusedPeak(List<SdrPeak> peaks, int forRx)
        {
            foreach (SdrPeak p in peaks)
            {
                uint pKHz = (uint)Math.Round(p.CenterHz / 1000.0) + _settings.LnbLoKHz;
                bool used = false;

                for (int r = 0; r < _tunedKHz.Length; r++)
                {
                    if (r == forRx)
                        continue;

                    if (_tunedKHz[r] != 0 && Math.Abs((long)pKHz - _tunedKHz[r]) < 100)
                    {
                        used = true;
                        break;
                    }
                }

                if (!used)
                    return new Tuple<SdrPeak, int>(p, forRx);
            }

            return null;
        }

        // --------------------------------------------------------------
        // Narrowband audio (line-up)
        // --------------------------------------------------------------
        private void ApplyAudioSettings()
        {
            if (_settings.AudioEnabled)
            {
                EnsureAudio();
                UpdateAudioTuning();
            }
            else
            {
                StopAudio();
            }
        }

        private void EnsureAudio()
        {
            if (_waveOut != null)
                return;

            _audioProvider = new BufferedWaveProvider(new WaveFormat(_audioSampleRate, 16, 1))
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromMilliseconds(500)
            };

            try
            {
                _waveOut = new WaveOut();
                _waveOut.Init(_audioProvider);
                _waveOut.Play();
            }
            catch (Exception ex)
            {
                Log.Warning("SDR: audio output failed: " + ex.Message);
                _waveOut = null;
            }
        }

        private void StopAudio()
        {
            try { _waveOut?.Stop(); } catch { }
            try { _waveOut?.Dispose(); } catch { }
            _waveOut = null;
            _audioProvider = null;
        }

        private void UpdateAudioTuning()
        {
            // audio passband is at the last tuned IF, relative to the SDR centre
            double offset = _settings.AudioOffsetHz;
            _audioPhaseInc = 2.0 * Math.PI * offset / _settings.SampleRateHz;
            _audioDecim = Math.Max(1, (int)Math.Round((double)_settings.SampleRateHz / _audioSampleRate));
        }

        private void ProcessAudio(float ii, float qq)
        {
            double sign = (_settings.DemodMode == SdrDemodMode.LSB) ? 1.0 : -1.0;
            _audioPhase += sign * _audioPhaseInc;
            if (_audioPhase > Math.PI * 2) _audioPhase -= Math.PI * 2;
            if (_audioPhase < -Math.PI * 2) _audioPhase += Math.PI * 2;

            double c = Math.Cos(_audioPhase);
            double s = Math.Sin(_audioPhase);

            float mixI = (float)(ii * c - qq * s);
            float mixQ = (float)(ii * s + qq * c);

            _audioAccI += mixI;
            _audioAccQ += mixQ;
            _audioAccCount++;

            if (_audioAccCount < _audioDecim)
                return;

            float demI = _audioAccI / _audioAccCount;
            float demQ = _audioAccQ / _audioAccCount;
            _audioAccI = 0;
            _audioAccQ = 0;
            _audioAccCount = 0;

            float sample;

            if (_settings.DemodMode == SdrDemodMode.AM)
            {
                float env = (float)Math.Sqrt(demI * demI + demQ * demQ);
                _audioDc = _audioDc * 0.999f + env * 0.001f;
                sample = env - _audioDc;
            }
            else
            {
                sample = demI;   // product detector (USB/LSB selected by mixing sign)
            }

            float gain = _settings.AudioVolume / 100f;
            short pcm = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, sample * gain * 30000f));

            if (_audioProvider != null)
            {
                byte[] bytes = BitConverter.GetBytes(pcm);
                try { _audioProvider.AddSamples(bytes, 0, 2); } catch { }
            }
        }

        public void SetAudioFrequency(uint ifHz, SdrDemodMode mode)
        {
            _settings.AudioOffsetHz = (int)((long)ifHz - _settings.CenterFrequencyHz);
            _settings.DemodMode = mode;
            UpdateAudioTuning();
        }

        public void SetSelectedReceiver(int receiver)
        {
            _selectedReceiver = receiver;
        }
    }
}
