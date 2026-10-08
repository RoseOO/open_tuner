using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using opentuner.ExtraFeatures.QRZ;
using opentuner.Utilities;

namespace OpenTuner.Wpf.Dialogs
{
    /// <summary>
    /// QRZ.com XML API settings and manual callsign lookup.
    /// The service name of a received DATV signal is parsed for a callsign
    /// (service names often carry extra text after the callsign).
    /// </summary>
    public class QrzWindow : Window
    {
        private readonly QrzSettings _s;

        private TextBox _user, _search;
        private PasswordBox _pass;
        private CheckBox _enabled, _auto;
        private TextBlock _result;

        public QrzWindow(QrzSettings settings)
        {
            _s = settings ?? new QrzSettings();
            SettingsUi.StyleWindow(this, LocalizationManager.Get("dt.qrz"), 460);

            StackPanel content;
            Content = SettingsUi.Layout(this, out content, Save);

            StackPanel b;
            var card = SettingsUi.Card("QRZ.com account (XML subscription required)", out b);
            _user = SettingsUi.Text(_s.username);
            SettingsUi.Row(b, "Username:", _user);
            _pass = new PasswordBox { Password = _s.password ?? "" };
            SettingsUi.Row(b, "Password:", _pass);
            _enabled = new CheckBox { Content = "Enable QRZ lookups" };
            b.Children.Add(_enabled);
            _auto = new CheckBox { Content = "Auto lookup when a service name appears" };
            b.Children.Add(_auto);
            content.Children.Add(card);

            StackPanel l;
            var lookup = SettingsUi.Card("Callsign lookup", out l);
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _search = SettingsUi.Text("");
            _search.Margin = new Thickness(0, 0, 8, 0);
            _search.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) DoLookup(); };
            var go = new Button { Content = "Lookup" };
            go.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
            go.Click += (s, e) => DoLookup();
            Grid.SetColumn(_search, 0);
            Grid.SetColumn(go, 1);
            row.Children.Add(_search);
            row.Children.Add(go);
            l.Children.Add(row);

            _result = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
                Foreground = (Brush)Application.Current.FindResource("TextSecondary")
            };
            l.Children.Add(_result);
            content.Children.Add(lookup);

            _enabled.IsChecked = _s.enabled;
            _auto.IsChecked = _s.auto_lookup;
        }

        private async void DoLookup()
        {
            string callsign = CallsignParser.Extract(_search.Text);
            if (string.IsNullOrEmpty(callsign))
                callsign = _search.Text.Trim().ToUpperInvariant();

            if (string.IsNullOrEmpty(callsign))
            {
                _result.Text = "Enter a callsign or a service name.";
                return;
            }

            _result.Text = "Looking up " + callsign + " ...";

            var client = new QrzClient(new QrzSettings
            {
                username = _user.Text.Trim(),
                password = _pass.Password
            });

            QrzResult r = await client.LookupAsync(callsign);

            if (!r.success)
            {
                _result.Text = "Lookup failed: " + r.error;
                return;
            }

            _result.Text =
                r.callsign + Environment.NewLine +
                (string.IsNullOrEmpty(r.name) ? "" : r.name + Environment.NewLine) +
                (string.IsNullOrEmpty(r.city) ? "" : r.city + ", ") +
                (string.IsNullOrEmpty(r.state) ? "" : r.state + ", ") +
                (string.IsNullOrEmpty(r.country) ? "" : r.country + Environment.NewLine) +
                (string.IsNullOrEmpty(r.grid) ? "" : "Grid: " + r.grid + "  ") +
                (string.IsNullOrEmpty(r.licence_class) ? "" : "Class: " + r.licence_class);

            _search.Text = callsign;
        }

        private void Save()
        {
            _s.username = _user.Text.Trim();
            _s.password = _pass.Password;
            _s.enabled = _enabled.IsChecked == true;
            _s.auto_lookup = _auto.IsChecked == true;
            DialogResult = true;
            Close();
        }
    }
}
