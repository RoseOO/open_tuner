using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenTuner.Wpf.Dialogs
{
    /// <summary>
    /// Simple "which hardware interface?" chooser used when a source is set to
    /// "Always Ask". Returns the selected index via <see cref="SelectedIndex"/>
    /// and DialogResult true.
    /// </summary>
    public class ChooseInterfaceWindow : Window
    {
        public int SelectedIndex { get; private set; } = -1;

        private readonly ComboBox _combo;

        public ChooseInterfaceWindow(string title, string[] options)
        {
            Title = title;
            Width = 380; Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)Application.Current.FindResource("WindowBackground");
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            StackPanel content;
            Content = SettingsUi.Layout(this, out content, () => Accept());

            StackPanel body;
            var card = SettingsUi.Card("Select interface", out body);

            _combo = SettingsUi.Combo(options);
            if (_combo.Items.Count > 0)
                _combo.SelectedIndex = 0;
            SettingsUi.Row(body, "Interface:", _combo);

            body.Children.Add(new TextBlock
            {
                Text = "Choose the hardware interface to use for this connection.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = (Brush)Application.Current.FindResource("TextSecondary")
            });

            content.Children.Add(card);
        }

        private void Accept()
        {
            SelectedIndex = _combo.SelectedIndex;
            DialogResult = true;
            Close();
        }
    }
}
