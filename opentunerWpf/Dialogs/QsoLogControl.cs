using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using opentuner;
using opentuner.Qso;
using opentuner.Utilities;

namespace OpenTuner.Wpf.Dialogs
{
    /// <summary>
    /// QSO logger with ADIF export, hosted as a bottom tab (and usable as a
    /// dialog). Designed around satellite DATV: you can start and stop (pause)
    /// the contact while re-syncing; the recorded TIME_ON is the first start and
    /// TIME_OFF the last stop, and everything needed for a compliant ADIF record
    /// is captured (mode, frequency, band, propagation, satellite, reports,
    /// comments).
    /// </summary>
    public class QsoLogControl : UserControl
    {
        private readonly SettingsManager<List<QsoRecord>> _store = new SettingsManager<List<QsoRecord>>("qso_log");
        private readonly List<QsoRecord> _records;

        private readonly string _myCallDefault;
        private readonly string _satDefault;
        private readonly string _propDefault;
        private readonly Func<int> _rxCount;
        private readonly Func<int, (string call, double freqMhz)> _signalForRx;
        private readonly Func<int, string> _signalInfoForRx;
        private int _lastRx = 0;

        private TextBox _txtCall, _txtMyCall, _txtFreq, _txtSubmode, _txtSat, _txtRstSent, _txtRstRcvd, _txtComment;
        private ComboBox _comboMode, _comboProp;
        private TextBlock _txtTimer, _txtTimeOn, _txtTimeOff, _status;
        private ListView _list;
        private WrapPanel _rxButtons;

        private DateTime? _timeOn = null;
        private DateTime? _timeOff = null;
        private bool _running = false;
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

        public QsoLogControl(MainSettings settings, Func<int> rxCount,
                             Func<int, (string call, double freqMhz)> signalForRx,
                             Func<int, string> signalInfoForRx)
        {
            _myCallDefault = settings.station_callsign;
            _satDefault = settings.qso_satellite;
            _propDefault = settings.qso_prop_mode;
            _rxCount = rxCount;
            _signalForRx = signalForRx;
            _signalInfoForRx = signalInfoForRx;

            try { _records = _store.LoadSettings(new List<QsoRecord>()) ?? new List<QsoRecord>(); }
            catch { _records = new List<QsoRecord>(); }

            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            BuildUi();
            RefreshList();
            RefreshRxButtons();

            _timer.Tick += (s, e) => UpdateTimer();
            _timer.Start();
            Unloaded += (s, e) => _timer.Stop();
        }

        private void BuildUi()
        {
            var dock = new DockPanel();

            var buttons = new Border
            {
                Background = (Brush)Application.Current.FindResource("SurfaceBackground"),
                BorderBrush = (Brush)Application.Current.FindResource("Border"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 10, 16, 10)
            };
            DockPanel.SetDock(buttons, Dock.Bottom);
            var bs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var export = new Button { Content = "Export ADIF...", Width = 130, Margin = new Thickness(0, 0, 10, 0) };
            export.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
            export.Click += (s, e) => ExportAdif();
            var del = new Button { Content = "Delete", Width = 90 };
            del.Click += (s, e) => DeleteSelected();
            bs.Children.Add(export);
            bs.Children.Add(del);
            buttons.Child = bs;
            dock.Children.Add(buttons);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) };
            var content = new StackPanel();
            scroll.Content = content;
            dock.Children.Add(scroll);

            StackPanel edits;
            var editCard = SettingsUi.Card("Current QSO", out edits);
            content.Children.Add(editCard);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            var right = new StackPanel();
            Grid.SetColumn(left, 0);
            Grid.SetColumn(right, 1);
            grid.Children.Add(left);
            grid.Children.Add(right);

            _txtCall = SettingsUi.Text("");
            SettingsUi.Row(left, "Worked call:", _txtCall);

            _rxButtons = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            SettingsUi.Row(left, "From RX:", _rxButtons);

            _txtMyCall = SettingsUi.Text(_myCallDefault);
            SettingsUi.Row(left, "My callsign:", _txtMyCall);

