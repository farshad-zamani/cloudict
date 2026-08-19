using System;
using System.Diagnostics;
using System.Threading;
using Cloudict.Abstractions;
using Cloudict.Services;

namespace Cloudict
{
    /// <summary>
    /// Executes the system actions a voice command can trigger: typing text, pressing keys and
    /// switching the keyboard layout.
    ///
    /// <para>All the <c>user32</c> P/Invoke and the H.InputSimulator dependency that used to live
    /// here have moved into <c>Cloudict.Platform</c>, and the key-name parsing into
    /// <see cref="KeyCommandParser"/> where it is unit-tested. What remains is a small adapter, kept
    /// so the windows that call it did not need rewriting during the restructure.</para>
    /// </summary>
    public class SystemCommandExecutor
    {
        private readonly ITextInjector _injector;
        private readonly IKeyboardLayout _keyboardLayout;

        public SystemCommandExecutor()
        {
            _injector = AppServices.Platform.TextInjector;
            _keyboardLayout = AppServices.Platform.KeyboardLayout;
        }

        /// <summary>Types literal text into whatever window has focus.</summary>
        public void TypeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            try { _injector.TypeText(text); }
            catch (Exception ex) { Debug.WriteLine($"[SystemCommandExecutor] typing failed: {ex.Message}"); }
        }

        /// <summary>
        /// Presses the key or combination named by a voice command's action value — "Enter",
        /// "{BACKSPACE}", "Ctrl+Backspace", "copy", "اینتر" and so on.
        /// </summary>
        public void ExecuteKeyCommand(string keyCommand)
        {
            try
            {
                if (!KeyCommandParser.TryParse(keyCommand, out var parsed))
                {
                    Debug.WriteLine($"[SystemCommandExecutor] unrecognized key command '{keyCommand}'");
                    return;
                }

                if (parsed.Modifiers == KeyModifiers.None) _injector.SendKey(parsed.Key);
                else _injector.SendChord(parsed.Key, parsed.Modifiers);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemCommandExecutor] key command '{keyCommand}' failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Switches the system keyboard layout. Accepts either a bare language code ("fa") or a
        /// full culture name ("fa-IR"), because both spellings exist in saved settings.
        /// </summary>
        public void ChangeLanguage(string languageCode)
        {
            try
            {
                if (!_keyboardLayout.IsSupported)
                {
                    Debug.WriteLine("[SystemCommandExecutor] keyboard layout switching is not available on this platform");
                    return;
                }

                _keyboardLayout.TrySwitchTo(languageCode);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemCommandExecutor] language switch to '{languageCode}' failed: {ex.Message}");
            }
        }

        /// <summary>The active layout's two-letter language code, or null when it cannot be read.</summary>
        public string GetCurrentKeyboardLanguage() => _keyboardLayout.GetCurrentLanguage();

        /// <summary>Small pause used between chained command actions.</summary>
        public void Wait(int milliseconds = 100) => Thread.Sleep(milliseconds);
    }
}
