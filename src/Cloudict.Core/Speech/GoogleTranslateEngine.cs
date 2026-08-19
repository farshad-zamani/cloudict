using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cloudict.Abstractions;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace Cloudict.Speech
{
    /// <summary>A status update, as a localization key plus arguments.</summary>
    public sealed class EngineStatusEventArgs : EventArgs
    {
        public EngineStatusEventArgs(string messageKey, params object[] args)
        {
            MessageKey = messageKey;
            Args = args ?? Array.Empty<object>();
        }

        public string MessageKey { get; }
        public object[] Args { get; }
    }

    /// <summary>
    /// Drives the Google Translate page in a helper Chrome window and reports what it hears.
    ///
    /// <para>This is Cloudict's speech engine: it has no API key and no local model, it simply
    /// automates the public Google Translate voice input. All of it used to live inside the WPF
    /// <c>MainWindow</c>, mixed with dispatcher calls and status-bar assignments, which meant the
    /// recognition logic could not be reused by any other front end. It is now UI-free — it raises
    /// events carrying localization keys and lets whoever owns a screen decide how to show
    /// them — so the same engine serves Windows, Linux and macOS.</para>
    ///
    /// <para>The selector strategy is carried over unchanged, because it is the product of a lot of
    /// trial against a page Google keeps changing: the voice button is found by a stable
    /// <c>jsname</c> rather than by position or label, and text is read through a chain of
    /// fallbacks. The user's own selectors from Settings are always tried first, so a future page
    /// change can be worked around without a new build.</para>
    /// </summary>
    public sealed class GoogleTranslateEngine : IDisposable
    {
        /// <summary>
        /// The voice button's <c>jsname</c>. It is a single toggle, and carries the
        /// <c>XiUwde</c> class while actively listening — which is how we avoid switching an
        /// already-live microphone back off.
        /// </summary>
        private const string VoiceButtonJsName = "Sz6qce";
        private const string ListeningClass = "XiUwde";

        /// <summary>Structural fallbacks for the voice button, tried after the user's own selector.</summary>
        private static readonly string[] BuiltInMicXPaths =
        {
            "//button[@jsname='Sz6qce']",
            "//*[@id=\"yDmH0d\"]/c-wiz/div/div[2]/c-wiz/div[2]/c-wiz/div[1]/div[2]/div[2]/div/c-wiz/div[5]/div/div[1]/c-wiz/span[2]/span/button",
            "//*[@id=\"yDmH0d\"]/c-wiz/div/div[2]/c-wiz/div[2]/c-wiz/div[1]/div[2]/div[2]/div/c-wiz/div[4]/div/div[1]/c-wiz/span[2]/span/button"
        };

        private readonly BrowserProvisioner _provisioner;
        private readonly Func<AppSettings> _settings;
        private readonly object _gate = new object();

        private IWebDriver _driver;
        private BrowserProvisioner.Provision _provision;
        private bool _disposed;

        public GoogleTranslateEngine(BrowserProvisioner provisioner, Func<AppSettings> settingsAccessor)
        {
            _provisioner = provisioner ?? throw new ArgumentNullException(nameof(provisioner));
            _settings = settingsAccessor ?? throw new ArgumentNullException(nameof(settingsAccessor));
        }

        /// <summary>Progress and error updates, as localization keys.</summary>
        public event EventHandler<EngineStatusEventArgs> StatusChanged;

        /// <summary>Raised when the helper browser opens or closes.</summary>
        public event EventHandler<bool> BrowserOpenChanged;

        /// <summary>True while the helper browser is open.</summary>
        public bool IsBrowserOpen => _driver != null;

        /// <summary>True between successful <see cref="StartListeningAsync"/> and stop.</summary>
        public bool IsListening { get; private set; }

        /// <summary>Chrome and driver details, once resolved. Null until the browser is first opened.</summary>
        public BrowserProvisioner.Provision Provision => _provision;

        private void Report(string key, params object[] args) =>
            StatusChanged?.Invoke(this, new EngineStatusEventArgs(key, args));

        #region Browser lifecycle

        /// <summary>
        /// Opens the helper browser on the Google Translate page. Safe to call when already open.
        /// </summary>
        public async Task<bool> OpenBrowserAsync(CancellationToken ct = default)
        {
            if (IsBrowserOpen) return true;

            return await Task.Run(() =>
            {
                lock (_gate)
                {
                    if (IsBrowserOpen) return true;

                    try
                    {
                        _provision ??= _provisioner.Resolve(
                            status => Report(status.MessageKey, status.Args), ct: ct);

                        Report("Main_St_OpeningGT");
                        _driver = CreateDriver(_provision);
                        _driver.Navigate().GoToUrl(BuildUrl());

                        Report("Main_St_WaitingPageLoad");
                        new WebDriverWait(_driver, TimeSpan.FromSeconds(30)).Until(
                            d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").Equals("complete"));

                        Report("Main_St_BrowserReadyPressGreen");
                        BrowserOpenChanged?.Invoke(this, true);
                        return true;
                    }
                    catch (BrowserProvisioner.ProvisionException ex)
                    {
                        // Already an actionable message ("install Chrome"); pass the key through.
                        Report(ex.MessageKey);
                        CloseInternal();
                        return false;
                    }
                    catch (Exception ex)
                    {
                        Report("Main_St_BrowserStartErrorPrefix_Fmt", ex.Message);
                        CloseInternal();
                        return false;
                    }
                }
            }, ct);
        }

        /// <summary>Closes the helper browser and forgets the session.</summary>
        public Task CloseBrowserAsync() => Task.Run(() =>
        {
            lock (_gate)
            {
                CloseInternal();
                Report("Main_St_ChromeClosed");
            }
        });

        private void CloseInternal()
        {
            IsListening = false;

            if (_driver == null) return;

            try { _driver.Quit(); }
            catch (Exception ex) { Debug.WriteLine($"[GoogleTranslateEngine] quit failed: {ex.Message}"); }

            _driver = null;
            BrowserOpenChanged?.Invoke(this, false);
        }

        private IWebDriver CreateDriver(BrowserProvisioner.Provision provision)
        {
            var options = new ChromeOptions();
            options.AddArgument("--disable-gpu");
            options.AddArgument("--window-size=450,540");
            options.AddArgument("--disable-extensions");
            options.AddArgument("--disable-popup-blocking");
            options.AddArgument("--disable-default-apps");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");

            // Grants the microphone without a prompt; without it the page silently hears nothing.
            options.AddArgument("--use-fake-ui-for-media-stream");

            var language = TypingLanguage();
            options.AddArgument($"--lang={language}");
            options.AddUserProfilePreference("profile.default_content_setting_values.media_stream_mic", 1);
            options.AddUserProfilePreference("intl.accept_languages", language);

            if (!string.IsNullOrWhiteSpace(provision.ChromePath))
                options.BinaryLocation = provision.ChromePath;

            var service = ChromeDriverService.CreateDefaultService(
                provision.DriverDirectory, provision.DriverFileName);
            service.HideCommandPromptWindow = true;

            // Only when Chrome outran every driver available and the network could not help.
            if (provision.RequiresBuildCheckOverride) service.DisableBuildCheck = true;

            return new ChromeDriver(service, options);
        }

        private string TypingLanguage()
        {
            var language = _settings()?.TypingLanguage;
            return string.IsNullOrWhiteSpace(language) ? "en" : language;
        }

        /// <summary>
        /// Builds the Google Translate URL for the configured dictation language. <c>hl</c> follows
        /// that language so the page's own labels match what is being spoken, which keeps the
        /// microphone selectors working across languages.
        /// </summary>
        internal string BuildUrl()
        {
            var language = TypingLanguage();
            var target = language == "en" ? "fa" : "en";
            return $"https://translate.google.com/?hl={language}&sl={language}&tl={target}&op=translate";
        }

        #endregion

        #region Microphone

        /// <summary>Starts Google Translate listening. Returns false if the button could not be found.</summary>
        public Task<bool> StartListeningAsync() => Task.Run(() =>
        {
            if (!IsBrowserOpen) return false;

            Report("Main_St_ActivatingMic");
            WaitForPageInteractive();

            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (attempt > 0)
                {
                    Report("Main_St_MicAttempt", attempt + 1);
                    Thread.Sleep(300);
                }

                // Click the toggle only when it is not already listening, so a live microphone is
                // never switched back off by a retry.
                if (TryScript($@"
                        var b = document.querySelector('button[jsname=""{VoiceButtonJsName}""]');
                        if(!b) return false;
                        if(!b.classList.contains('{ListeningClass}')) b.click();
                        return true;"))
                {
                    IsListening = true;
                    Report("Main_St_MicActivated");
                    return true;
                }

                if (TryClickAny(MicXPaths()))
                {
                    IsListening = true;
                    Report("Main_St_MicActivated");
                    return true;
                }
            }

            Report("Main_St_MicButtonNotFound");
            return false;
        });

        /// <summary>Stops Google Translate listening.</summary>
        public Task<bool> StopListeningAsync() => Task.Run(() =>
        {
            if (!IsBrowserOpen) return false;

            // Same toggle, opposite condition: click only while it *is* listening.
            if (TryScript($@"
                    var b = document.querySelector('button[jsname=""{VoiceButtonJsName}""]');
                    if(!b) return false;
                    if(b.classList.contains('{ListeningClass}')) b.click();
                    return true;"))
            {
                IsListening = false;
                Report("Main_St_MicStopped");
                return true;
            }

            if (TryClickAny(MicXPaths()))
            {
                IsListening = false;
                Report("Main_St_MicStopped");
                return true;
            }

            // Escape is the last resort; the page treats it as "stop listening".
            try
            {
                new OpenQA.Selenium.Interactions.Actions(_driver).SendKeys(Keys.Escape).Perform();
                IsListening = false;
                Report("Main_St_StopViaEsc");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GoogleTranslateEngine] escape fallback failed: {ex.Message}");
            }

            Report("Main_St_StopAllFailed");
            return false;
        });

        /// <summary>
        /// Stops and restarts listening, and clears the source box. Google Translate keeps revising
        /// a long dictation, so periodically restarting keeps its buffer short and the text stable.
        /// </summary>
        public async Task ResetMicrophoneAsync()
        {
            await StopListeningAsync();
            await ClearSourceTextAsync();
            await Task.Delay(200);
            await StartListeningAsync();
        }

        /// <summary>The user's configured selector first, then the built-in structural fallbacks.</summary>
        private IEnumerable<string> MicXPaths()
        {
            var configured = _settings()?.MicButtonXPath;
            if (!string.IsNullOrWhiteSpace(configured)) yield return configured.Trim();

            foreach (var xpath in BuiltInMicXPaths) yield return xpath;
        }

        private void WaitForPageInteractive()
        {
            try
            {
                new WebDriverWait(_driver, TimeSpan.FromSeconds(1)).Until(d =>
                {
                    try
                    {
                        return ((IJavaScriptExecutor)d).ExecuteScript(
                            "return document.readyState === 'complete' && document.querySelectorAll('button').length > 0")
                            .Equals(true);
                    }
                    catch { return false; }
                });

                Thread.Sleep(300);
            }
            catch (Exception)
            {
                Report("Main_St_PageNotReadyContinue");
            }
        }

        #endregion

        #region Reading and clearing text

        /// <summary>
        /// Reads whatever Google Translate currently has in its source box.
        ///
        /// <para>Runs entirely as one script so the page is sampled at a single instant — reading
        /// through several round trips would catch the box mid-revision. The chain of selectors
        /// (aria-label, then class, then any visible textarea) exists because Google changes the
        /// page's structure without notice and the labels are language-specific.</para>
        /// </summary>
        public Task<string> ReadRecognizedTextAsync() => Task.Run(() =>
        {
            if (!IsBrowserOpen) return string.Empty;

            var labels = string.Join(",", AriaLabels().Select(l => "\"" + l.Replace("\"", "\\\"") + "\""));
            var classes = string.Join(",", ClassSelectors().Select(c => "\"" + c.Replace("\"", "\\\"") + "\""));

            var script = $@"
                try {{
                    var labels = [{labels}];
                    for (var i = 0; i < labels.length; i++) {{
                        var t = document.querySelector('textarea[aria-label=""' + labels[i] + '""]');
                        if (t && t.value && t.value.trim()) return t.value.trim();
                    }}

                    var classes = [{classes}];
                    for (var j = 0; j < classes.length; j++) {{
                        var els = document.querySelectorAll(classes[j]);
                        for (var k = 0; k < els.length; k++) {{
                            if (els[k].value && els[k].value.trim()) return els[k].value.trim();
                        }}
                    }}

                    var all = document.querySelectorAll('textarea');
                    for (var m = 0; m < all.length; m++) {{
                        if (all[m].value && all[m].value.trim() && all[m].offsetParent !== null)
                            return all[m].value.trim();
                    }}

                    return '';
                }} catch (e) {{ return ''; }}";

            var result = TryScriptValue(script);
            if (!string.IsNullOrEmpty(result)) return result;

            // If scripting failed outright, fall back to WebDriver's own element lookup.
            return ReadViaElements();
        });

        /// <summary>Empties the source box and tells the page, so recognition starts from nothing.</summary>
        public Task ClearSourceTextAsync() => Task.Run(() =>
        {
            if (!IsBrowserOpen) return;

            // Dispatching 'input' matters: setting .value alone does not notify the page's own
            // handlers, and the previous text reappears on the next revision.
            TryScript(@"
                var all = document.querySelectorAll('textarea');
                for (var i = 0; i < all.length; i++) {
                    if (all[i].offsetParent !== null) {
                        all[i].value = '';
                        all[i].dispatchEvent(new Event('input', { bubbles: true }));
                    }
                }
                return true;");
        });

        private IEnumerable<string> AriaLabels()
        {
            var configured = _settings()?.TextBoxAriaLabels;
            if (configured != null)
                foreach (var label in configured.Where(l => !string.IsNullOrWhiteSpace(l)))
                    yield return label.Trim();

            // Language-specific labels Google uses; harmless when they do not match.
            yield return "Source text";
            yield return "متن منبع";
            yield return "متن برای ترجمه";
            yield return "Text to translate";
        }

        private IEnumerable<string> ClassSelectors()
        {
            var configured = _settings()?.TextBoxClassSelectors;
            if (configured != null)
                foreach (var selector in configured.Where(s => !string.IsNullOrWhiteSpace(s)))
                    yield return "textarea." + selector.Trim().TrimStart('.');

            yield return "textarea[jsname]";
            yield return "textarea[data-initial-value]";
        }

        private string ReadViaElements()
        {
            try
            {
                foreach (var label in AriaLabels())
                {
                    try
                    {
                        var element = _driver.FindElement(By.CssSelector($"textarea[aria-label='{label}']"));
                        var value = element.GetAttribute("value");
                        if (!string.IsNullOrEmpty(value)) return value;
                    }
                    catch (Exception ex) { Debug.WriteLine($"[GoogleTranslateEngine] aria '{label}': {ex.Message}"); }
                }

                foreach (var selector in ClassSelectors())
                {
                    try
                    {
                        foreach (var element in _driver.FindElements(By.CssSelector(selector)))
                        {
                            var value = element.GetAttribute("value");
                            if (!string.IsNullOrEmpty(value)) return value;
                        }
                    }
                    catch (Exception ex) { Debug.WriteLine($"[GoogleTranslateEngine] selector '{selector}': {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GoogleTranslateEngine] element fallback failed: {ex.Message}");
            }

            return string.Empty;
        }

        #endregion

        #region Scripting helpers

        private bool TryScript(string script)
        {
            try
            {
                var result = ((IJavaScriptExecutor)_driver).ExecuteScript(script);
                return result is bool ok && ok;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GoogleTranslateEngine] script failed: {ex.Message}");
                return false;
            }
        }

        private string TryScriptValue(string script)
        {
            try
            {
                return ((IJavaScriptExecutor)_driver).ExecuteScript(script)?.ToString() ?? string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GoogleTranslateEngine] script failed: {ex.Message}");
                return null;
            }
        }

        private bool TryClickAny(IEnumerable<string> xpaths)
        {
            foreach (var xpath in xpaths)
            {
                try
                {
                    var element = _driver.FindElement(By.XPath(xpath));
                    if (element == null || !element.Displayed) continue;

                    element.Click();
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GoogleTranslateEngine] xpath '{xpath}': {ex.Message}");
                }
            }

            return false;
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (_gate) CloseInternal();
        }
    }
}