            _comboMode = SettingsUi.Combo("DV", "DIG", "DATA", "SSB", "FM", "AM", "CW", "RTTY");
            _comboMode.SelectedIndex = 0;
            SettingsUi.Row(left, "Mode:", _comboMode);

            _txtSubmode = SettingsUi.Text("DATV");
            SettingsUi.Row(left, "Submode:", _txtSubmode);

            _comboProp = SettingsUi.Combo("SAT", "TR", "ES", "MS", "AU", "F2", "IONO", "AS", "EME", "");
            _comboProp.SelectedIndex = Math.Max(0, _comboProp.Items.IndexOf(_propDefault));
            SettingsUi.Row(left, "Propagation:", _comboProp);

            _txtSat = SettingsUi.Text(_satDefault);
            SettingsUi.Row(right, "Satellite:", _txtSat);

            _txtFreq = SettingsUi.Text("10489.750");
            SettingsUi.Row(right, "Frequency (MHz):", _txtFreq);

            _txtRstSent = SettingsUi.Text("59");
            SettingsUi.Row(right, "RST sent:", _txtRstSent);

            _txtRstRcvd = SettingsUi.Text("59");
            SettingsUi.Row(right, "RST received:", _txtRstRcvd);

            edits.Children.Add(grid);

            var timerRow = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            timerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            timerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var timerBtns = new StackPanel { Orientation = Orientation.Horizontal };
            var btnStart = new Button { Content = "Start / TX", Width = 100, Margin = new Thickness(0, 0, 8, 0) };
            btnStart.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
            btnStart.Click += (s, e) => StartQso();
            var btnStop = new Button { Content = "Stop / RX", Width = 100, Margin = new Thickness(0, 0, 8, 0) };
            btnStop.Click += (s, e) => StopQso();
            var reset = new Button { Content = "Reset", Width = 80 };
            reset.Click += (s, e) => ResetQso();
            timerBtns.Children.Add(btnStart);
            timerBtns.Children.Add(btnStop);
            timerBtns.Children.Add(reset);
            Grid.SetColumn(timerBtns, 0);

