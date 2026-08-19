using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Cloudict
{
    /// <summary>
    /// The application's UI language.
    ///
    /// <para>Strings used to live in WPF <c>ResourceDictionary</c> files, which tied every localized
    /// message to a UI framework and put them out of reach of <c>Cloudict.Core</c> — the reason Core
    /// currently passes resource <em>keys</em> outward instead of finished sentences. They are now
    /// plain JSON embedded in this assembly, so the same dictionary serves the UI, Core and the
    /// platform layer, on every operating system.</para>
    ///
    /// <para>English is loaded first and always kept as the fallback, so a key missing from a
    /// translation still renders readable text rather than the key itself.</para>
    /// </summary>
    public static class LocalizationManager
    {
        public const string DefaultLanguage = "en";

        /// <summary>Language codes the UI can be displayed in.</summary>
        public static readonly string[] SupportedLanguages = { "en", "fa" };

        /// <summary>Language codes written right-to-left.</summary>
        private static readonly string[] RightToLeftLanguages = { "fa" };

        private static Dictionary<string, string> _fallback = new Dictionary<string, string>();
        private static Dictionary<string, string> _current = new Dictionary<string, string>();

        /// <summary>The active language code.</summary>
        public static string CurrentLanguage { get; private set; } = DefaultLanguage;

        /// <summary>True when the active language is written right-to-left.</summary>
        public static bool IsRightToLeft => Array.IndexOf(RightToLeftLanguages, CurrentLanguage) >= 0;

        /// <summary>Raised after the language changes, so views can refresh.</summary>
        public static event EventHandler LanguageChanged;

        static LocalizationManager()
        {
            _fallback = Load(DefaultLanguage);
            _current = _fallback;
        }

        /// <summary>Selects the UI language, falling back to English for anything unsupported.</summary>
        public static void Apply(string language)
        {
            if (string.IsNullOrWhiteSpace(language) || Array.IndexOf(SupportedLanguages, language) < 0)
                language = DefaultLanguage;

            CurrentLanguage = language;
            _current = language == DefaultLanguage ? _fallback : Load(language);

            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Resolves a key, falling back to English and finally to the key itself.</summary>
        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            if (_current.TryGetValue(key, out var value)) return value;
            if (_fallback.TryGetValue(key, out var english)) return english;

            Debug.WriteLine($"[LocalizationManager] missing string '{key}'");
            return key;
        }

        /// <summary>Resolves a format string and fills in <paramref name="args"/>.</summary>
        public static string Get(string key, params object[] args)
        {
            var format = Get(key);
            if (args == null || args.Length == 0) return format;

            try
            {
                return string.Format(CultureInfo.CurrentCulture, format, args);
            }
            catch (FormatException ex)
            {
                // A malformed placeholder must not take down a status update.
                Debug.WriteLine($"[LocalizationManager] bad format for '{key}': {ex.Message}");
                return format;
            }
        }

        /// <summary>All keys in the active language, for diagnostics and translation tooling.</summary>
        public static IReadOnlyCollection<string> Keys => _current.Keys.ToList();

        private static Dictionary<string, string> Load(string language)
        {
            var resource = $"Cloudict.Localization.Strings.Strings.{language}.json";

            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
                if (stream == null)
                {
                    Debug.WriteLine($"[LocalizationManager] resource '{resource}' not found");
                    return new Dictionary<string, string>();
                }

                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();

                return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                       ?? new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LocalizationManager] could not load '{language}': {ex.Message}");
                return new Dictionary<string, string>();
            }
        }
    }

    /// <summary>Short alias for resolving localized strings: <c>Loc.Get("Key")</c>.</summary>
    public static class Loc
    {
        public static string Get(string key) => LocalizationManager.Get(key);
        public static string Get(string key, params object[] args) => LocalizationManager.Get(key, args);
    }
}
