using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Cloudict.Abstractions;

namespace Cloudict.Platform.Linux
{
    /// <summary>
    /// Types on Wayland by driving <c>ydotool</c>.
    ///
    /// <para>Wayland has no protocol for one application to synthesise input into another — that is
    /// a deliberate security boundary, not an omission. The only route left is the kernel's
    /// <c>uinput</c> device, which creates a virtual keyboard the compositor treats as real hardware.
    /// <c>ydotool</c> is the established front-end for that: its daemon holds the <c>uinput</c>
    /// handle (which needs privileges a desktop app should not carry) and the client sends it work
    /// over a socket.</para>
    ///
    /// <para>Cloudict shells out to the client rather than opening <c>uinput</c> itself. Doing it
    /// directly would mean shipping a privileged component and reimplementing the keycode mapping
    /// ydotool already solves; delegating keeps the elevated part outside the application, where the
    /// distribution's own packaging can manage it.</para>
    /// </summary>
    internal sealed class YdotoolTextInjector : ITextInjector
    {
        private readonly string _ydotoolPath;

        public string BackendName => "ydotool";

        public bool IsAvailable { get; private set; }
        public string UnavailableReasonKey { get; private set; }

        public YdotoolTextInjector()
        {
            _ydotoolPath = FindYdotool();
            Refresh();
        }

        public void Refresh()
        {
            if (_ydotoolPath == null)
            {
                IsAvailable = false;
                UnavailableReasonKey = "Platform_Err_LinuxYdotoolMissing";
                return;
            }

            // The client is useless without its daemon: it exits non-zero with a socket error.
            if (!IsDaemonReachable())
            {
                IsAvailable = false;
                UnavailableReasonKey = "Platform_Err_LinuxYdotoolDaemonMissing";
                return;
            }

            IsAvailable = true;
            UnavailableReasonKey = null;
        }