            _txtTimer = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), FontSize = 14 };
            Grid.SetColumn(_txtTimer, 1);

            timerRow.Children.Add(timerBtns);
            timerRow.Children.Add(_txtTimer);
            edits.Children.Add(timerRow);

            _txtTimeOn = new TextBlock { Foreground = (Brush)Application.Current.FindResource("TextSecondary"), Margin = new Thickness(0, 6, 0, 0) };
            _txtTimeOff = new TextBlock { Foreground = (Brush)Application.Current.FindResource("TextSecondary") };
            edits.Children.Add(_txtTimeOn);
            edits.Children.Add(_txtTimeOff);

            _txtComment = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 54,
                VerticalContentAlignment = VerticalAlignment.Top
            };
            SettingsUi.Row(edits, "Comment:", _txtComment);

            var saveRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var save = new Button { Content = "Save QSO", Width = 130, Margin = new Thickness(0, 0, 10, 0) };
            save.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
            save.Click += (s, e) => SaveQso();
            var addInfo = new Button { Content = "Add signal info", Width = 140, Margin = new Thickness(0, 0, 10, 0), ToolTip = "Append the tuned signal's details (RXn) to the comment" };
            addInfo.Click += (s, e) => AppendSignalInfo(_lastRx);
            var clear = new Button { Content = "Clear", Width = 90 };
            clear.Click += (s, e) => ResetQso();
            saveRow.Children.Add(save);
            saveRow.Children.Add(addInfo);
            saveRow.Children.Add(clear);
            edits.Children.Add(saveRow);

            StackPanel listBody;
            var listCard = SettingsUi.Card("Logged QSOs", out listBody);
            content.Children.Add(listCard);

            _list = new ListView { Height = 180 };
            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "Call", Width = 90, DisplayMemberBinding = new System.Windows.Data.Binding("call") });
            gv.Columns.Add(new GridViewColumn { Header = "Date", Width = 90, DisplayMemberBinding = new System.Windows.Data.Binding("date") });
            gv.Columns.Add(new GridViewColumn { Header = "On", Width = 70, DisplayMemberBinding = new System.Windows.Data.Binding("on") });
            gv.Columns.Add(new GridViewColumn { Header = "Off", Width = 70, DisplayMemberBinding = new System.Windows.Data.Binding("off") });
            gv.Columns.Add(new GridViewColumn { Header = "MHz", Width = 90, DisplayMemberBinding = new System.Windows.Data.Binding("freq") });
            gv.Columns.Add(new GridViewColumn { Header = "Band", Width = 55, DisplayMemberBinding = new System.Windows.Data.Binding("band") });
            gv.Columns.Add(new GridViewColumn { Header = "Mode", Width = 60, DisplayMemberBinding = new System.Windows.Data.Binding("mode") });
            gv.Columns.Add(new GridViewColumn { Header = "Sat", Width = 80, DisplayMemberBinding = new System.Windows.Data.Binding("sat") });
            _list.View = gv;
            listBody.Children.Add(_list);

            _status = new TextBlock { Margin = new Thickness(0, 8, 0, 0), Foreground = (Brush)Application.Current.FindResource("TextSecondary") };
            content.Children.Add(_status);

            Content = dock;
        }

        private void UseCurrentSignal(int rx)
        {
            try
            {
                var (call, freq) = _signalForRx?.Invoke(rx) ?? ("", 0);
                if (!string.IsNullOrEmpty(call)) _txtCall.Text = call;
                if (freq > 0) _txtFreq.Text = freq.ToString("0.######", CultureInfo.InvariantCulture);
            }
            catch { }
        }

        /// <summary>Builds one "RXn" button per available tuner.</summary>
        private void RefreshRxButtons()
        {
            if (_rxButtons == null)
                return;

            _rxButtons.Children.Clear();

            int count = 2;
            try { count = Math.Max(1, Math.Min(8, _rxCount != null ? _rxCount() : 2)); } catch { }
            if (count < 1) count = 1;

            for (int i = 0; i < count; i++)
            {
                int rx = i;
                var b = new Button
                {
                    Content = "RX" + (i + 1),
                    Margin = new Thickness(0, 0, 6, 0),
                    Padding = new Thickness(8, 2, 8, 2),
                    FontSize = 11,
                    ToolTip = "Fill from RX" + (i + 1)
                };
                b.Click += (s, e) => { _lastRx = rx; UseCurrentSignal(rx); };
                _rxButtons.Children.Add(b);
            }
        }

        /// <summary>Appends the tuned signal's details (from RXn) to the comment.</summary>
        private void AppendSignalInfo(int rx)
        {
            try
            {
                string info = _signalInfoForRx?.Invoke(rx);
                if (string.IsNullOrWhiteSpace(info))
                {
                    _status.Text = "No signal info available for RX" + (rx + 1) + ".";
                    return;
                }

                string existing = _txtComment.Text ?? "";
                _txtComment.Text = string.IsNullOrEmpty(existing.Trim())
                    ? info
                    : existing.TrimEnd() + "\r\n" + info;

                _txtComment.CaretIndex = _txtComment.Text.Length;
                _txtComment.ScrollToEnd();
            }
            catch { }
        }

        private void StartQso()
        {
            if (_timeOn == null)
                _timeOn = DateTime.UtcNow;
            _running = true;
            UpdateTimer();
        }

        private void StopQso()
        {
            _timeOff = DateTime.UtcNow;
            _running = false;
            UpdateTimer();
        }

        private void ResetQso()
        {
            _timeOn = null;
            _timeOff = null;
            _running = false;
            _txtCall.Text = "";
            _txtComment.Text = "";
            _txtTimeOn.Text = "TIME ON  : -";
            _txtTimeOff.Text = "TIME OFF : -";
            _txtTimer.Text = "00:00:00";
        }

        private void UpdateTimer()
        {
            if (_timeOn == null)
            {
                _txtTimer.Text = "00:00:00";
                return;
            }

            DateTime end = _running ? DateTime.UtcNow : (_timeOff ?? DateTime.UtcNow);
            TimeSpan d = end - _timeOn.Value;
            _txtTimer.Text = d.ToString(@"hh\:mm\:ss");

            _txtTimeOn.Text = "TIME ON  : " + _timeOn.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";
            _txtTimeOff.Text = "TIME OFF : " + (_timeOff.HasValue
                ? _timeOff.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC"
                : (_running ? "(in progress)" : "-"));
        }

        private void SaveQso()
        {
            try
            {
                string call = _txtCall.Text.Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(call))
                {
                    MessageBox.Show("Enter the worked callsign.", "QSO Log");
                    return;
                }

                double.TryParse(_txtFreq.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double freq);

                DateTime on = _timeOn ?? DateTime.UtcNow;
                DateTime off = _timeOff ?? DateTime.UtcNow;
                if (off < on) off = on;

                var q = new QsoRecord
                {
                    call = call,
                    my_call = _txtMyCall.Text.Trim().ToUpperInvariant(),
                    time_on_utc = on,
                    time_off_utc = off,
                    freq_mhz = freq,
                    mode = (_comboMode.SelectedItem as string) ?? "DV",
                    submode = _txtSubmode.Text.Trim(),
                    prop_mode = (_comboProp.SelectedItem as string) ?? "SAT",
                    sat_name = _txtSat.Text.Trim(),
                    rst_sent = _txtRstSent.Text.Trim(),
                    rst_rcvd = _txtRstRcvd.Text.Trim(),
                    comment = _txtComment.Text.Trim()
                };

                _records.Add(q);
                _store.SaveSettings(_records);
                RefreshList();
                ResetQso();

                _status.Text = "Saved QSO with " + call + " (" + _records.Count + " total).";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Save failed: " + ex.Message, "QSO Log");
            }
        }

        private void DeleteSelected()
        {
            if (!(_list.SelectedItem is QsoRow row))
                return;

            _records.Remove(row.Record);
            _store.SaveSettings(_records);
            RefreshList();
        }

        private sealed class QsoRow
        {
            public QsoRecord Record;
            public string call => Record.call;
            public string date => Record.time_on_utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            public string on => Record.time_on_utc.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            public string off => Record.time_off_utc.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            public string freq => Record.freq_mhz.ToString("0.######", CultureInfo.InvariantCulture);
            public string band => Record.Band;
            public string mode => Record.mode;
            public string sat => Record.sat_name;
        }

        private void RefreshList()
        {
            _list.Items.Clear();
            foreach (QsoRecord r in _records.OrderByDescending(r => r.time_on_utc))
                _list.Items.Add(new QsoRow { Record = r });
        }

        /// <summary>Re-reads the shared QSO store (so tab + window stay in sync).</summary>
        public void Reload()
        {
            try
            {
                _records.Clear();
                var loaded = _store.LoadSettings(new List<QsoRecord>());
                if (loaded != null)
                    _records.AddRange(loaded);
                RefreshList();
                RefreshRxButtons();
            }
            catch { }
        }

        private void ExportAdif()
        {
            if (_records.Count == 0)
            {
                MessageBox.Show("No QSOs to export.", "QSO Log");
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "ADIF files (*.adi)|*.adi|All files (*.*)|*.*",
                FileName = "opentuner_" + DateTime.Now.ToString("yyyyMMdd") + ".adi"
            };
            if (dlg.ShowDialog() != true)
                return;

            try
            {
                System.IO.File.WriteAllText(dlg.FileName, AdifExport.Build(_records));
                _status.Text = "Exported " + _records.Count + " QSO(s) to " + dlg.FileName;
                ToastService.Show("Exported " + _records.Count + " QSO(s) to ADIF", ToastKind.Success, 4);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export failed: " + ex.Message, "QSO Log");
            }
        }
    }
}
