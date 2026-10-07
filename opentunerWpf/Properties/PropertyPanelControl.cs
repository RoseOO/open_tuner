using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using opentuner.MediaSources;
using opentuner.Utilities;

namespace OpenTuner.Wpf.Properties
{
    /// <summary>
    /// Native WPF tuner property panel. Builds its UI from the UI-agnostic
    /// property model exposed by OTSource and polls live values.
    /// </summary>
    public class PropertyPanelControl : UserControl
    {
        private sealed class ValueBinding
        {
            public int GroupId;
            public string Key;
            public TextBlock Label;
        }

        private sealed class SliderBinding
        {
            public int GroupId;
            public string Key;
            public Slider Slider;
            public TextBlock Label;
        }

        private sealed class MediaBinding
        {
            public int GroupId;
            public Button Mute;
            public Button Record;
            public Button Udp;
        }

        private readonly StackPanel _root = new StackPanel();
        private readonly DispatcherTimer _timer;
        private readonly List<ValueBinding> _valueBindings = new List<ValueBinding>();
        private readonly List<SliderBinding> _sliderBindings = new List<SliderBinding>();
        private readonly List<MediaBinding> _mediaBindings = new List<MediaBinding>();

        private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(219, 112, 147));
        private static readonly Brush StreamingBrush = new SolidColorBrush(Color.FromRgb(64, 224, 208));

        private static readonly Brush TransparentBrush = Brushes.Transparent;

        private OTSource _source;
        private bool _updatingSliders;

