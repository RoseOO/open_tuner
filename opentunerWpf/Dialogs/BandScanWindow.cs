using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using opentuner.MediaSources;

namespace OpenTuner.Wpf.Dialogs
{
    /// <summary>
    /// Steps a tuner across a frequency range and logs every locked signal.
    /// Results can be exported to CSV or copied to the clipboard.
    /// </summary>
    public class BandScanWindow : Window
    {
        private readonly OTSource _source;

        private TextBox _start, _stop, _step, _sr, _dwell;
        private TextBlock _status;
        private ListView _results;
        private Button _startBtn, _stopBtn, _exportBtn;

        private CancellationTokenSource _cts;
        private volatile OTSourceData _current;
        private readonly List<ScanRow> _rows = new List<ScanRow>();

        private sealed class ScanRow
        {
            public uint FreqKhz;
            public bool Locked;
            public string Service;
            public double Mer;
            public double Margin;
            public uint Sr;
        }

        public BandScanWindow(OTSource source, OTSourceData latest)
        {
            _source = source;

            Title = LocalizationManager.Get("dt.bandscan");
            Width = 760; Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)Application.Current.FindResource("WindowBackground");
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            var dock = new DockPanel();

            // controls
            var top = new Border
            {
                Background = (Brush)Application.Current.FindResource("SurfaceBackground"),
                BorderBrush = (Brush)Application.Current.FindResource("Border"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 10, 16, 10)
            };
            DockPanel.SetDock(top, Dock.Top);
            var grid = new Grid();
            for (int i = 0; i < 6; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });

            _start = AddField(grid, 0, "Start (MHz)", "10490.0");
            _stop = AddField(grid, 1, "Stop (MHz)", "10500.0");
            _step = AddField(grid, 2, "Step (kHz)", "500");
            _sr = AddField(grid, 3, "Symbol rate (k)", "1000");
            _dwell = AddField(grid, 4, "Dwell (ms)", "1500");
            top.Child = grid;
            dock.Children.Add(top);

            // bottom buttons
            var bottom = new Border
            {
                Background = (Brush)Application.Current.FindResource("SurfaceBackground"),
                BorderBrush = (Brush)Application.Current.FindResource("Border"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 10, 16, 10)
            };
            DockPanel.SetDock(bottom, Dock.Bottom);
            var bsp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _startBtn = new Button { Content = LocalizationManager.Get("btn.start"), Width = 96, Margin = new Thickness(0, 0, 10, 0) };
            _startBtn.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
            _startBtn.Click += (s, e) => StartScan();
            _stopBtn = new Button { Content = LocalizationManager.Get("btn.stop"), Width = 96, Margin = new Thickness(0, 0, 10, 0), IsEnabled = false };
            _stopBtn.Click += (s, e) => StopScan();
            _exportBtn = new Button { Content = "Export CSV", Width = 110, Margin = new Thickness(0, 0, 10, 0) };
            _exportBtn.Click += (s, e) => ExportCsv();
            var close = new Button { Content = LocalizationManager.Get("btn.close"), Width = 96 };
            close.Click += (s, e) => { StopScan(); Close(); };
            bsp.Children.Add(_startBtn);
            bsp.Children.Add(_stopBtn);
            bsp.Children.Add(_exportBtn);
            bsp.Children.Add(close);
            bottom.Child = bsp;
            dock.Children.Add(bottom);

            _status = new TextBlock { Padding = new Thickness(16, 6, 16, 6), Foreground = (Brush)Application.Current.FindResource("TextSecondary") };
            DockPanel.SetDock(_status, Dock.Bottom);
            dock.Children.Add(_status);

