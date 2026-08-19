using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading;
using Cloudict.Abstractions;

namespace Cloudict.Platform.MacOS
{
    /// <summary>
    /// Types into the focused application on macOS using Quartz event services.
    ///
    /// <para>Unlike X11, macOS needs no scratch-keycode trick: a keyboard event can carry literal
    /// UTF-16 text via <c>CGEventKeyboardSetUnicodeString</c>, and the system delivers exactly those
    /// characters regardless of the active input source. That is the natural way to type Persian
    /// while the user's input source is English.</para>
    ///
    /// <para>The real obstacle is permission. macOS refuses to post synthetic events until the user
    /// grants Accessibility in System Settings, and an application cannot request it — it can only
    /// ask the system to open the pane. Worse, a denied post fails silently: the call succeeds and
    /// nothing appears. Availability is therefore checked up front and re-checked on
    /// <see cref="Refresh"/>, so the UI can explain the situation instead of looking broken.</para>
    /// </summary>
    [SupportedOSPlatform("macos")]
    internal sealed class MacTextInjector : ITextInjector
    {
        /// <summary>
        /// Pause between characters. macOS coalesces events posted faster than this, and some
        /// applications drop them entirely.
        /// </summary>
        private const int PerCharacterDelayMs = 5;

        private readonly object _gate = new object();

        public string BackendName => "cgevent";

        public bool IsAvailable { get; private set; }
        public string UnavailableReasonKey { get; private set; }

        public MacTextInjector() => Refresh();

        public void Refresh()
        {
            try
            {
                if (MacInterop.AXIsProcessTrusted())
                {
                    IsAvailable = true;
                    UnavailableReasonKey = null;
                }
                else
                {
                    IsAvailable = false;
                    UnavailableReasonKey = "Platform_Err_MacAccessibilityDenied";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MacTextInjector] permission check failed: {ex.Message}");
                IsAvailable = false;
                UnavailableReasonKey = "Platform_Err_MacInjectionUnavailable";
            }
        }

        public void TypeText(string text)
        {
            if (string.IsNullOrEmpty(text) || !IsAvailable) return;

            lock (_gate)
            {
                foreach (char c in text)
                {
                    if (char.IsControl(c) && c != ' ' && c != '\t' && c != '\n' && c != '\r')
                        continue;

                    if (c == '\n' || c == '\r') { Tap(MacVirtualKeys.Return, 0); continue; }
                    if (c == '\t') { Tap(MacVirtualKeys.Tab, 0); continue; }

                    TypeCharacter(c);
                    Thread.Sleep(PerCharacterDelayMs);
                }
            }
        }

        /// <summary>
        /// Posts a key event whose payload is the character itself. The virtual key is left at 0
        /// because the text, not the key position, is what the receiving application reads.
        /// </summary>
        private static void TypeCharacter(char c)
        {
            IntPtr down = IntPtr.Zero, up = IntPtr.Zero;

            try
            {
                down = MacInterop.CGEventCreateKeyboardEvent(IntPtr.Zero, 0, true);
                up = MacInterop.CGEventCreateKeyboardEvent(IntPtr.Zero, 0, false);
                if (down == IntPtr.Zero || up == IntPtr.Zero) return;

                var s = c.ToString();
                MacInterop.CGEventKeyboardSetUnicodeString(down, s.Length, s);
                MacInterop.CGEventKeyboardSetUnicodeString(up, s.Length, s);

                MacInterop.CGEventPost(MacInterop.kCGHIDEventTap, down);
                MacInterop.CGEventPost(MacInterop.kCGHIDEventTap, up);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MacTextInjector] could not type '{c}': {ex.Message}");
            }
            finally
            {
                if (down != IntPtr.Zero) MacInterop.CFRelease(down);
                if (up != IntPtr.Zero) MacInterop.CFRelease(up);
            }
        }

        public void SendKey(InjectedKey key)
        {
            if (!IsAvailable) return;

            ushort vk = MacVirtualKeys.ToVirtualKey(key);
            if (vk == MacVirtualKeys.None) return;

            lock (_gate) Tap(vk, 0);
        }

        public void SendChord(InjectedKey key, KeyModifiers modifiers)
        {
            if (!IsAvailable) return;

            ushort vk = MacVirtualKeys.ToVirtualKey(key);
            if (vk == MacVirtualKeys.None) return;

            ulong flags = 0;
            if (modifiers.HasFlag(KeyModifiers.Control)) flags |= MacInterop.kCGEventFlagMaskControl;
            if (modifiers.HasFlag(KeyModifiers.Alt)) flags |= MacInterop.kCGEventFlagMaskAlternate;
            if (modifiers.HasFlag(KeyModifiers.Shift)) flags |= MacInterop.kCGEventFlagMaskShift;
            if (modifiers.HasFlag(KeyModifiers.Meta)) flags |= MacInterop.kCGEventFlagMaskCommand;

            lock (_gate) Tap(vk, flags);
        }