        public PropertyPanelControl()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            scroll.Content = _root;
            Content = scroll;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _timer.Tick += (s, e) => Refresh();
        }

        public void Attach(OTSource source)
        {
            _source = source;
            Build();
            _timer.Start();
        }

        public void Detach()
        {
            _timer.Stop();
            _source = null;
            _valueBindings.Clear();
            _sliderBindings.Clear();
            _root.Children.Clear();
        }

        private void Build()
        {
            _valueBindings.Clear();
            _sliderBindings.Clear();
            _mediaBindings.Clear();
            _root.Children.Clear();

            if (_source == null)
                return;

            List<PropertyGroupDescriptor> groups = _source.GetPropertyGroups();

            foreach (PropertyGroupDescriptor g in groups)
            {
                var card = new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14, 12, 14, 12),
                    Margin = new Thickness(0, 0, 0, 12)
                };
                card.SetResourceReference(Border.BackgroundProperty, "SurfaceBackground");
                card.SetResourceReference(Border.BorderBrushProperty, "Border");

                var stack = new StackPanel();

                var title = new TextBlock
                {
                    Text = g.Title,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                title.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                stack.Children.Add(title);

                foreach (PropertyItemDescriptor item in g.Items)
                {
                    switch (item.Kind)
                    {
                        case PropertyItemKind.Slider:
                            stack.Children.Add(BuildSlider(g.Id, item));
                            break;
                        case PropertyItemKind.MediaControls:
                            stack.Children.Add(BuildMediaControls(g.Id, item));
                            break;
                        default:
                            stack.Children.Add(BuildValue(g.Id, item));
                            break;
                    }
                }

                card.Child = stack;
                _root.Children.Add(card);
            }
        }

        private FrameworkElement BuildValue(int groupId, PropertyItemDescriptor item)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new TextBlock
            {
                Text = item.Name,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            Grid.SetColumn(name, 0);

            var value = new TextBlock
            {
                Text = "",
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            value.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            Grid.SetColumn(value, 1);

            grid.Children.Add(name);
            grid.Children.Add(value);

            _valueBindings.Add(new ValueBinding { GroupId = groupId, Key = item.Key, Label = value });

            // right-click context menu (tuner control, RF input, symbol rate, LNB, presets...)
            grid.MouseRightButtonUp += (s, e) =>
            {
                if (_source == null)
                    return;

                List<PropertyMenuOption> opts;
                try { opts = _source.GetPropertyMenu(groupId, item.Key); }
                catch { return; }

                if (opts == null || opts.Count == 0)
                    return;

                var cm = new ContextMenu();
                foreach (PropertyMenuOption o in opts)
                {
                    if (o.Separator)
                    {
                        cm.Items.Add(new Separator());
                        continue;
                    }

                    var mi = new MenuItem { Header = o.Label };
                    PropertyMenuOption local = o;
                    int g = groupId;
                    string k = item.Key;
                    mi.Click += (a, b) => { try { _source?.InvokePropertyCommand(g, k, local.Command, local.Options); } catch { } };
                    cm.Items.Add(mi);
                }

                cm.PlacementTarget = grid;
                cm.IsOpen = true;
                e.Handled = true;
            };

            return grid;
        }

        private FrameworkElement BuildSlider(int groupId, PropertyItemDescriptor item)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new TextBlock { Text = item.Name };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            Grid.SetColumn(name, 0);

            var value = new TextBlock
            {
                Text = "",
                FontWeight = FontWeights.SemiBold
            };
            value.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            Grid.SetColumn(value, 1);

            header.Children.Add(name);
            header.Children.Add(value);
            panel.Children.Add(header);

            var slider = new Slider
            {
                Minimum = item.Min,
                Maximum = item.Max,
                SmallChange = 1,
                LargeChange = 10,
                Margin = new Thickness(0, 6, 0, 0)
            };
            slider.ValueChanged += (s, e) =>
            {
                if (_updatingSliders)
                    return;

                value.Text = ((int)slider.Value).ToString();
                try { _source?.SetPropertySlider(groupId, item.Key, (int)slider.Value); } catch { }
            };
            panel.Children.Add(slider);

            _sliderBindings.Add(new SliderBinding { GroupId = groupId, Key = item.Key, Slider = slider, Label = value });

            return panel;
        }

        private FrameworkElement BuildMediaControls(int groupId, PropertyItemDescriptor item)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 2) };

            var mb = new MediaBinding { GroupId = groupId };
            mb.Mute = MakeButton("Mute", groupId, item.Key, 0, panel);
            MakeButton("Snapshot", groupId, item.Key, 1, panel);
            mb.Record = MakeButton("Record", groupId, item.Key, 2, panel);
            mb.Udp = MakeButton("UDP", groupId, item.Key, 3, panel);
            _mediaBindings.Add(mb);

            return panel;
        }

        private Button MakeButton(string text, int groupId, string key, int function, Panel panel)
        {
            var b = new Button
            {
                Content = text,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(10, 5, 10, 5)
            };
            b.Click += (s, e) => { try { _source?.SetPropertyMediaButton(groupId, key, function); } catch { } };
            panel.Children.Add(b);
            return b;
        }

        private void Refresh()
        {
            if (_source == null)
                return;

            foreach (ValueBinding vb in _valueBindings)
            {
                try { vb.Label.Text = _source.GetPropertyValue(vb.GroupId, vb.Key); } catch { }
            }

            _updatingSliders = true;
            foreach (SliderBinding sb in _sliderBindings)
            {
                try
                {
                    string v = _source.GetPropertyValue(sb.GroupId, sb.Key);
                    sb.Label.Text = v;
                    if (int.TryParse(v, out int iv) && !sb.Slider.IsMouseCaptured && (int)sb.Slider.Value != iv)
                        sb.Slider.Value = Math.Max(sb.Slider.Minimum, Math.Min(sb.Slider.Maximum, iv));
                }
                catch { }
            }
            _updatingSliders = false;

            foreach (MediaBinding mb in _mediaBindings)
            {
                try
                {
                    int st = _source.GetMediaButtonState(mb.GroupId);
                    mb.Mute.Background = (st & 1) != 0 ? MutedBrush : TransparentBrush;
                    mb.Record.Background = (st & 2) != 0 ? MutedBrush : TransparentBrush;
                    mb.Udp.Background = (st & 4) != 0 ? StreamingBrush : TransparentBrush;
                }
                catch { }
            }
        }
    }
}