            _results = new ListView { Margin = new Thickness(12) };
            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "Freq (MHz)", Width = 120, DisplayMemberBinding = new System.Windows.Data.Binding("FreqDisplay") });
            gv.Columns.Add(new GridViewColumn { Header = "Locked", Width = 80, DisplayMemberBinding = new System.Windows.Data.Binding("LockedDisplay") });
            gv.Columns.Add(new GridViewColumn { Header = "Service", Width = 220, DisplayMemberBinding = new System.Windows.Data.Binding("Service") });
            gv.Columns.Add(new GridViewColumn { Header = "MER", Width = 70, DisplayMemberBinding = new System.Windows.Data.Binding("MerDisplay") });
            gv.Columns.Add(new GridViewColumn { Header = "Margin", Width = 70, DisplayMemberBinding = new System.Windows.Data.Binding("MarginDisplay") });
            gv.Columns.Add(new GridViewColumn { Header = "SR", Width = 70, DisplayMemberBinding = new System.Windows.Data.Binding("SrDisplay") });
            _results.View = gv;
            dock.Children.Add(_results);

            Content = dock;

            Loaded += (s, e) => _source.OnSourceData += OnSourceData;
            Closed += (s, e) => { try { _source.OnSourceData -= OnSourceData; } catch { } StopScan(); };
        }

        private TextBox AddField(Grid grid, int col, string label, string value)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            sp.Children.Add(new TextBlock { Text = label, Foreground = (Brush)Application.Current.FindResource("TextSecondary"), FontSize = 11 });
            var tb = new TextBox { Text = value, Width = 100 };
            sp.Children.Add(tb);
            Grid.SetColumn(sp, col * 2);
            grid.Children.Add(sp);
            return tb;
        }

        private void OnSourceData(int videoNr, OTSourceData data, string description)
        {
            if (videoNr == 1 || videoNr == 0)
                _current = data;
        }

        private void StartScan()
        {
            if (_source == null)
            {
                MessageBox.Show("Connect to a source first.", "Open Tuner");
                return;
            }

            if (!double.TryParse(_start.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double start) ||
                !double.TryParse(_stop.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double stop) ||
                !int.TryParse(_step.Text, out int stepKhz) ||
                !uint.TryParse(_sr.Text, out uint sr) ||
                !int.TryParse(_dwell.Text, out int dwellMs))
            {
                MessageBox.Show("Invalid scan parameters.", "Open Tuner");
                return;
            }

            if (stepKhz <= 0 || stop < start || dwellMs < 100)
            {
                MessageBox.Show("Check the scan range/step/dwell.", "Open Tuner");
                return;
            }

            _rows.Clear();
            _results.Items.Clear();
            _cts = new CancellationTokenSource();
            _startBtn.IsEnabled = false;
            _stopBtn.IsEnabled = true;

            uint startKhz = (uint)Math.Round(start * 1000.0);
            uint stopKhz = (uint)Math.Round(stop * 1000.0);

            Task.Run(() => ScanLoop(startKhz, stopKhz, (uint)stepKhz, sr, dwellMs, _cts.Token));
        }

        private void ScanLoop(uint startKhz, uint stopKhz, uint stepKhz, uint sr, int dwellMs, CancellationToken token)
        {
            int total = (int)((stopKhz - startKhz) / stepKhz) + 1;
            int index = 0;

            for (uint freq = startKhz; freq <= stopKhz; freq += stepKhz)
            {
                if (token.IsCancellationRequested)
                    break;

                index++;
                int capturedIndex = index;

                try { _current = null; } catch { }
                try { _source.SetFrequency(0, freq, sr, true); } catch { }

                Dispatcher.Invoke(() => _status.Text = "Scanning " + (freq / 1000.0).ToString("0.####") + " MHz  (" + capturedIndex + "/" + total + ")");

                // wait for the dwell period, checking for cancellation
                for (int t = 0; t < dwellMs; t += 100)
                {
                    if (token.IsCancellationRequested)
                        break;
                    Thread.Sleep(100);
                }

                var d = _current;
                var row = new ScanRow
                {
                    FreqKhz = freq,
                    Locked = d != null && d.demod_locked,
                    Service = d?.service_name ?? "",
                    Mer = d?.mer ?? 0,
                    Margin = d?.db_margin ?? 0,
                    Sr = d != null ? (uint)d.symbol_rate : sr
                };

                if (row.Locked)
                {
                    _rows.Add(row);
                    Dispatcher.Invoke(() => _results.Items.Add(new
                    {
                        FreqDisplay = (freq / 1000.0).ToString("0.####"),
                        LockedDisplay = "Yes",
                        row.Service,
                        MerDisplay = row.Mer.ToString("0.0"),
                        MarginDisplay = row.Margin.ToString("0.0"),
                        SrDisplay = row.Sr.ToString()
                    }));
                }
            }

            Dispatcher.Invoke(() =>
            {
                _status.Text = token.IsCancellationRequested
                    ? "Scan stopped. " + _rows.Count + " signal(s) found."
                    : "Scan complete. " + _rows.Count + " signal(s) found.";
                _startBtn.IsEnabled = true;
                _stopBtn.IsEnabled = false;
            });
        }

        private void StopScan()
        {
            try { _cts?.Cancel(); } catch { }
            _cts = null;
        }

        private void ExportCsv()
        {
            if (_rows.Count == 0)
            {
                MessageBox.Show("Nothing to export.", "Open Tuner");
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = "bandscan_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".csv"
            };

            if (dlg.ShowDialog() != true)
                return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Frequency_kHz,Frequency_MHz,Locked,Service,MER,Margin,SymbolRate");
                foreach (var r in _rows)
                {
                    sb.AppendLine(string.Join(",",
                        r.FreqKhz.ToString(CultureInfo.InvariantCulture),
                        (r.FreqKhz / 1000.0).ToString("F4", CultureInfo.InvariantCulture),
                        "Yes",
                        Csv(r.Service),
                        r.Mer.ToString("F1", CultureInfo.InvariantCulture),
                        r.Margin.ToString("F1", CultureInfo.InvariantCulture),
                        r.Sr.ToString()));
                }

                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                ToastService.Show("Exported " + _rows.Count + " row(s)", ToastKind.Success, 3);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export failed: " + ex.Message, "Open Tuner");
            }
        }

        private static string Csv(string value)
        {
            value = value ?? "";
            if (value.IndexOfAny(new[] { ',', '"', '\n' }) < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
