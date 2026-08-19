using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Cloudict.Abstractions;

namespace Cloudict.Platform.Linux
{
    /// <summary>
    /// Types into the focused window on X11 using the XTEST extension.
    ///
    /// <para><b>Why a scratch keycode.</b> XTEST can only synthesise <em>keycodes</em> — physical key
    /// positions — not characters. A keycode's meaning comes from the user's keyboard layout, so
    /// typing "س" while the layout is US English is impossible by that route: no key produces it.
    /// The technique used here (the same one xdotool uses) is to find a keycode the layout leaves
    /// unused, temporarily point it at the keysym we want, press it, and put it back. That makes the
    /// injector independent of whatever layout the user has, which is essential for an app whose
    /// whole purpose is typing Persian while the keyboard is in another language.</para>
    ///
    /// <para>All four keysym slots of the scratch key are set to the same value so no modifier state
    /// can change which character comes out.</para>
    /// </summary>
    internal sealed class X11TextInjector : ITextInjector
    {
        /// <summary>Pause between characters, matching the Windows injector's pacing.</summary>
        private const int PerCharacterDelayMs = 5;

        private readonly object _gate = new object();

        private IntPtr _display;
        private byte _scratchKeycode;
        private int _keysymsPerKeycode;
        private bool _disposed;

        public string BackendName => "xtest";

        public bool IsAvailable { get; private set; }
        public string UnavailableReasonKey { get; private set; }

        public X11TextInjector()
        {
            Refresh();
        }

        public void Refresh()
        {
            lock (_gate)
            {
                Close();

                try
                {
                    _display = X11Interop.XOpenDisplay(null);
                    if (_display == IntPtr.Zero)
                    {
                        Fail("Platform_Err_LinuxNoDisplay");
                        return;
                    }

                    if (!X11Interop.XTestQueryExtension(_display, out _, out _, out _, out _))
                    {
                        Fail("Platform_Err_LinuxNoXTest");
                        return;
                    }

                    if (!X11Keyboard.TryFindSpareKeycode(_display, out _scratchKeycode, out _keysymsPerKeycode))
                    {
                        Fail("Platform_Err_LinuxNoSpareKeycode");
                        return;
                    }

                    IsAvailable = true;
                    UnavailableReasonKey = null;
                }
                catch (DllNotFoundException)
                {
                    // libX11/libXtst absent — a headless or minimal system.
                    Fail("Platform_Err_LinuxX11LibsMissing");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[X11TextInjector] init failed: {ex.Message}");
                    Fail("Platform_Err_LinuxInjectionUnavailable");
                }
            }
        }

        private void Fail(string reasonKey)
        {
            IsAvailable = false;
            UnavailableReasonKey = reasonKey;
            Close();
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

                    if (c == '\n' || c == '\r') { PressNamedKey(X11Interop.XK_Return); continue; }
                    if (c == '\t') { PressNamedKey(X11Interop.XK_Tab); continue; }

                    PressCharacter(X11Interop.KeysymForChar(c));
                    Thread.Sleep(PerCharacterDelayMs);
                }

