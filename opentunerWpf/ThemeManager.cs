using System;
using System.Windows;

namespace OpenTuner.Wpf
{
    public static class ThemeManager
    {
        public static bool IsDark { get; private set; }

        public static void Apply(bool dark)
        {
            IsDark = dark;

            ResourceDictionary theme = new ResourceDictionary
            {
                Source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative)
            };

            var dicts = Application.Current.Resources.MergedDictionaries;

            if (dicts.Count == 0)
                dicts.Add(theme);
            else
                dicts[0] = theme;   // the first merged dictionary is always the active theme
        }
    }
}
