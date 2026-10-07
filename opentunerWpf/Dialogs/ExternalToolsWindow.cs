using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using opentuner;

namespace OpenTuner.Wpf.Dialogs
{
    /// <summary>Native WPF external tools manager (replaces externalToolsManager/EditExternalToolForm).</summary>
    public class ExternalToolsWindow : Window
    {
        private readonly List<ExternalTool> _tools;
        private readonly StackPanel _rows = new StackPanel();

        private sealed class Row
        {
            public TextBox Name, Path, Parameters;
            public CheckBox Udp1, Udp2;
        }

        private readonly List<Row> _rowList = new List<Row>();

        public ExternalToolsWindow(List<ExternalTool> tools)
        {
            _tools = tools;

            Title = "External Tools";
            Width = 720; Height = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)Application.Current.FindResource("WindowBackground");
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            BuildUi();

            foreach (ExternalTool t in _tools)
                AddRow(t);
        }

        private static readonly double[] ColumnWidths = { 130, 220, 160, 60, 60 };
        private static readonly string[] ColumnHeaders = { "Name", "Executable path", "Arguments", "UDP 1", "UDP 2" };

        private void BuildUi()
        {
            var dock = new DockPanel();

            var header = new Border
            {
                Background = (Brush)Application.Current.FindResource("SurfaceBackground"),
                BorderBrush = (Brush)Application.Current.FindResource("Border"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 10, 16, 10)
            };
            DockPanel.SetDock(header, Dock.Top);

            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = "External tools are launched from here. \"Executable path\" is the program to run and \"Arguments\" are passed to it. " +
                       "Tick UDP 1 / UDP 2 to record that the tool consumes transport stream 1 / 2.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
                Margin = new Thickness(0, 0, 0, 8)
            });

            var hdr = new Grid();
            for (int i = 0; i < ColumnHeaders.Length; i++)
            {
                hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColumnWidths[i]) });
                var tb = new TextBlock
                {
                    Text = ColumnHeaders[i],
                    Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
                    FontWeight = FontWeights.SemiBold
                };
                Grid.SetColumn(tb, i);
                hdr.Children.Add(tb);
            }
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerStack.Children.Add(hdr);
            header.Child = headerStack;
            dock.Children.Add(header);

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
            add.Click += (s, e) => AddRow(new ExternalTool { ToolName = "New Tool" });
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

        private void AddRow(ExternalTool t)
        {
            var row = new Row();
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            for (int i = 0; i < ColumnWidths.Length; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColumnWidths[i]) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            row.Name = new TextBox { Text = t.ToolName ?? "" };
            row.Path = new TextBox { Text = t.ToolPath ?? "" };
            row.Parameters = new TextBox { Text = t.ToolParameters ?? "" };
            row.Udp1 = new CheckBox { IsChecked = t.EnableUDP1 };
            row.Udp2 = new CheckBox { IsChecked = t.EnableUDP2 };

            var name = Wrap(row.Name, "A friendly name for this tool");
            var path = Wrap(row.Path, "Full path to the executable to launch");
            var pars = Wrap(row.Parameters, "Command line arguments passed to the executable");
            var u1 = Wrap(row.Udp1, "This tool consumes transport stream 1");
            var u2 = Wrap(row.Udp2, "This tool consumes transport stream 2");

            Grid.SetColumn(name, 0); g.Children.Add(name);
            Grid.SetColumn(path, 1); g.Children.Add(path);
            Grid.SetColumn(pars, 2); g.Children.Add(pars);
            Grid.SetColumn(u1, 3); g.Children.Add(u1);
            Grid.SetColumn(u2, 4); g.Children.Add(u2);

            var launch = new Button { Content = "Launch", Width = 70, Margin = new Thickness(8, 0, 8, 0) };
            launch.Click += (s, e) => Launch(row);
            Grid.SetColumn(launch, 5);
            g.Children.Add(launch);

            var container = new Border { Child = g };
            _rows.Children.Add(container);
            _rowList.Add(row);
        }

        private static FrameworkElement Wrap(FrameworkElement c, string tip)
        {
            c.Margin = new Thickness(0, 0, 6, 0);
            c.ToolTip = tip;
            return c;
        }

        private void Launch(Row r)
        {
            try
            {
                string path = r.Path.Text.Trim();
                if (path.Length == 0) return;
                System.Diagnostics.Process.Start(path, r.Parameters.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Launch failed: " + ex.Message, "Open Tuner");
            }
        }

        private void Commit()
        {
            _tools.Clear();
            foreach (Row r in _rowList)
            {
                _tools.Add(new ExternalTool
                {
                    ToolName = r.Name.Text,
                    ToolPath = r.Path.Text,
                    ToolParameters = r.Parameters.Text,
                    EnableUDP1 = r.Udp1.IsChecked == true,
                    EnableUDP2 = r.Udp2.IsChecked == true
                });
            }
        }
    }
}
