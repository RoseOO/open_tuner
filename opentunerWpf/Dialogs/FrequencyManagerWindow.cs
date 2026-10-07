using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using opentuner;

namespace OpenTuner.Wpf.Dialogs
{
    /// <summary>Native WPF frequency-preset manager (replaces frequencyManagerForm).</summary>
    public class FrequencyManagerWindow : Window
    {
        private readonly List<StoredFrequency> _presets;
        private readonly StackPanel _rows = new StackPanel();

        private sealed class Row
        {
            public TextBox Name;
            public TextBox Frequency;
            public TextBox Offset;
            public TextBox SymbolRate;
            public TextBox RFInput;
            public Border Container;
        }

        private readonly List<Row> _rowList = new List<Row>();

        public FrequencyManagerWindow(List<StoredFrequency> presets)
        {
            _presets = presets;

            Title = "Frequency Presets";
            Width = 640; Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)Application.Current.FindResource("WindowBackground");
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            BuildUi();

            foreach (StoredFrequency p in _presets)
                AddRow(p);
        }

        private void BuildUi()
        {
            var dock = new DockPanel();

            // header row
            var header = new Border
            {
                Background = (Brush)Application.Current.FindResource("SurfaceBackground"),
                BorderBrush = (Brush)Application.Current.FindResource("Border"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 8, 16, 8)
            };
            DockPanel.SetDock(header, Dock.Top);

            var hdr = new Grid();
            string[] cols = { "Name", "Frequency (kHz)", "Offset", "Symbol rate", "RF input", "" };
            double[] widths = { 140, 120, 90, 100, 70, 40 };
            for (int i = 0; i < cols.Length; i++)
            {
                hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[i]) });
                var tb = new TextBlock { Text = cols[i], Foreground = (Brush)Application.Current.FindResource("TextSecondary"), FontWeight = FontWeights.SemiBold };
                Grid.SetColumn(tb, i);
                hdr.Children.Add(tb);
            }
            header.Child = hdr;
            dock.Children.Add(header);

            // buttons
            var buttons = new Border
            {
                Background = (Brush)Application.Current.FindResource("SurfaceBackground"),
                BorderBrush = (Brush)Application.Current.FindResource("Border"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 10, 16, 10)
            };
            DockPanel.SetDock(buttons, Dock.Bottom);

            var bp = new StackPanel { Orientation = Orientation.Horizontal };
            var add = new Button { Content = "Add", Width = 90 };
            add.Click += (s, e) => AddRow(new StoredFrequency { Name = "New", Frequency = 10491500, Offset = 9750000, SymbolRate = 1500 });
            var save = new Button { Content = "Save", Width = 90, Margin = new Thickness(10, 0, 0, 0) };
            save.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
            save.Click += (s, e) => { Commit(); DialogResult = true; Close(); };
            var cancel = new Button { Content = "Cancel", Width = 90, Margin = new Thickness(10, 0, 0, 0) };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            bp.Children.Add(add);
            bp.Children.Add(save);
            bp.Children.Add(cancel);
            buttons.Child = bp;
            dock.Children.Add(buttons);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) };
            scroll.Content = _rows;
            dock.Children.Add(scroll);

            Content = dock;
        }

        private void AddRow(StoredFrequency p)
        {
            var row = new Row();
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            double[] widths = { 140, 120, 90, 100, 70, 40 };
            for (int i = 0; i < widths.Length; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[i]) });

            row.Name = new TextBox { Text = p.Name ?? "" };
            row.Frequency = new TextBox { Text = p.Frequency.ToString() };
            row.Offset = new TextBox { Text = p.Offset.ToString() };
            row.SymbolRate = new TextBox { Text = p.SymbolRate.ToString() };
            row.RFInput = new TextBox { Text = p.RFInput.ToString() };

            TextBox[] boxes = { row.Name, row.Frequency, row.Offset, row.SymbolRate, row.RFInput };
            for (int i = 0; i < boxes.Length; i++)
            {
                boxes[i].Margin = new Thickness(0, 0, 6, 0);
                Grid.SetColumn(boxes[i], i);
                g.Children.Add(boxes[i]);
            }

            var del = new Button { Content = "X", Width = 30 };
            Grid.SetColumn(del, 5);
            g.Children.Add(del);

            var container = new Border { Child = g };
            _rows.Children.Add(container);

            row.Container = container;
            _rowList.Add(row);

            del.Click += (s, e) => { _rows.Children.Remove(container); _rowList.Remove(row); };
        }

        private void Commit()
        {
            _presets.Clear();

            foreach (Row r in _rowList)
            {
                var p = new StoredFrequency { Name = r.Name.Text };
                if (uint.TryParse(r.Frequency.Text, out uint f)) p.Frequency = f;
                if (uint.TryParse(r.Offset.Text, out uint o)) p.Offset = o;
                if (uint.TryParse(r.SymbolRate.Text, out uint sr)) p.SymbolRate = sr;
                if (byte.TryParse(r.RFInput.Text, out byte rf)) p.RFInput = rf;
                _presets.Add(p);
            }
        }
    }
}
