using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenTuner.Wpf.Dialogs
{
    /// <summary>
    /// Generic WPF editor for a plain settings object. Reflects over the public
    /// instance fields (scalars, strings and primitive arrays) so the many
    /// source/feature settings classes all get a modern editor without a
    /// bespoke window each.
    /// </summary>
    public class SettingsEditorWindow : Window
    {
        private readonly object _target;
        private readonly List<Action> _commits = new List<Action>();
        private readonly StackPanel _content = new StackPanel();
        private readonly Dictionary<string, string> _friendly = new Dictionary<string, string>();

        public SettingsEditorWindow(object target, string title, Dictionary<string, string> friendlyNames = null)
        {
            _target = target;

            Title = string.IsNullOrEmpty(title) ? "Settings" : title;
            Width = 520; Height = 620; MinWidth = 440; MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)Application.Current.FindResource("WindowBackground");
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            if (friendlyNames != null)
                _friendly = friendlyNames;

            BuildUi();
            BuildFields();
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

            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "Cancel", Width = 96, Margin = new Thickness(0, 0, 10, 0) };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            var save = new Button { Content = "Save", Width = 96 };
            save.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
            save.Click += (s, e) => { foreach (var c in _commits) c(); DialogResult = true; Close(); };
            sp.Children.Add(cancel);
            sp.Children.Add(save);
            buttons.Child = sp;

            dock.Children.Add(buttons);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) };
            scroll.Content = _content;
            dock.Children.Add(scroll);

            Content = dock;
        }

        private static bool IsNumeric(Type t)
        {
            return t == typeof(byte) || t == typeof(sbyte) || t == typeof(short) || t == typeof(ushort) ||
                   t == typeof(int) || t == typeof(uint) || t == typeof(long) || t == typeof(ulong) ||
                   t == typeof(float) || t == typeof(double) || t == typeof(decimal);
        }

        private string Label(FieldInfo f)
        {
            return _friendly.TryGetValue(f.Name, out string v) ? v : SplitCamel(f.Name);
        }

        private static string SplitCamel(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new System.Text.StringBuilder();
            sb.Append(char.ToUpper(s[0]));
            for (int i = 1; i < s.Length; i++)
            {
                if (char.IsUpper(s[i]) && !char.IsUpper(s[i - 1]))
                    sb.Append(' ');
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        private void BuildFields()
        {
            foreach (FieldInfo f in _target.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.IsInitOnly || f.IsLiteral)
                    continue;

                Type t = f.FieldType;
                object val = f.GetValue(_target);

                if (t == typeof(bool))
                {
                    var cb = new CheckBox { Content = "", IsChecked = val is bool b && b };
                    _commits.Add(() => f.SetValue(_target, cb.IsChecked == true));
                    AddRow(Label(f), cb);
                }
                else if (t == typeof(string))
                {
                    var tb = new TextBox { Text = val as string ?? "" };
                    _commits.Add(() => f.SetValue(_target, tb.Text));
                    AddRow(Label(f), tb);
                }
                else if (t.IsArray && t.GetElementType() != null && (t.GetElementType().IsPrimitive || t.GetElementType() == typeof(string)))
                {
                    var arr = (Array)val;
                    string joined = JoinArray(arr);
                    var tb = new TextBox { Text = joined };
                    _commits.Add(() => f.SetValue(_target, ParseArray(t, tb.Text, arr)));
                    AddRow(Label(f), tb);
                }
                else if (IsNumeric(t))
                {
                    var tb = new TextBox { Text = Convert.ToString(val, CultureInfo.InvariantCulture) };
                    _commits.Add(() =>
                    {
                        try { f.SetValue(_target, Convert.ChangeType(tb.Text, t, CultureInfo.InvariantCulture)); }
                        catch { }
                    });
                    AddRow(Label(f), tb);
                }
                // complex types are skipped
            }
        }

        private static string JoinArray(Array a)
        {
            if (a == null) return "";
            var parts = new List<string>();
            foreach (var v in a)
                parts.Add(Convert.ToString(v, CultureInfo.InvariantCulture));
            return string.Join(", ", parts);
        }

        private static object ParseArray(Type arrayType, string text, Array original)
        {
            Type elem = arrayType.GetElementType();
            string[] parts = (text ?? "").Split(',');

            int len = original != null ? original.Length : parts.Length;
            Array result = Array.CreateInstance(elem, len);

            for (int i = 0; i < len; i++)
            {
                object old = original != null && i < original.Length ? original.GetValue(i) : null;

                if (i < parts.Length && !string.IsNullOrWhiteSpace(parts[i]))
                {
                    try { result.SetValue(Convert.ChangeType(parts[i].Trim(), elem, CultureInfo.InvariantCulture), i); }
                    catch { result.SetValue(old, i); }
                }
                else
                {
                    result.SetValue(old, i);
                }
            }

            return result;
        }

        private void AddRow(string label, FrameworkElement control)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lbl = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.FindResource("TextSecondary")
            };
            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(control, 1);
            g.Children.Add(lbl);
            g.Children.Add(control);
            _content.Children.Add(g);
        }
    }
}
