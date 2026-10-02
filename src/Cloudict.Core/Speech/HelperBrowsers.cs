namespace Cloudict.Speech
{
    /// <summary>
    /// The values of <see cref="AppSettings.HelperBrowser"/>: which browser hosts Google Translate.
    /// </summary>
    public static class HelperBrowsers
    {
        /// <summary>Chrome if installed; else a Chrome for Testing already downloaded; else Edge, if
        /// its speech recognition works here; else Chrome for Testing, downloaded once.</summary>
        public const string Auto = "auto";

        public const string Chrome = "chrome";
        public const string Edge = "edge";

        /// <summary>Google's portable Chrome build for automation, downloaded by Cloudict.</summary>
        public const string ChromeForTesting = "cft";

        public static readonly string[] All = { Auto, Chrome, Edge, ChromeForTesting };

        /// <summary>Anything unrecognised — including a settings file from before 3.2.6 — is automatic.</summary>
        public static string Normalise(string value) =>
            (value ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                Chrome => Chrome,
                Edge => Edge,
                ChromeForTesting => ChromeForTesting,
                _ => Auto
            };
    }
}
