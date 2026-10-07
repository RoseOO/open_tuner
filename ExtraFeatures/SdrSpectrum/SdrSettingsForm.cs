using System;
using System.Drawing;
using System.Windows.Forms;
using opentuner.Utilities;

namespace opentuner.ExtraFeatures.SdrSpectrum
{
    public class SdrSettingsForm : Form
    {
        private readonly SdrSettings _settings;

        private ComboBox comboSource;
        private TextBox txtHost;
        private NumericUpDown numPort;
        private TextBox txtPlutoUri;
        private NumericUpDown numDeviceIndex;
        private ComboBox comboSampleRate;
        private ComboBox comboCenter;
        private NumericUpDown numCenterMhz;
        private CheckBox checkFollowLnb;
        private CheckBox checkAgc;
        private NumericUpDown numGain;
        private NumericUpDown numPpm;
        private ComboBox comboFft;
        private NumericUpDown numMinDb;
        private NumericUpDown numMaxDb;
        private NumericUpDown numLnbLo;
        private NumericUpDown numBroadbandSr;
        private NumericUpDown numNarrowbandSr;
        private CheckBox checkEstimateSr;
        private NumericUpDown numRolloff;
        private CheckBox checkSweep;
        private NumericUpDown numSweepSpanMhz;
        private NumericUpDown numSweepCenterMhz;
        private NumericUpDown numSweepDwell;

        private CheckBox checkAudio;
        private ComboBox comboDemod;
        private NumericUpDown numVolume;

        public SdrSettingsForm(SdrSettings settings)
        {
            _settings = settings;

            Text = "SDR Spectrum Settings";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            MinimumSize = new Size(470, 360);
            ClientSize = new Size(450, 560);

            BuildUi();
            LoadValues();

            Theme.Apply(this);
        }

