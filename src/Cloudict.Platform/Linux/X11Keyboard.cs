using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Cloudict.Platform.Linux
{
    /// <summary>
    /// Keyboard-map queries done by reading the server's mapping table directly.
    ///
    /// <para>Xlib's <c>XKeysymToKeycode</c> looked like the obvious way to turn a keysym into a key,
    /// but it returned 0 for ordinary letters on a freshly opened connection here — it depends on
    /// Xlib's per-connection keymap cache, which is populated lazily and refreshed only when the
    /// client handles <c>MappingNotify</c>. Cloudict's hotkey listener does neither, so the lookup
    /// silently failed and no shortcut could ever be grabbed. The text injector hid the same failure
    /// by falling back to its scratch key.</para>
    ///
    /// <para>Fetching the table with <c>XGetKeyboardMapping</c> and scanning it is a few more lines
    /// and has no hidden state: what it reports is what the server currently has.</para>
    /// </summary>
    internal static class X11Keyboard
    {
        /// <summary>
        /// Finds the key that produces <paramref name="keysym"/>.
        /// </summary>
        /// <param name="shiftLevel">
        /// Which slot of that key the keysym sits in: 0 is unmodified, 1 needs Shift. Callers that
        /// press the key must apply this or a capital letter arrives as lowercase.
        /// </param>
        public static bool TryFindKeycode(IntPtr display, nint keysym, out byte keycode, out int shiftLevel)
        {
            keycode = 0;
            shiftLevel = 0;

            if (display == IntPtr.Zero || keysym == 0) return false;

            X11Interop.XDisplayKeycodes(display, out int min, out int max);
            int count = max - min + 1;
            if (count <= 0) return false;

            IntPtr mapping = X11Interop.XGetKeyboardMapping(display, (byte)min, count, out int perKeycode);
            if (mapping == IntPtr.Zero || perKeycode <= 0) return false;

            try
            {
                for (int i = 0; i < count; i++)
                {
                    for (int slot = 0; slot < perKeycode; slot++)
                    {
                        var entry = Marshal.ReadIntPtr(mapping, (i * perKeycode + slot) * IntPtr.Size);
                        if ((nint)entry != keysym) continue;

                        keycode = (byte)(min + i);

                        // Slots alternate unshifted/shifted within each group of two.
                        shiftLevel = slot % 2;
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[X11Keyboard] mapping scan failed: {ex.Message}");
                return false;
            }
            finally
            {
                X11Interop.XFree(mapping);
            }
        }

        /// <summary>Finds a keycode the layout leaves entirely unused, for temporary remapping.</summary>
        public static bool TryFindSpareKeycode(IntPtr display, out byte keycode, out int keysymsPerKeycode)
        {
            keycode = 0;
            keysymsPerKeycode = 0;

            X11Interop.XDisplayKeycodes(display, out int min, out int max);
            int count = max - min + 1;
            if (count <= 0) return false;

            IntPtr mapping = X11Interop.XGetKeyboardMapping(display, (byte)min, count, out keysymsPerKeycode);
            if (mapping == IntPtr.Zero || keysymsPerKeycode <= 0) return false;

            try
            {
                // Search downwards: high keycodes are the ones layouts are least likely to claim.
                for (int i = count - 1; i >= 0; i--)
                {
                    bool free = true;
                    for (int slot = 0; slot < keysymsPerKeycode; slot++)
                    {
                        if (Marshal.ReadIntPtr(mapping, (i * keysymsPerKeycode + slot) * IntPtr.Size) != IntPtr.Zero)
                        {
                            free = false;
                            break;
                        }
                    }

                    if (free)
                    {
                        keycode = (byte)(min + i);
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                X11Interop.XFree(mapping);
            }
        }
    }
}
