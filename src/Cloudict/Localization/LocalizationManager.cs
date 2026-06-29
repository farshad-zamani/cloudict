using System;
using System.Windows;

namespace Cloudict
{
    /// <summary>
    /// Manages the application's UI language.
    ///
    /// English is the default and fallback language; Persian (and any future
    /// languages) are loaded from per-language resource dictionaries under
    /// <c>/Localization/Strings</c>. The language is applied once at startup —
    /// switching it requires an application restart.
    ///
    /// To add a new language, drop a <c>Strings.&lt;code&gt;.xaml</c> resource
    /// dictionary next to the existing ones and add the code to
    /// <see cref="SupportedLanguages"/> (and to <see cref="RightToLeftLanguages"/>
    /// if the script is right-to-left).
    /// </summary>
    public static class LocalizationManager
    {
        /// <summary>Language used when none is selected or the selection is invalid.</summary>
        public const string DefaultLanguage = "en";

        /// <summary>Language codes the UI can be displayed in.</summary>
        public static readonly string[] SupportedLanguages = { "en", "fa" };

        /// <summary>Language codes that use a right-to-left layout.</summary>
        private static readonly string[] RightToLeftLanguages = { "fa" };

        /// <summary>The currently active language code.</summary>
        public static string CurrentLanguage { get; private set; } = DefaultLanguage;

        /// <summary>True when the active language is written right-to-left.</summary>
        public static bool IsRightToLeft => Array.IndexOf(RightToLeftLanguages, CurrentLanguage) >= 0;

        /// <summary>The flow direction matching the active language.</summary>
        public static FlowDirection FlowDirection =>
            IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        /// <summary>
        /// Loads the requested language into the application's merged resource
        /// dictionaries. English is merged first as a fallback, then the selected
        /// language is merged on top, so any key missing from a translation still
        /// resolves to its English text. Must be called before any window is shown.
        /// </summary>
        public static void Apply(string language)
        {
            if (string.IsNullOrWhiteSpace(language) || Array.IndexOf(SupportedLanguages, language) < 0)
                language = DefaultLanguage;

            CurrentLanguage = language;

            var merged = Application.Current.Resources.MergedDictionaries;

            // Remove any language dictionaries left over from a previous Apply call.
            for (int i = merged.Count - 1; i >= 0; i--)
            {
                var src = merged[i].Source?.OriginalString ?? string.Empty;
                if (src.Contains("/Localization/Strings/Strings."))
                    merged.RemoveAt(i);
            }

            merged.Add(LoadLanguage(DefaultLanguage));
            if (language != DefaultLanguage)
                merged.Add(LoadLanguage(language));

            // Expose the active flow direction so windows can bind to it via DynamicResource.
            Application.Current.Resources["AppFlowDirection"] = FlowDirection;

            // Expose the UI font for the active language: Vazirmatn for Persian (RTL), Inter for English.
            // Reference embedded fonts via base-URI + "./folder/#Family"; this is the form that
            // reliably resolves resource-embedded fonts when a FontFamily is created in code.
            var baseUri = new Uri("pack://application:,,,/");
            Application.Current.Resources["AppFontFamily"] = new System.Windows.Media.FontFamily(
                baseUri, IsRightToLeft ? "./Assets/Fonts/#Vazirmatn" : "./Assets/Fonts/#Inter");
        }

        private static ResourceDictionary LoadLanguage(string language)
        {
            var uri = new Uri(
                $"pack://application:,,,/Localization/Strings/Strings.{language}.xaml",
                UriKind.Absolute);
            return new ResourceDictionary { Source = uri };
        }

        /// <summary>Resolves a localized string by key, falling back to the key itself.</summary>
        public static string Get(string key)
        {
            return Application.Current?.TryFindResource(key) as string ?? key;
        }

        /// <summary>Resolves a localized format string and fills in <paramref name="args"/>.</summary>
        public static string Get(string key, params object[] args)
        {
            return string.Format(Get(key), args);
        }
    }

    /// <summary>Short alias for resolving localized strings from code-behind: <c>Loc.Get("Key")</c>.</summary>
    public static class Loc
    {
        public static string Get(string key) => LocalizationManager.Get(key);
        public static string Get(string key, params object[] args) => LocalizationManager.Get(key, args);
    }
}