        /// <summary>
        /// Presses and releases a real key. Modifiers ride along as event flags rather than as
        /// separate key events, which is how Quartz expects a combination to be expressed.
        /// </summary>
        private static void Tap(ushort virtualKey, ulong flags)
        {
            IntPtr down = IntPtr.Zero, up = IntPtr.Zero;

            try
            {
                down = MacInterop.CGEventCreateKeyboardEvent(IntPtr.Zero, virtualKey, true);
                up = MacInterop.CGEventCreateKeyboardEvent(IntPtr.Zero, virtualKey, false);
                if (down == IntPtr.Zero || up == IntPtr.Zero) return;

                if (flags != 0)
                {
                    MacInterop.CGEventSetFlags(down, flags);
                    MacInterop.CGEventSetFlags(up, flags);
                }

                MacInterop.CGEventPost(MacInterop.kCGHIDEventTap, down);
                MacInterop.CGEventPost(MacInterop.kCGHIDEventTap, up);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MacTextInjector] could not press key 0x{virtualKey:X}: {ex.Message}");
            }
            finally
            {
                if (down != IntPtr.Zero) MacInterop.CFRelease(down);
                if (up != IntPtr.Zero) MacInterop.CFRelease(up);
            }
        }

        public void Dispose() { }
    }

    /// <summary>
    /// macOS virtual key codes (<c>HIToolbox/Events.h</c>). These describe physical key positions on
    /// an ANSI layout and are unrelated to characters, which is why literal text is sent separately.
    /// </summary>
    internal static class MacVirtualKeys
    {
        public const ushort None = 0xFFFF;

        public const ushort Return = 0x24;
        public const ushort Tab = 0x30;
        public const ushort Space = 0x31;
        public const ushort Delete = 0x33;         // Backspace
        public const ushort ForwardDelete = 0x75;
        public const ushort Escape = 0x35;

        private static readonly Dictionary<InjectedKey, ushort> Map = new Dictionary<InjectedKey, ushort>
        {
            [InjectedKey.Enter] = Return,
            [InjectedKey.Tab] = Tab,
            [InjectedKey.Space] = Space,
            [InjectedKey.Backspace] = Delete,
            [InjectedKey.Delete] = ForwardDelete,
            [InjectedKey.Escape] = Escape,
            [InjectedKey.Home] = 0x73,
            [InjectedKey.End] = 0x77,
            [InjectedKey.PageUp] = 0x74,
            [InjectedKey.PageDown] = 0x79,
            [InjectedKey.Left] = 0x7B,
            [InjectedKey.Right] = 0x7C,
            [InjectedKey.Down] = 0x7D,
            [InjectedKey.Up] = 0x7E,
            [InjectedKey.Insert] = 0x72,           // Help key position

            [InjectedKey.A] = 0x00, [InjectedKey.B] = 0x0B, [InjectedKey.C] = 0x08, [InjectedKey.D] = 0x02,
            [InjectedKey.E] = 0x0E, [InjectedKey.F] = 0x03, [InjectedKey.G] = 0x05, [InjectedKey.H] = 0x04,
            [InjectedKey.I] = 0x22, [InjectedKey.J] = 0x26, [InjectedKey.K] = 0x28, [InjectedKey.L] = 0x25,
            [InjectedKey.M] = 0x2E, [InjectedKey.N] = 0x2D, [InjectedKey.O] = 0x1F, [InjectedKey.P] = 0x23,
            [InjectedKey.Q] = 0x0C, [InjectedKey.R] = 0x0F, [InjectedKey.S] = 0x01, [InjectedKey.T] = 0x11,
            [InjectedKey.U] = 0x20, [InjectedKey.V] = 0x09, [InjectedKey.W] = 0x0D, [InjectedKey.X] = 0x07,
            [InjectedKey.Y] = 0x10, [InjectedKey.Z] = 0x06,

            [InjectedKey.D0] = 0x1D, [InjectedKey.D1] = 0x12, [InjectedKey.D2] = 0x13, [InjectedKey.D3] = 0x14,
            [InjectedKey.D4] = 0x15, [InjectedKey.D5] = 0x17, [InjectedKey.D6] = 0x16, [InjectedKey.D7] = 0x1A,
            [InjectedKey.D8] = 0x1C, [InjectedKey.D9] = 0x19,

            [InjectedKey.F1] = 0x7A, [InjectedKey.F2] = 0x78, [InjectedKey.F3] = 0x63, [InjectedKey.F4] = 0x76,
            [InjectedKey.F5] = 0x60, [InjectedKey.F6] = 0x61, [InjectedKey.F7] = 0x62, [InjectedKey.F8] = 0x64,
            [InjectedKey.F9] = 0x65, [InjectedKey.F10] = 0x6D, [InjectedKey.F11] = 0x67, [InjectedKey.F12] = 0x6F,
        };

        public static ushort ToVirtualKey(InjectedKey key) => Map.TryGetValue(key, out var vk) ? vk : None;
    }
}
