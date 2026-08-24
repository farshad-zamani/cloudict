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
    public sealed class GoogleTranslateEngine : ISpeechEngine, IDisposable
    {
        /// <summary>
        /// The voice button's <c>jsname</c>. It is a single toggle, and carries the
        /// <c>XiUwde</c> class while actively listening — which is how we avoid switching an
        /// already-live microphone back off.
        /// </summary>
        private const string VoiceButtonJsName = "Sz6qce";
        private const string ListeningClass = "XiUwde";

        /// <summary>The page's own "clear source text" button — the X beside the box.</summary>
        private const string ClearButtonJsName = "r4nke";

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
        private bool _ready;
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

        /// <summary>
        /// True while the helper browser is open <em>and finished opening</em>.
        ///
        /// <para>The second half matters. This used to be a plain null check on the driver, which
        /// goes non-null the moment Chrome launches — well before Google Translate has finished
        /// building its page. A second caller arriving in that gap, which is exactly what pressing
        /// the green button during a slow start does, was told the browser was ready and began
        /// issuing commands on a session the first call was still setting up. Two threads driving
        /// one WebDriver is not something WebDriver supports, and a session broken that way takes
        /// Chrome down with it.</para>
        ///
        /// <para>Reporting "not open" until the setup finishes makes the second caller wait on the
        /// same lock instead, and it then finds a browser that genuinely is ready.</para>
        /// </summary>
        public bool IsBrowserOpen => _driver != null && _ready;

        /// <summary>True between successful <see cref="StartListeningAsync"/> and stop.</summary>
        public bool IsListening { get; private set; }

        /// <summary>Chrome and driver details, once resolved. Null until the browser is first opened.</summary>
        public BrowserProvisioner.Provision Provision => _provision;

        private void Report(string key, params object[] args) =>
            StatusChanged?.Invoke(this, new EngineStatusEventArgs(key, args));

        private static void Log(string message) => DiagnosticLog.Write("GoogleTranslateEngine", message);

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

                        // readyState only says the document finished loading. Google Translate is a
                        // single-page app that builds its controls afterwards, so the voice button
                        // does not exist yet at that point. Reporting the browser ready here made
                        // the first press of the shortcut fail and the second one work, because by
                        // then the page had caught up.
                        WaitForVoiceButton(TimeSpan.FromSeconds(20));

                        _ready = true;

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
            _ready = false;

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

            // Covers the case where the page reloaded after the browser was opened; when it is
            // already there this returns immediately.
            WaitForVoiceButton(TimeSpan.FromSeconds(10));

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
                    Log("microphone activated via jsname toggle");
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
        ///
        /// <para>The result says what actually happened rather than assuming it worked. Both halves
        /// can fail on a page that has been open for a long time — the box refuses to empty, or the
        /// voice button will not come back on — and the caller has to know, because text left in the
        /// box reads back as brand-new speech on the very next poll.</para>
        /// </summary>
        public async Task<MicResetResult> ResetMicrophoneAsync()
        {
            await StopListeningAsync();

            // Give the page time to actually stop before touching its box. Clearing while its own
            // recognition is still winding down is how the phrase came back: it finishes, writes
            // what it heard, and overwrites the empty box a moment after it was emptied. The stable
            // 2.x build waited about this long here, and the shorter wait that replaced it is why
            // the repeat became more frequent rather than less.
            await Task.Delay(700);

            var remaining = await ClearSourceTextAsync();
            await Task.Delay(300);

            var listening = await StartListeningAsync();

            var cleared = remaining != null && remaining.Length == 0;

            if (!cleared)
                Log($"source box not verified empty after the reset (remaining: {remaining?.Length.ToString() ?? "unknown"})");
            if (!listening)
                Log("the voice button did not come back on after the reset");

            return new MicResetResult(cleared, remaining ?? string.Empty, listening);
        }

        /// <summary>The user's configured selector first, then the built-in structural fallbacks.</summary>
        private IEnumerable<string> MicXPaths()
        {
            var configured = _settings()?.MicButtonXPath;
            if (!string.IsNullOrWhiteSpace(configured)) yield return configured.Trim();

            foreach (var xpath in BuiltInMicXPaths) yield return xpath;
        }

        /// <summary>
        /// Waits until the voice button actually exists in the page.
        ///
        /// <para>This is the difference between the shortcut working on the first press and needing
        /// a second one. Every other readiness signal — <c>readyState</c>, "some buttons exist" —
        /// goes true well before Google Translate has finished building its controls.</para>
        /// </summary>
        private bool WaitForVoiceButton(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            var started = DateTime.UtcNow;
            Log(FormattableString.Invariant($"waiting for the voice button (up to {timeout.TotalSeconds:N0}s)"));

            while (DateTime.UtcNow < deadline)
            {
                if (TryScript($@"return document.querySelector('button[jsname=""{VoiceButtonJsName}""]') !== null;"))
                {
                    Log(FormattableString.Invariant($"voice button appeared after {(DateTime.UtcNow - started).TotalSeconds:N1}s"));
                    return true;
                }

                // Fall back to the user's own selector, in case Google has changed the jsname and
                // they have already corrected it in Settings.
                foreach (var xpath in MicXPaths())
                {
                    try
                    {
                        if (_driver.FindElements(By.XPath(xpath)).Count > 0) return true;
                    }
                    catch (Exception ex) { Debug.WriteLine($"[GoogleTranslateEngine] probe '{xpath}': {ex.Message}"); }
                }

                Thread.Sleep(200);
            }

            Log(FormattableString.Invariant($"voice button did not appear within {timeout.TotalSeconds:N0}s"));
            return false;
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

        /// <summary>
        /// Empties the source box and tells the page, so recognition starts from nothing.
        ///
        /// <para>Returns whatever is <em>still</em> in the box — empty when the clear worked, and null
        /// when the page could not be asked at all. It is checked rather than assumed because the
        /// page does not always comply: after a long session, or once its own speech recognition has
        /// errored, Google Translate puts the previous phrase straight back. Treating that as new
        /// speech is what made Cloudict type the same sentence again and again until it was stopped
        /// by hand.</para>
        /// </summary>
        public async Task<string> ClearSourceTextAsync()
        {
            if (!IsBrowserOpen) return null;

            var labels = string.Join(",", AriaLabels().Select(l => "\"" + l.Replace("\"", "\\\"") + "\""));

            for (int attempt = 0; attempt < 3; attempt++)
            {
                await Task.Run(() => TryScript($@"
                    try {{
                        // Google's own clear button first. Emptying .value only changes the DOM; the
                        // page keeps its own idea of the phrase and can put it straight back, which
                        // is exactly how a finished sentence ended up being typed a second time.
                        // Pressing the button the user would press resets that state properly.
                        var clearBtn = document.querySelector(
                            'button[jsname=""{ClearButtonJsName}""], button[aria-label=""Clear source text""], button[aria-label=""پاک کردن متن منبع""]');
                        if (clearBtn && clearBtn.offsetParent !== null) {{
                            try {{ clearBtn.click(); }} catch (e) {{ }}
                        }}

                        // Then the box itself, addressed by the labels the user configured before any
                        // guesswork. Both events are dispatched: some of the page's handlers listen
                        // for 'change' rather than 'input', and one without the other leaves half of
                        // its state stale.
                        var cleared = false;
                        var labels = [{labels}];
                        for (var i = 0; i < labels.length; i++) {{
                            var el = document.querySelector('textarea[aria-label=""' + labels[i] + '""]');
                            if (el) {{
                                el.value = '';
                                el.dispatchEvent(new Event('input', {{ bubbles: true }}));
                                el.dispatchEvent(new Event('change', {{ bubbles: true }}));
                                cleared = true;
                            }}
                        }}

                        if (!cleared) {{
                            var all = document.querySelectorAll('textarea');
                            for (var j = 0; j < all.length; j++) {{
                                if (all[j].offsetParent !== null) {{
                                    all[j].value = '';
                                    all[j].dispatchEvent(new Event('input', {{ bubbles: true }}));
                                    all[j].dispatchEvent(new Event('change', {{ bubbles: true }}));
                                }}
                            }}
                        }}

                        return true;
                    }} catch (e) {{ return false; }}"));

                // The page restores the value asynchronously when it is going to restore it at all,
                // so a read taken in the same breath as the write always looks like success.
                await Task.Delay(250);

                var remaining = await Task.Run(ReadVisibleSourceText);
                if (remaining != null && remaining.Length == 0) return string.Empty;

                Log($"clear attempt {attempt + 1} left text behind");
            }

            return await Task.Run(ReadVisibleSourceText);
        }

        /// <summary>
        /// The contents of the visible source box, with none of <see cref="ReadRecognizedTextAsync"/>'s
        /// fallbacks — an empty answer here has to mean "the box is empty", not "nothing was found".
        /// Null when the page could not be queried at all.
        /// </summary>
        private string ReadVisibleSourceText()
        {
            if (!IsBrowserOpen) return null;

            return TryScriptValue(@"
                try {
                    var all = document.querySelectorAll('textarea');
                    for (var i = 0; i < all.length; i++) {
                        if (all[i].offsetParent !== null && all[i].value && all[i].value.trim())
                            return all[i].value.trim();
                    }
                    return '';
                } catch (e) { return ''; }");
        }

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
            yield return "نوشتار مبدأ";
            yield return "نوشتن متن";
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

    /// <summary>
    /// What a microphone reset actually achieved. Both halves are reported because a reset that
    /// half-worked is worse than one that plainly failed: text left in the source box is read back
    /// as new speech, and a microphone that never came on leaves the session listening to nothing.
    /// </summary>
    public sealed class MicResetResult
    {
        public MicResetResult(bool sourceCleared, string remainingText, bool listening)
        {
            SourceCleared = sourceCleared;
            RemainingText = remainingText ?? string.Empty;
            Listening = listening;
        }

        /// <summary>True only when the source box was <em>confirmed</em> empty afterwards.</summary>
        public bool SourceCleared { get; }

        /// <summary>What the box still holds. Empty when it was cleared or could not be read.</summary>
        public string RemainingText { get; }

        /// <summary>True when the voice button came back on.</summary>
        public bool Listening { get; }
    }
}
