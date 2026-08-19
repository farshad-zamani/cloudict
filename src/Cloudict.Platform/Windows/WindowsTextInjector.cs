using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Cloudict.Abstractions;

namespace Cloudict.Platform.Windows
{
    /// <summary>
    /// Types into the focused window using <c>SendInput</c>.
    ///
    /// <para>Text is sent with <c>KEYEVENTF_UNICODE</c>, which delivers the character itself rather
    /// than a scan code. That is what lets Cloudict type Persian and Arabic into an application
    /// while the user's keyboard layout is still English — the layout is bypassed entirely.</para>
    ///
    /// <para>This is the proven implementation carried over from the 2.x <c>MainWindow</c>, moved
    /// behind <see cref="ITextInjector"/> without behavioural change: same per-character pacing,
    /// same tolerance for a failed <c>SendInput</c>.</para>
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class WindowsTextInjector : ITextInjector
    {
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;

        /// <summary>
        /// Pause between characters. Some applications (notably Electron and Java UIs) drop input
        /// delivered faster than this.
        /// </summary>
        private const int PerCharacterDelayMs = 5;

        public string BackendName => "sendinput";

        public bool IsAvailable => true;

        /// <summary>
        /// Windows never refuses injection outright for a same-or-lower integrity target, and the
        /// app already requests elevation, so there is no reason to report here.
        /// </summary>
        public string UnavailableReasonKey => null;

        public void Refresh() { /* nothing to re-check on Windows */ }

        public void TypeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            foreach (char c in text)
            {
                // Control characters other than the whitespace we care about would be typed as
                // garbage; newline and tab are handled as real keys below.
                if (char.IsControl(c) && c != ' ' && c != '\t' && c != '\n' && c != '\r')
                    continue;

                if (c == '\n' || c == '\r') { SendKey(InjectedKey.Enter); continue; }
                if (c == '\t') { SendKey(InjectedKey.Tab); continue; }

                SendUnicodeChar(c);
                Thread.Sleep(PerCharacterDelayMs);
            }
        }

        public void SendKey(InjectedKey key)
        {
            ushort vk = VirtualKeys.ToVirtualKey(key);
            if (vk == 0) return;

            SendVirtualKey(vk, keyUp: false);
            SendVirtualKey(vk, keyUp: true);
        }

        public void SendChord(InjectedKey key, KeyModifiers modifiers)
        {
            ushort vk = VirtualKeys.ToVirtualKey(key);
            if (vk == 0) return;

            var held = new List<ushort>();
            if (modifiers.HasFlag(KeyModifiers.Control)) held.Add(VirtualKeys.VK_CONTROL);
            if (modifiers.HasFlag(KeyModifiers.Alt)) held.Add(VirtualKeys.VK_MENU);
            if (modifiers.HasFlag(KeyModifiers.Shift)) held.Add(VirtualKeys.VK_SHIFT);
            if (modifiers.HasFlag(KeyModifiers.Meta)) held.Add(VirtualKeys.VK_LWIN);

            foreach (var m in held) SendVirtualKey(m, keyUp: false);
            SendVirtualKey(vk, keyUp: false);
            SendVirtualKey(vk, keyUp: true);

            // Release in reverse order so the modifier state unwinds cleanly.
            for (int i = held.Count - 1; i >= 0; i--) SendVirtualKey(held[i], keyUp: true);
        }

        private static void SendUnicodeChar(char c)
        {
            var inputs = new[]
            {
                MakeInput(0, c, KEYEVENTF_UNICODE),
                MakeInput(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP)
            };

            if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == 0)
                Debug.WriteLine($"[WindowsTextInjector] SendInput failed for '{c}' (error {Marshal.GetLastWin32Error()})");
        }

        private static void SendVirtualKey(ushort virtualKey, bool keyUp)
        {
            var input = MakeInput(virtualKey, '\0', keyUp ? KEYEVENTF_KEYUP : 0);
            if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 0)
                Debug.WriteLine($"[WindowsTextInjector] SendInput failed for VK 0x{virtualKey:X} (error {Marshal.GetLastWin32Error()})");
        }

        private static INPUT MakeInput(ushort vk, char scan, uint flags) => new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = scan,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = GetMessageExtraInfo()
                }
            }
        };

        public void Dispose() { }

        #region Win32

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL, wParamH;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern IntPtr GetMessageExtraInfo();

        #endregion
    }

    /// <summary>Maps <see cref="InjectedKey"/> onto Windows virtual-key codes.</summary>
    [SupportedOSPlatform("windows")]
    internal static class VirtualKeys
    {
        public const ushort VK_SHIFT = 0x10;
        public const ushort VK_CONTROL = 0x11;
        public const ushort VK_MENU = 0x12;   // Alt
        public const ushort VK_LWIN = 0x5B;

        public static ushort ToVirtualKey(InjectedKey key)
        {
            if (key >= InjectedKey.A && key <= InjectedKey.Z)
                return (ushort)('A' + (key - InjectedKey.A));

            if (key >= InjectedKey.D0 && key <= InjectedKey.D9)
                return (ushort)('0' + (key - InjectedKey.D0));

            if (key >= InjectedKey.F1 && key <= InjectedKey.F12)
                return (ushort)(0x70 + (key - InjectedKey.F1));

            switch (key)
            {
                case InjectedKey.Enter: return 0x0D;
                case InjectedKey.Tab: return 0x09;
                case InjectedKey.Space: return 0x20;
                case InjectedKey.Backspace: return 0x08;
                case InjectedKey.Delete: return 0x2E;
                case InjectedKey.Escape: return 0x1B;
                case InjectedKey.Insert: return 0x2D;
                case InjectedKey.Home: return 0x24;
                case InjectedKey.End: return 0x23;
                case InjectedKey.PageUp: return 0x21;
                case InjectedKey.PageDown: return 0x22;
                case InjectedKey.Up: return 0x26;
                case InjectedKey.Down: return 0x28;
                case InjectedKey.Left: return 0x25;
                case InjectedKey.Right: return 0x27;
                default: return 0;
            }
        }
    }
}
