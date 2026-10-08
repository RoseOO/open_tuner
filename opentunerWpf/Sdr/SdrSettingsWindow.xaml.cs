using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using opentuner.ExtraFeatures.SdrSpectrum;

namespace OpenTuner.Wpf.Sdr
{
    public partial class SdrSettingsWindow : Window
    {
        private readonly SdrSettings _settings;

        private ComboBox comboSource;
        private TextBox txtHost, txtPort, txtPlutoUri, txtDeviceIndex;
        private ComboBox comboSampleRate;
        private TextBox txtCenterMhz, txtGainDb, txtPpm, txtMinDb, txtMaxDb, txtLnbLo, txtBroadbandSr, txtNarrowbandSr;
        private TextBox txtSweepSpan, txtSweepCenter, txtSweepDwell, txtRolloff;
        private CheckBox chkAgc, chkFollowLnb, chkSweep, chkEstimateSr, chkAudio, chkAudioFilter;
        private ComboBox comboDemod;
        private TextBox txtVolume;
        private TextBox txtAudioFilter, txtDeemph;
        private ComboBox comboFft;

        public SdrSettingsWindow(SdrSettings settings)
        {
            InitializeComponent();
            _settings = settings;

            BuildForm();
            LoadValues();
        }

        private int _row;

        private void AddRow(string label, FrameworkElement control)
        {
            var lbl = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 6, 10, 6),
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary")
            };
            Grid.SetColumn(lbl, 0);
            Grid.SetRow(lbl, _row);

