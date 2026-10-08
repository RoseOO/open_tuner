using System;
using System.Windows;

namespace OpenTuner.Wpf
{
    public static class ThemeManager
    {
        public static bool IsDark { get; private set; }
        public static bool IsHighContrast { get; private set; }

        public static void Apply(bool dark)
        {
            IsDark = dark;
            IsHighContrast = false;
            ApplySource(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml");
        }

        public static void ApplyHighContrast(bool highContrast)
        {
            if (highContrast)
            {
                IsHighContrast = true;
                ApplySource("Themes/HighContrast.xaml");
            }
            else
            {
                IsHighContrast = false;
                Apply(IsDark);
            }
        }

        private static void ApplySource(string relativeUri)
        {
            ResourceDictionary theme = new ResourceDictionary
            {
                Source = new Uri(relativeUri, UriKind.Relative)
            };

            var dicts = Application.Current.Resources.MergedDictionaries;

            if (dicts.Count == 0)
                dicts.Add(theme);
            else
                dicts[0] = theme;   // the first merged dictionary is always the active theme
        }
    }
}