                ReleaseScratch();
            }
        }

        public void SendKey(InjectedKey key)
        {
            if (!IsAvailable) return;

            var keysym = X11Interop.KeysymForKey(key);
            if (keysym == 0) return;

            lock (_gate)
            {
                PressNamedKey(keysym);
                ReleaseScratch();
            }
        }

        public void SendChord(InjectedKey key, KeyModifiers modifiers)
        {
            if (!IsAvailable) return;

            var keysym = X11Interop.KeysymForKey(key);
            if (keysym == 0) return;

            lock (_gate)
            {
                var held = new List<byte>();
                void Hold(nint modifierKeysym)
                {
                    byte code = ResolveModifier(modifierKeysym);
                    if (code != 0) held.Add(code);
                }

                if (modifiers.HasFlag(KeyModifiers.Control)) Hold(X11Interop.XK_Control_L);
                if (modifiers.HasFlag(KeyModifiers.Alt)) Hold(X11Interop.XK_Alt_L);
                if (modifiers.HasFlag(KeyModifiers.Shift)) Hold(X11Interop.XK_Shift_L);
                if (modifiers.HasFlag(KeyModifiers.Meta)) Hold(X11Interop.XK_Super_L);

                foreach (var code in held) X11Interop.XTestFakeKeyEvent(_display, code, true, 0);

                PressNamedKey(keysym);

                // Release in reverse so the modifier state unwinds cleanly.
                for (int i = held.Count - 1; i >= 0; i--)
                    X11Interop.XTestFakeKeyEvent(_display, held[i], false, 0);

                X11Interop.XSync(_display, false);
                ReleaseScratch();
            }
        }

        /// <summary>
        /// Types one character, <b>always</b> through the scratch keycode.
        ///
        /// <para>Reusing the layout's own keycode is tempting but wrong here. A keycode carries
        /// several keysyms — the <c>c</c> key holds both <c>c</c> and <c>C</c> — and which one
        /// arrives depends on the modifier state, so pressing it plainly turns every capital into a
        /// lowercase letter. Going through the scratch key, whose slots all hold the same keysym,
        /// makes the character that arrives independent of both the layout and the modifier state.</para>
        /// </summary>
        private void PressCharacter(nint keysym)
        {
            if (!MapScratch(keysym)) return;
            Tap(_scratchKeycode);
        }

        /// <summary>
        /// Presses a named key such as Enter or Left. These live at a fixed keycode with no shifted
        /// alternative, so the layout's own mapping is used when present; the scratch key covers
        /// exotic layouts that omit one.
        /// </summary>
        private void PressNamedKey(nint keysym)
        {
            if (X11Keyboard.TryFindKeycode(_display, keysym, out byte keycode, out int shiftLevel))
            {
                if (shiftLevel == 1)
                {
                    // The keysym only exists in the shifted slot, so Shift has to be held for it.
                    byte shift = ResolveModifier(X11Interop.XK_Shift_L);
                    if (shift != 0)
                    {
                        X11Interop.XTestFakeKeyEvent(_display, shift, true, 0);
                        Tap(keycode);
                        X11Interop.XTestFakeKeyEvent(_display, shift, false, 0);
                        X11Interop.XSync(_display, false);
                        return;
                    }
                }

                Tap(keycode);
                return;
            }

            if (MapScratch(keysym)) Tap(_scratchKeycode);
        }

        /// <summary>Resolves a modifier keysym to its keycode, or 0 when the layout lacks it.</summary>
        private byte ResolveModifier(nint keysym) =>
            X11Keyboard.TryFindKeycode(_display, keysym, out byte code, out _) ? code : (byte)0;

        private void Tap(byte keycode)
        {
            X11Interop.XTestFakeKeyEvent(_display, keycode, true, 0);
            X11Interop.XTestFakeKeyEvent(_display, keycode, false, 0);
            X11Interop.XSync(_display, false);
        }

        /// <summary>
        /// Points the scratch keycode at <paramref name="keysym"/> in every modifier slot, so the
        /// character produced does not depend on whether Shift or AltGr happens to be down.
        /// </summary>
        private bool MapScratch(nint keysym)
        {
            if (_scratchKeycode == 0 || _keysymsPerKeycode <= 0) return false;

            var keysyms = new nint[_keysymsPerKeycode];
            for (int i = 0; i < keysyms.Length; i++) keysyms[i] = keysym;

            X11Interop.XChangeKeyboardMapping(_display, _scratchKeycode, _keysymsPerKeycode, keysyms, 1);

            // The server must have applied the new mapping before the key event is sent, or the
            // event is interpreted against the old one and the wrong character appears.
            X11Interop.XSync(_display, false);
            return true;
        }

        /// <summary>Returns the scratch keycode to unused, so the user's layout is left as we found it.</summary>
        private void ReleaseScratch()
        {
            if (_scratchKeycode == 0 || _keysymsPerKeycode <= 0 || _display == IntPtr.Zero) return;

            try
            {
                var cleared = new nint[_keysymsPerKeycode];
                X11Interop.XChangeKeyboardMapping(_display, _scratchKeycode, _keysymsPerKeycode, cleared, 1);
                X11Interop.XSync(_display, false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[X11TextInjector] could not restore scratch keycode: {ex.Message}");
            }
        }

        private void Close()
        {
            if (_display == IntPtr.Zero) return;

            try
            {
                ReleaseScratch();
                X11Interop.XCloseDisplay(_display);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[X11TextInjector] close failed: {ex.Message}");
            }
            finally
            {
                _display = IntPtr.Zero;
                _scratchKeycode = 0;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (_gate)
            {
                IsAvailable = false;
                Close();
            }
        }
    }
}