            control.Margin = new Thickness(0, 6, 0, 6);
            Grid.SetColumn(control, 1);
            Grid.SetRow(control, _row);

            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.Children.Add(lbl);
            form.Children.Add(control);
            _row++;
        }

        private void BuildForm()
        {
            comboSource = new ComboBox();
            comboSource.Items.Add("RTL-TCP (network/local)");
            comboSource.Items.Add("RTL-SDR (local USB)");
            comboSource.Items.Add("HackRF (local USB)");
            comboSource.Items.Add("PlutoSDR/Pluto+ (USB or network)");
            comboSource.SelectionChanged += (s, e) => PopulateSampleRates(CurrentRate());
            AddRow("SDR source", comboSource);

            txtHost = new TextBox();
            AddRow("rtl_tcp host", txtHost);

            txtPort = new TextBox();
            AddRow("rtl_tcp port", txtPort);

            txtPlutoUri = new TextBox();
            AddRow("Pluto URI", txtPlutoUri);

            txtDeviceIndex = new TextBox();
            AddRow("Device index", txtDeviceIndex);

            comboSampleRate = new ComboBox();
            AddRow("Sample rate (Msps)", comboSampleRate);

            txtCenterMhz = new TextBox();
            AddRow("Centre (MHz)", txtCenterMhz);

            txtSweepCenter = new TextBox();
            AddRow("Sweep centre (MHz)", txtSweepCenter);

            txtSweepSpan = new TextBox();
            AddRow("Sweep span (MHz)", txtSweepSpan);

            txtSweepDwell = new TextBox();
            AddRow("Sweep dwell (ms)", txtSweepDwell);

            chkSweep = new CheckBox { Content = "Sweep & stitch (narrow receivers)" };
            AddRow("Sweep", chkSweep);

            chkFollowLnb = new CheckBox { Content = "Follow LNB power (H=WB, V=NB)" };
            AddRow("Band", chkFollowLnb);

            chkAgc = new CheckBox { Content = "Auto (AGC)" };
            AddRow("Gain", chkAgc);

            txtGainDb = new TextBox();
            AddRow("Manual gain (dB)", txtGainDb);

            txtPpm = new TextBox();
            AddRow("PPM correction", txtPpm);

            comboFft = new ComboBox();
            comboFft.Items.Add("1024");
            comboFft.Items.Add("2048");
            comboFft.Items.Add("4096");
            AddRow("FFT size", comboFft);

            txtMinDb = new TextBox();
            AddRow("Display floor (dB)", txtMinDb);

            txtMaxDb = new TextBox();
            AddRow("Display ceiling (dB)", txtMaxDb);

            txtLnbLo = new TextBox();
            AddRow("LNB LO (MHz)", txtLnbLo);

            txtBroadbandSr = new TextBox();
            AddRow("Broadband symbol rate (ksym)", txtBroadbandSr);

            txtNarrowbandSr = new TextBox();
            AddRow("Narrowband symbol rate (ksym)", txtNarrowbandSr);

            chkEstimateSr = new CheckBox { Content = "Estimate from peak width" };
            AddRow("Symbol rate from peak", chkEstimateSr);

            txtRolloff = new TextBox();
            AddRow("Rolloff", txtRolloff);

            chkAudio = new CheckBox { Content = "Enable AM/SSB listening" };
            AddRow("Narrowband audio", chkAudio);

            comboDemod = new ComboBox();
            comboDemod.Items.Add("AM");
            comboDemod.Items.Add("USB");
            comboDemod.Items.Add("LSB");
            AddRow("Demodulation", comboDemod);

            txtVolume = new TextBox();
            AddRow("Audio volume", txtVolume);

            chkAudioFilter = new CheckBox { Content = "Post-demod audio low-pass" };
            AddRow("Audio filter", chkAudioFilter);

            txtAudioFilter = new TextBox();
            AddRow("Audio filter cutoff (Hz)", txtAudioFilter);

            txtDeemph = new TextBox();
            AddRow("De-emphasis (us, 0=off)", txtDeemph);
        }

        private static double[] RatesForSource(int src)
        {
            switch (src)
            {
                case 0:
                case 1: return new double[] { 0.25, 1.024, 2.0, 2.4, 3.2 };
                case 2: return new double[] { 2.0, 4.0, 8.0, 10.0, 16.0, 20.0 };
                case 3: return new double[] { 1.0, 2.0, 4.0, 5.0, 8.0, 10.0, 15.0, 20.0 };
                default: return new double[] { 2.4 };
            }
        }

        private double CurrentRate()
        {
            if (double.TryParse(comboSampleRate.SelectedItem as string, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return v;
            return _settings.SampleRateHz / 1e6;
        }

        private void PopulateSampleRates(double want)
        {
            double[] rates = RatesForSource(comboSource.SelectedIndex);
            comboSampleRate.Items.Clear();
            foreach (double r in rates) comboSampleRate.Items.Add(r.ToString(CultureInfo.InvariantCulture));

            int best = 0; double bd = double.MaxValue;
            for (int i = 0; i < rates.Length; i++) { double d = Math.Abs(rates[i] - want); if (d < bd) { bd = d; best = i; } }
            comboSampleRate.SelectedIndex = best;
        }

        private void LoadValues()
        {
            comboSource.SelectedIndex = Math.Max(0, Math.Min(3, (int)_settings.SourceType));
            txtHost.Text = _settings.RtlTcpHost;
            txtPort.Text = _settings.RtlTcpPort.ToString();
            txtPlutoUri.Text = _settings.PlutoUri;
            txtDeviceIndex.Text = _settings.DeviceIndex.ToString();
            PopulateSampleRates(_settings.SampleRateHz / 1e6);

            txtCenterMhz.Text = (_settings.CenterFrequencyHz / 1e6).ToString("0.0000", CultureInfo.InvariantCulture);
            txtGainDb.Text = (_settings.GainDb / 10).ToString(CultureInfo.InvariantCulture);
            txtPpm.Text = _settings.PpmCorrection.ToString();
            comboFft.SelectedIndex = _settings.FftSize == 1024 ? 0 : (_settings.FftSize == 4096 ? 2 : 1);
            txtMinDb.Text = _settings.MinDb.ToString();
            txtMaxDb.Text = _settings.MaxDb.ToString();
            txtLnbLo.Text = (_settings.LnbLoKHz / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);
            txtBroadbandSr.Text = _settings.BroadbandSymbolRate.ToString();
            txtNarrowbandSr.Text = _settings.NarrowbandSymbolRate.ToString();

            chkFollowLnb.IsChecked = _settings.FollowLnbPolarization;
            chkSweep.IsChecked = _settings.SweepEnabled;
            txtSweepSpan.Text = (_settings.SweepSpanHz / 1e6).ToString("0.0", CultureInfo.InvariantCulture);
            txtSweepCenter.Text = (_settings.SweepCenterHz / 1e6).ToString("0.0000", CultureInfo.InvariantCulture);
            txtSweepDwell.Text = _settings.SweepDwellMs.ToString();

            chkAgc.IsChecked = _settings.Agc;
            chkEstimateSr.IsChecked = _settings.EstimateSymbolRate;
            txtRolloff.Text = _settings.Rolloff.ToString(CultureInfo.InvariantCulture);

            chkAudio.IsChecked = _settings.AudioEnabled;
            comboDemod.SelectedIndex = (int)_settings.DemodMode;
            txtVolume.Text = _settings.AudioVolume.ToString();
            chkAudioFilter.IsChecked = _settings.AudioFilterEnabled;
            txtAudioFilter.Text = _settings.AudioFilterHz.ToString();
            txtDeemph.Text = _settings.DeemphasisUs.ToString();
        }

        private static double ParseDouble(string s, double fallback)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
        }

        private static int ParseInt(string s, int fallback)
        {
            return int.TryParse(s, out int v) ? v : fallback;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _settings.SourceType = (SdrSourceType)comboSource.SelectedIndex;
            _settings.RtlTcpHost = txtHost.Text.Trim();
            _settings.RtlTcpPort = ParseInt(txtPort.Text, 1234);
            _settings.PlutoUri = txtPlutoUri.Text.Trim();
            _settings.DeviceIndex = ParseInt(txtDeviceIndex.Text, 0);

            double msps = CurrentRate();
            _settings.SampleRateHz = (uint)Math.Round(msps * 1e6);

            _settings.CenterFrequencyHz = (uint)Math.Round(ParseDouble(txtCenterMhz.Text, _settings.CenterFrequencyHz / 1e6) * 1e6);
            _settings.Agc = chkAgc.IsChecked == true;
            _settings.GainDb = (int)(ParseDouble(txtGainDb.Text, _settings.GainDb / 10.0) * 10);
            _settings.PpmCorrection = ParseInt(txtPpm.Text, 0);
            _settings.FftSize = comboFft.SelectedIndex == 0 ? 1024 : (comboFft.SelectedIndex == 2 ? 4096 : 2048);
            _settings.MinDb = ParseInt(txtMinDb.Text, -105);
            _settings.MaxDb = ParseInt(txtMaxDb.Text, -15);
            _settings.LnbLoKHz = (uint)Math.Round(ParseDouble(txtLnbLo.Text, 9750) * 1000);
            _settings.BroadbandSymbolRate = (uint)ParseInt(txtBroadbandSr.Text, 1500);
            _settings.NarrowbandSymbolRate = (uint)ParseInt(txtNarrowbandSr.Text, 30);

            _settings.FollowLnbPolarization = chkFollowLnb.IsChecked == true;
            _settings.SweepEnabled = chkSweep.IsChecked == true;
            _settings.SweepSpanHz = (uint)Math.Round(ParseDouble(txtSweepSpan.Text, 9) * 1e6);
            _settings.SweepCenterHz = (uint)Math.Round(ParseDouble(txtSweepCenter.Text, 741.5) * 1e6);
            _settings.SweepDwellMs = ParseInt(txtSweepDwell.Text, 250);

            _settings.EstimateSymbolRate = chkEstimateSr.IsChecked == true;
            _settings.Rolloff = ParseDouble(txtRolloff.Text, 0.35);

            _settings.AudioEnabled = chkAudio.IsChecked == true;
            _settings.DemodMode = (SdrDemodMode)comboDemod.SelectedIndex;
            _settings.AudioVolume = ParseInt(txtVolume.Text, 70);
            _settings.AudioFilterEnabled = chkAudioFilter.IsChecked == true;
            _settings.AudioFilterHz = ParseInt(txtAudioFilter.Text, 3000);
            _settings.DeemphasisUs = ParseInt(txtDeemph.Text, 0);

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
