using System;
using System.Windows;
using System.Windows.Media;
using opentuner;

namespace OpenTuner.Wpf.Dialogs
{
    /// <summary>
    /// Pop-out window hosting the QSO logger. The same logger is also available
    /// as a bottom tab; both share the same on-disk store and refresh on focus.
    /// </summary>
    public class QsoLogWindow : Window
    {
        private readonly QsoLogControl _control;

        public QsoLogWindow(MainSettings settings, Func<int> rxCount,
                            Func<int, (string call, double freqMhz)> signalForRx,
                            Func<int, string> signalInfoForRx)
        {
            Title = "QSO Log";
            Width = 840; Height = 680;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)Application.Current.FindResource("WindowBackground");
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            _control = new QsoLogControl(settings, rxCount, signalForRx, signalInfoForRx);
            Content = _control;

            Activated += (s, e) => _control.Reload();
        }
    }
}
