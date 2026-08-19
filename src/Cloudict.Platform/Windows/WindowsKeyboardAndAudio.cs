using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Cloudict.Abstractions;

namespace Cloudict.Platform.Windows
{
    /// <summary>
    /// Switches the active keyboard layout, used by the "switch to Persian / English" voice
    /// commands. Loads the layout for a language and asks the foreground window to adopt it, which
    /// is what makes the change visible in the application the user is actually typing into.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class WindowsKeyboardLayout : IKeyboardLayout
    {
        private const uint KLF_ACTIVATE = 0x00000001;
        private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
        private const uint INPUTLANGCHANGE_SYSCHARSET = 0x0001;

        public bool IsSupported => true;

        public bool TrySwitchTo(string languageCode)
        {
            var klid = ToKeyboardLayoutId(languageCode);
            if (klid == null) return false;

            try
            {
                IntPtr hkl = LoadKeyboardLayout(klid, KLF_ACTIVATE);
                if (hkl == IntPtr.Zero) return false;

                ActivateKeyboardLayout(hkl, 0);

                // Activating only affects this thread; the foreground window has to be told too.
                var foreground = GetForegroundWindow();
                if (foreground != IntPtr.Zero)
                    PostMessage(foreground, WM_INPUTLANGCHANGEREQUEST, new IntPtr(INPUTLANGCHANGE_SYSCHARSET), hkl);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsKeyboardLayout] switch to '{languageCode}' failed: {ex.Message}");
                return false;
            }
        }

        public string GetCurrentLanguage()
        {
            try
            {
                var foreground = GetForegroundWindow();
                uint threadId = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
                IntPtr hkl = GetKeyboardLayout(threadId);

                // The low word of HKL is the language identifier.
                int langId = hkl.ToInt32() & 0xFFFF;
                return new CultureInfo(langId).TwoLetterISOLanguageName;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsKeyboardLayout] cannot read current layout: {ex.Message}");
                return null;
            }
        }

        /// <summary>Maps a two-letter language code to a Windows keyboard layout identifier.</summary>
        private static string ToKeyboardLayoutId(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode)) return null;

            var code = languageCode.Trim().ToLowerInvariant();
            if (code.Contains('-')) code = code.Split('-')[0];

            switch (code)
            {
                case "fa": return "00000429";   // Persian
                case "en": return "00000409";   // English (US)
                case "ar": return "00000401";   // Arabic
                case "de": return "00000407";
                case "fr": return "0000040C";
                case "es": return "0000040A";
                case "ru": return "00000419";
                case "tr": return "0000041F";
                default: return null;
            }
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint threadId);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadKeyboardLayout(string klid, uint flags);
        [DllImport("user32.dll")] private static extern bool ActivateKeyboardLayout(IntPtr hkl, uint flags);
    }

    /// <summary>
    /// Reports whether any application is currently capturing the microphone, via WASAPI. Drives
    /// the desktop status light only, so failure here is cosmetic and degrades to "unknown".
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class WindowsMicrophoneMonitor : IMicrophoneMonitor
    {
        public bool IsSupported => true;

        public bool IsMicrophoneInUse()
        {
            try
            {
                using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                using var device = enumerator.GetDefaultAudioEndpoint(
                    NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Console);

                if (device == null || device.State != NAudio.CoreAudioApi.DeviceState.Active) return false;

                // A muted microphone is not capturing anything, whatever the sessions claim.
                if (device.AudioEndpointVolume.Mute) return false;

                var sessions = device.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    try
                    {
                        var session = sessions[i];

                        // The system-sounds session is always present and never means the user's
                        // microphone is live.
                        if (session.IsSystemSoundsSession) continue;

                        if (session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive &&
                            session.GetProcessID != 0)
                            return true;
                    }
                    catch (Exception sessionEx)
                    {
                        // Sessions disappear while being enumerated; skip and keep looking.
                        Debug.WriteLine($"[WindowsMicrophoneMonitor] session {i}: {sessionEx.Message}");
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsMicrophoneMonitor] probe failed: {ex.Message}");
                return false;
            }
        }

        public void Dispose() { }
    }
}