        private Label AddLabel(TableLayoutPanel t, string text, int row)
        {
            Label l = new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 0) };
            t.Controls.Add(l, 0, row);
            return l;
        }

        private void BuildUi()
        {
            TableLayoutPanel t = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                AutoSize = true,
                Padding = new Padding(10)
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));

            int r = 0;

            AddLabel(t, "SDR source", r);
            comboSource = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
            comboSource.Items.AddRange(new object[] { "RTL-TCP (network/local)", "RTL-SDR (local USB)", "HackRF (local USB)", "PlutoSDR/Pluto+ (USB or network)" });
            comboSource.SelectedIndexChanged += (s, e) => { UpdateEnableState(); PopulateSampleRates(CurrentRateMsps()); };
            t.Controls.Add(comboSource, 1, r++);

            AddLabel(t, "rtl_tcp host", r);
            txtHost = new TextBox { Width = 210 };
            t.Controls.Add(txtHost, 1, r++);

            AddLabel(t, "rtl_tcp port", r);
            numPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Width = 210 };
            t.Controls.Add(numPort, 1, r++);

            AddLabel(t, "Pluto URI", r);
            txtPlutoUri = new TextBox { Width = 210 };
            t.Controls.Add(txtPlutoUri, 1, r++);

            AddLabel(t, "Device index (local)", r);
            numDeviceIndex = new NumericUpDown { Minimum = 0, Maximum = 8, Width = 210 };
            t.Controls.Add(numDeviceIndex, 1, r++);

            AddLabel(t, "Sample rate (Msps)", r);
            comboSampleRate = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
            t.Controls.Add(comboSampleRate, 1, r++);

            AddLabel(t, "Centre frequency", r);
            comboCenter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
            comboCenter.Items.AddRange(new object[] { "741.5 MHz (WB horizontal)", "749.25 MHz", "739.5 MHz (NB vertical)", "Custom" });
            comboCenter.SelectedIndexChanged += (s, e) => OnCenterPreset();
            t.Controls.Add(comboCenter, 1, r++);

            AddLabel(t, "Centre (MHz)", r);
            numCenterMhz = new NumericUpDown { Minimum = 100, Maximum = 2000, DecimalPlaces = 4, Increment = 0.025M, Width = 210 };
            t.Controls.Add(numCenterMhz, 1, r++);

            AddLabel(t, "Band", r);
            checkFollowLnb = new CheckBox { Text = "Follow LNB power (H=WB 741.5, V=NB 739.5)", AutoSize = true };
            t.Controls.Add(checkFollowLnb, 1, r++);

            AddLabel(t, "Sweep", r);
            checkSweep = new CheckBox { Text = "Sweep & stitch (for narrow receivers)", AutoSize = true };
            t.Controls.Add(checkSweep, 1, r++);

            AddLabel(t, "Sweep span (MHz)", r);
            numSweepSpanMhz = new NumericUpDown { Minimum = 1, Maximum = 500, DecimalPlaces = 1, Increment = 1, Value = 9, Width = 210 };
            t.Controls.Add(numSweepSpanMhz, 1, r++);

            AddLabel(t, "Sweep centre (MHz)", r);
            numSweepCenterMhz = new NumericUpDown { Minimum = 100, Maximum = 2000, DecimalPlaces = 4, Increment = 0.025M, Width = 210 };
            t.Controls.Add(numSweepCenterMhz, 1, r++);

            AddLabel(t, "Sweep dwell (ms)", r);
            numSweepDwell = new NumericUpDown { Minimum = 50, Maximum = 2000, Increment = 50, Width = 210 };
            t.Controls.Add(numSweepDwell, 1, r++);

            AddLabel(t, "Gain", r);
            CheckBox agc = new CheckBox { Text = "Auto (AGC)", AutoSize = true };
            checkAgc = agc;
            checkAgc.CheckedChanged += (s, e) => UpdateEnableState();
            t.Controls.Add(checkAgc, 1, r++);

            AddLabel(t, "Manual gain (dB)", r);
            numGain = new NumericUpDown { Minimum = 0, Maximum = 50, Width = 210 };
            t.Controls.Add(numGain, 1, r++);

            AddLabel(t, "PPM correction", r);
            numPpm = new NumericUpDown { Minimum = -200, Maximum = 200, Width = 210 };
            t.Controls.Add(numPpm, 1, r++);

            AddLabel(t, "FFT size", r);
            comboFft = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
            comboFft.Items.AddRange(new object[] { "1024", "2048", "4096" });
            t.Controls.Add(comboFft, 1, r++);

            AddLabel(t, "Display floor (dB)", r);
            numMinDb = new NumericUpDown { Minimum = -160, Maximum = 0, Width = 210 };
            t.Controls.Add(numMinDb, 1, r++);

            AddLabel(t, "Display ceiling (dB)", r);
            numMaxDb = new NumericUpDown { Minimum = -80, Maximum = 40, Width = 210 };
            t.Controls.Add(numMaxDb, 1, r++);

            AddLabel(t, "LNB LO (MHz)", r);
            numLnbLo = new NumericUpDown { Minimum = 1000, Maximum = 12000, DecimalPlaces = 3, Increment = 1, Width = 210 };
            t.Controls.Add(numLnbLo, 1, r++);

            AddLabel(t, "Broadband symbol rate (ksym)", r);
            numBroadbandSr = new NumericUpDown { Minimum = 1, Maximum = 100000, Width = 210 };
            t.Controls.Add(numBroadbandSr, 1, r++);

            AddLabel(t, "Narrowband symbol rate (ksym)", r);
            numNarrowbandSr = new NumericUpDown { Minimum = 1, Maximum = 100000, Width = 210 };
            t.Controls.Add(numNarrowbandSr, 1, r++);

            AddLabel(t, "Symbol rate from peak", r);
            checkEstimateSr = new CheckBox { Text = "Estimate from peak width", AutoSize = true };
            t.Controls.Add(checkEstimateSr, 1, r++);

            AddLabel(t, "Rolloff", r);
            numRolloff = new NumericUpDown { Minimum = 0, Maximum = 0.5M, DecimalPlaces = 2, Increment = 0.05M, Width = 210 };
            t.Controls.Add(numRolloff, 1, r++);

            AddLabel(t, "Narrowband audio", r);
            checkAudio = new CheckBox { Text = "Enable AM/SSB listening", AutoSize = true };
            checkAudio.CheckedChanged += (s, e) => UpdateEnableState();
            t.Controls.Add(checkAudio, 1, r++);

            AddLabel(t, "Demodulation", r);
            comboDemod = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
            comboDemod.Items.AddRange(new object[] { "AM", "USB", "LSB" });
            t.Controls.Add(comboDemod, 1, r++);

            AddLabel(t, "Audio volume", r);
            numVolume = new NumericUpDown { Minimum = 0, Maximum = 100, Width = 210 };
            t.Controls.Add(numVolume, 1, r++);

            // Root layout: scrolling content on top, fixed button bar at the bottom.
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

            Panel content = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            content.Controls.Add(t);

            Panel buttons = new Panel { Dock = DockStyle.Fill, Height = 48 };
            Button btnOk = new Button { Text = "Save", Width = 90, Height = 28 };
            Button btnCancel = new Button { Text = "Cancel", Width = 90, Height = 28 };
            btnOk.Click += BtnOk_Click;
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            buttons.Controls.Add(btnOk);
            buttons.Controls.Add(btnCancel);
            btnOk.Location = new Point(ClientSize.Width - 210, 10);
            btnCancel.Location = new Point(ClientSize.Width - 110, 10);
            buttons.Resize += (s, e) =>
            {
                btnOk.Location = new Point(buttons.Width - 200, 10);
                btnCancel.Location = new Point(buttons.Width - 100, 10);
            };

            root.Controls.Add(content, 0, 0);
            root.Controls.Add(buttons, 0, 1);
            Controls.Add(root);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private static double[] RatesForSource(int src)
        {
            switch (src)
            {
                case 0: // RTL-TCP
                case 1: // RTL-SDR (local)
                    return new double[] { 0.25, 1.024, 2.0, 2.4, 3.2 };
                case 2: // HackRF
                    return new double[] { 2.0, 4.0, 8.0, 10.0, 16.0, 20.0 };
                case 3: // PlutoSDR / Pluto+
                    return new double[] { 1.0, 2.0, 4.0, 5.0, 8.0, 10.0, 15.0, 20.0 };
                default:
                    return new double[] { 2.4 };
            }
        }

        private double CurrentRateMsps()
        {
            if (comboSampleRate.SelectedItem != null &&
                double.TryParse((string)comboSampleRate.SelectedItem,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double v))
                return v;

            return _settings.SampleRateHz / 1e6;
        }

        private void PopulateSampleRates(double requestedMsps)
        {
            double[] rates = RatesForSource(comboSource.SelectedIndex);

            comboSampleRate.Items.Clear();
            foreach (double r in rates)
                comboSampleRate.Items.Add(r.ToString(System.Globalization.CultureInfo.InvariantCulture));

            // pick the closest available rate
            int best = 0;
            double bestDelta = double.MaxValue;
            for (int i = 0; i < rates.Length; i++)
            {
                double d = Math.Abs(rates[i] - requestedMsps);
                if (d < bestDelta) { bestDelta = d; best = i; }
            }

            comboSampleRate.SelectedIndex = best;
        }

        private void OnCenterPreset()
        {
            switch (comboCenter.SelectedIndex)
            {
                case 0: numCenterMhz.Value = 741.5M; break;
                case 1: numCenterMhz.Value = 749.25M; break;
                case 2: numCenterMhz.Value = 739.5M; break;
            }
        }

        private void LoadValues()
        {
            comboSource.SelectedIndex = (int)_settings.SourceType;
            txtHost.Text = _settings.RtlTcpHost;
            numPort.Value = _settings.RtlTcpPort;
            txtPlutoUri.Text = _settings.PlutoUri;
            numDeviceIndex.Value = _settings.DeviceIndex;

            PopulateSampleRates(_settings.SampleRateHz / 1e6);

            comboCenter.SelectedIndex = 3; // custom
            numCenterMhz.Value = Math.Min(numCenterMhz.Maximum, Math.Max(numCenterMhz.Minimum, (decimal)_settings.CenterFrequencyHz / 1000000M));
            checkFollowLnb.Checked = _settings.FollowLnbPolarization;

            checkAgc.Checked = _settings.Agc;
            numGain.Value = Math.Min(numGain.Maximum, Math.Max(numGain.Minimum, _settings.GainDb / 10));
            numPpm.Value = Math.Max(numPpm.Minimum, Math.Min(numPpm.Maximum, _settings.PpmCorrection));

            comboFft.SelectedIndex = 1;
            for (int i = 0; i < comboFft.Items.Count; i++)
                if (int.Parse((string)comboFft.Items[i]) == _settings.FftSize)
                    comboFft.SelectedIndex = i;

            numMinDb.Value = Math.Max(numMinDb.Minimum, Math.Min(numMinDb.Maximum, _settings.MinDb));
            numMaxDb.Value = Math.Max(numMaxDb.Minimum, Math.Min(numMaxDb.Maximum, _settings.MaxDb));

            numLnbLo.Value = Math.Max(numLnbLo.Minimum, Math.Min(numLnbLo.Maximum, _settings.LnbLoKHz / 1000M));
            numBroadbandSr.Value = Math.Max(numBroadbandSr.Minimum, Math.Min(numBroadbandSr.Maximum, _settings.BroadbandSymbolRate));
            numNarrowbandSr.Value = Math.Max(numNarrowbandSr.Minimum, Math.Min(numNarrowbandSr.Maximum, _settings.NarrowbandSymbolRate));

            checkEstimateSr.Checked = _settings.EstimateSymbolRate;
            numRolloff.Value = Math.Max(numRolloff.Minimum, Math.Min(numRolloff.Maximum, (decimal)_settings.Rolloff));

            checkSweep.Checked = _settings.SweepEnabled;
            numSweepSpanMhz.Value = Math.Max(numSweepSpanMhz.Minimum, Math.Min(numSweepSpanMhz.Maximum, (decimal)_settings.SweepSpanHz / 1000000M));
            numSweepCenterMhz.Value = Math.Max(numSweepCenterMhz.Minimum, Math.Min(numSweepCenterMhz.Maximum, (decimal)_settings.SweepCenterHz / 1000000M));
            numSweepDwell.Value = Math.Max(numSweepDwell.Minimum, Math.Min(numSweepDwell.Maximum, _settings.SweepDwellMs));

            checkAudio.Checked = _settings.AudioEnabled;
            comboDemod.SelectedIndex = (int)_settings.DemodMode;
            numVolume.Value = Math.Max(numVolume.Minimum, Math.Min(numVolume.Maximum, _settings.AudioVolume));

            UpdateEnableState();
        }

        private void UpdateEnableState()
        {
            int src = comboSource.SelectedIndex;
            bool rtlTcp = src == 0;
            bool pluto = src == 3;

            txtHost.Enabled = rtlTcp;
            numPort.Enabled = rtlTcp;
            txtPlutoUri.Enabled = pluto;
            numDeviceIndex.Enabled = src == 1 || src == 2;
            numGain.Enabled = !checkAgc.Checked;
            comboDemod.Enabled = checkAudio.Checked;
            numVolume.Enabled = checkAudio.Checked;
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            _settings.SourceType = (SdrSourceType)comboSource.SelectedIndex;
            _settings.RtlTcpHost = txtHost.Text.Trim();
            _settings.RtlTcpPort = (int)numPort.Value;
            _settings.PlutoUri = txtPlutoUri.Text.Trim();
            _settings.DeviceIndex = (int)numDeviceIndex.Value;

            double msps = double.Parse((string)comboSampleRate.SelectedItem,
                                       System.Globalization.CultureInfo.InvariantCulture);
            _settings.SampleRateHz = (uint)Math.Round(msps * 1e6);

            _settings.SweepEnabled = checkSweep.Checked;
            _settings.SweepSpanHz = (uint)Math.Round((double)numSweepSpanMhz.Value * 1e6);
            _settings.SweepCenterHz = (uint)Math.Round((double)numSweepCenterMhz.Value * 1e6);
            _settings.SweepDwellMs = (int)numSweepDwell.Value;

            _settings.CenterFrequencyHz = (uint)Math.Round((double)numCenterMhz.Value * 1e6);
            _settings.FollowLnbPolarization = checkFollowLnb.Checked;
            _settings.Agc = checkAgc.Checked;
            _settings.GainDb = (int)numGain.Value * 10;
            _settings.PpmCorrection = (int)numPpm.Value;

            _settings.FftSize = int.Parse((string)comboFft.SelectedItem);
            _settings.MinDb = (int)numMinDb.Value;
            _settings.MaxDb = (int)numMaxDb.Value;

            _settings.LnbLoKHz = (uint)Math.Round((double)numLnbLo.Value * 1000);
            _settings.BroadbandSymbolRate = (uint)numBroadbandSr.Value;
            _settings.NarrowbandSymbolRate = (uint)numNarrowbandSr.Value;
            _settings.EstimateSymbolRate = checkEstimateSr.Checked;
            _settings.Rolloff = (double)numRolloff.Value;

            _settings.AudioEnabled = checkAudio.Checked;
            _settings.DemodMode = (SdrDemodMode)comboDemod.SelectedIndex;
            _settings.AudioVolume = (int)numVolume.Value;

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