        private static string FindYdotool()
        {
            foreach (var candidate in new[]
            {
                "/usr/bin/ydotool", "/usr/local/bin/ydotool", "/bin/ydotool",
                "/var/lib/flatpak/exports/bin/ydotool"
            })
            {
                if (File.Exists(candidate)) return candidate;
            }

            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'))
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), "ydotool");
                    if (File.Exists(candidate)) return candidate;
                }
                catch (Exception ex) { Debug.WriteLine($"[YdotoolTextInjector] PATH probe: {ex.Message}"); }
            }

            return null;
        }

        /// <summary>Sends a harmless no-op to see whether the daemon socket answers.</summary>
        private bool IsDaemonReachable()
        {
            try
            {
                // "sleep 0" exercises the client/daemon handshake without producing any input.
                return Run(new[] { "sleep", "0" }, out _) == 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[YdotoolTextInjector] daemon probe failed: {ex.Message}");
                return false;
            }
        }

        public void TypeText(string text)
        {
            if (string.IsNullOrEmpty(text) || !IsAvailable) return;

            // ydotool type takes the string as one argument, so Unicode is handled by the daemon and
            // no per-character work is needed here.
            if (Run(new[] { "type", "--", text }, out string error) != 0 && !string.IsNullOrWhiteSpace(error))
                Debug.WriteLine($"[YdotoolTextInjector] type failed: {error}");
        }

        public void SendKey(InjectedKey key)
        {
            int code = LinuxKeyCodes.ToEvdev(key);
            if (code == 0 || !IsAvailable) return;

            Run(new[] { "key", $"{code}:1", $"{code}:0" }, out _);
        }

        public void SendChord(InjectedKey key, KeyModifiers modifiers)
        {
            int code = LinuxKeyCodes.ToEvdev(key);
            if (code == 0 || !IsAvailable) return;

            var sequence = new List<string> { "key" };
            var held = new List<int>();

            if (modifiers.HasFlag(KeyModifiers.Control)) held.Add(LinuxKeyCodes.KEY_LEFTCTRL);
            if (modifiers.HasFlag(KeyModifiers.Alt)) held.Add(LinuxKeyCodes.KEY_LEFTALT);
            if (modifiers.HasFlag(KeyModifiers.Shift)) held.Add(LinuxKeyCodes.KEY_LEFTSHIFT);
            if (modifiers.HasFlag(KeyModifiers.Meta)) held.Add(LinuxKeyCodes.KEY_LEFTMETA);

            foreach (var m in held) sequence.Add($"{m}:1");
            sequence.Add($"{code}:1");
            sequence.Add($"{code}:0");
            for (int i = held.Count - 1; i >= 0; i--) sequence.Add($"{held[i]}:0");

            Run(sequence.ToArray(), out _);
        }

        private int Run(string[] arguments, out string error)
        {
            error = null;

            var psi = new ProcessStartInfo(_ydotoolPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var a in arguments) psi.ArgumentList.Add(a);

            using var process = Process.Start(psi);
            if (process == null) return -1;

            error = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(5000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return -1;
            }

            return process.ExitCode;
        }

        public void Dispose() { }
    }

    /// <summary>
    /// Linux input event codes from <c>linux/input-event-codes.h</c>. These are the kernel's own key
    /// numbers, which is the level <c>uinput</c> works at — unrelated to X11 keycodes or keysyms.
    /// </summary>
    internal static class LinuxKeyCodes
    {
        public const int KEY_LEFTCTRL = 29;
        public const int KEY_LEFTSHIFT = 42;
        public const int KEY_LEFTALT = 56;
        public const int KEY_LEFTMETA = 125;

        private static readonly Dictionary<InjectedKey, int> Map = new Dictionary<InjectedKey, int>
        {
            [InjectedKey.Escape] = 1,
            [InjectedKey.Backspace] = 14,
            [InjectedKey.Tab] = 15,
            [InjectedKey.Enter] = 28,
            [InjectedKey.Space] = 57,
            [InjectedKey.Home] = 102,
            [InjectedKey.Up] = 103,
            [InjectedKey.PageUp] = 104,
            [InjectedKey.Left] = 105,
            [InjectedKey.Right] = 106,
            [InjectedKey.End] = 107,
            [InjectedKey.Down] = 108,
            [InjectedKey.PageDown] = 109,
            [InjectedKey.Insert] = 110,
            [InjectedKey.Delete] = 111,

            [InjectedKey.D1] = 2, [InjectedKey.D2] = 3, [InjectedKey.D3] = 4, [InjectedKey.D4] = 5,
            [InjectedKey.D5] = 6, [InjectedKey.D6] = 7, [InjectedKey.D7] = 8, [InjectedKey.D8] = 9,
            [InjectedKey.D9] = 10, [InjectedKey.D0] = 11,

            [InjectedKey.Q] = 16, [InjectedKey.W] = 17, [InjectedKey.E] = 18, [InjectedKey.R] = 19,
            [InjectedKey.T] = 20, [InjectedKey.Y] = 21, [InjectedKey.U] = 22, [InjectedKey.I] = 23,
            [InjectedKey.O] = 24, [InjectedKey.P] = 25,
            [InjectedKey.A] = 30, [InjectedKey.S] = 31, [InjectedKey.D] = 32, [InjectedKey.F] = 33,
            [InjectedKey.G] = 34, [InjectedKey.H] = 35, [InjectedKey.J] = 36, [InjectedKey.K] = 37,
            [InjectedKey.L] = 38,
            [InjectedKey.Z] = 44, [InjectedKey.X] = 45, [InjectedKey.C] = 46, [InjectedKey.V] = 47,
            [InjectedKey.B] = 48, [InjectedKey.N] = 49, [InjectedKey.M] = 50,

            [InjectedKey.F1] = 59, [InjectedKey.F2] = 60, [InjectedKey.F3] = 61, [InjectedKey.F4] = 62,
            [InjectedKey.F5] = 63, [InjectedKey.F6] = 64, [InjectedKey.F7] = 65, [InjectedKey.F8] = 66,
            [InjectedKey.F9] = 67, [InjectedKey.F10] = 68, [InjectedKey.F11] = 87, [InjectedKey.F12] = 88,
        };

        public static int ToEvdev(InjectedKey key) => Map.TryGetValue(key, out int code) ? code : 0;
    }
}
