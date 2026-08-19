using System;
using System.Runtime.InteropServices;
using Cloudict.Abstractions;

namespace Cloudict.Platform.Linux
{
    /// <summary>
    /// Bindings for the two X11 client libraries Cloudict needs: <c>libX11</c> for the keyboard map
    /// and <c>libXtst</c> for the XTEST extension that synthesises key events.
    ///
    /// <para>Both are loaded by soname rather than plain <c>libX11.so</c>, because the unversioned
    /// symlink only exists when the development packages are installed — on a normal desktop it is
    /// absent and the plain name would fail to resolve.</para>
    /// </summary>
    internal static class X11Interop
    {
        private const string LibX11 = "libX11.so.6";
        private const string LibXtst = "libXtst.so.6";

        [DllImport(LibX11)] public static extern IntPtr XOpenDisplay(string display);
        [DllImport(LibX11)] public static extern int XCloseDisplay(IntPtr display);
        [DllImport(LibX11)] public static extern int XFlush(IntPtr display);
        [DllImport(LibX11)] public static extern int XSync(IntPtr display, bool discard);
        [DllImport(LibX11)] public static extern int XFree(IntPtr data);

        [DllImport(LibX11)] public static extern byte XKeysymToKeycode(IntPtr display, nint keysym);
        [DllImport(LibX11)] public static extern int XDisplayKeycodes(IntPtr display, out int minKeycode, out int maxKeycode);

        /// <summary>Returns a KeySym array of <c>count * keysymsPerKeycode</c> entries; free it with <see cref="XFree"/>.</summary>
        [DllImport(LibX11)]
        public static extern IntPtr XGetKeyboardMapping(IntPtr display, byte firstKeycode, int keycodeCount, out int keysymsPerKeycode);

        [DllImport(LibX11)]
        public static extern int XChangeKeyboardMapping(IntPtr display, int firstKeycode, int keysymsPerKeycode, nint[] keysyms, int numCodes);

        /// <summary>Synthesises a key press or release at the server, as though the hardware had produced it.</summary>
        [DllImport(LibXtst)]
        public static extern int XTestFakeKeyEvent(IntPtr display, uint keycode, bool isPress, ulong delay);

        [DllImport(LibXtst)]
        public static extern bool XTestQueryExtension(IntPtr display, out int eventBase, out int errorBase, out int majorVersion, out int minorVersion);

        #region Global shortcuts

        [DllImport(LibX11)] public static extern IntPtr XDefaultRootWindow(IntPtr display);
        [DllImport(LibX11)] public static extern int XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow, bool ownerEvents, int pointerMode, int keyboardMode);
        [DllImport(LibX11)] public static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow);
        [DllImport(LibX11)] public static extern int XSelectInput(IntPtr display, IntPtr window, long eventMask);
        [DllImport(LibX11)] public static extern int XNextEvent(IntPtr display, byte[] eventReturn);
        [DllImport(LibX11)] public static extern int XPending(IntPtr display);

        public delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

        [DllImport(LibX11)] public static extern IntPtr XSetErrorHandler(XErrorHandler handler);

        public const int GrabModeSync = 0;
        public const int GrabModeAsync = 1;
        public const long KeyPressMask = 1L << 0;

        public const uint ShiftMask = 1 << 0;
        public const uint LockMask = 1 << 1;   // Caps Lock
        public const uint ControlMask = 1 << 2;
        public const uint Mod1Mask = 1 << 3;   // Alt
        public const uint Mod2Mask = 1 << 4;   // Num Lock
        public const uint Mod4Mask = 1 << 6;   // Super / Meta
        public const uint Mod5Mask = 1 << 7;   // Scroll Lock on many layouts

        /// <summary>All the lock bits together, for masking them out of a reported modifier state.</summary>
        public const uint AllLockMask = LockMask | Mod2Mask | Mod5Mask;

        #endregion

        #region Keysyms

        // Latin-1 characters are their own keysyms; anything above that uses the Unicode range.
        public const nint XK_BackSpace = 0xff08;
        public const nint XK_Tab = 0xff09;
        public const nint XK_Return = 0xff0d;
        public const nint XK_Escape = 0xff1b;
        public const nint XK_Delete = 0xffff;
        public const nint XK_Home = 0xff50;
        public const nint XK_Left = 0xff51;
        public const nint XK_Up = 0xff52;
        public const nint XK_Right = 0xff53;
        public const nint XK_Down = 0xff54;
        public const nint XK_Page_Up = 0xff55;
        public const nint XK_Page_Down = 0xff56;
        public const nint XK_End = 0xff57;
        public const nint XK_Insert = 0xff63;
        public const nint XK_F1 = 0xffbe;

        public const nint XK_Shift_L = 0xffe1;
        public const nint XK_Control_L = 0xffe3;
        public const nint XK_Alt_L = 0xffe9;
        public const nint XK_Super_L = 0xffeb;

        /// <summary>
        /// The X11 keysym for a Unicode character. Code points below 0x100 map directly; everything
        /// else uses the 0x01000000 Unicode keysym range, which is how Persian and Arabic text is
        /// expressed to the X server.
        /// </summary>
        public static nint KeysymForChar(char c) => c < 0x100 ? (nint)c : (nint)(0x01000000 + c);

        /// <summary>The keysym for one of Cloudict's named keys, or 0 when it has none.</summary>
        public static nint KeysymForKey(InjectedKey key)
        {
            if (key >= InjectedKey.A && key <= InjectedKey.Z)
                return (nint)('a' + (key - InjectedKey.A));   // lowercase: no Shift required

            if (key >= InjectedKey.D0 && key <= InjectedKey.D9)
                return (nint)('0' + (key - InjectedKey.D0));

            if (key >= InjectedKey.F1 && key <= InjectedKey.F12)
                return XK_F1 + (key - InjectedKey.F1);

            switch (key)
            {
                case InjectedKey.Enter: return XK_Return;
                case InjectedKey.Tab: return XK_Tab;
                case InjectedKey.Space: return ' ';
                case InjectedKey.Backspace: return XK_BackSpace;
                case InjectedKey.Delete: return XK_Delete;
                case InjectedKey.Escape: return XK_Escape;
                case InjectedKey.Insert: return XK_Insert;
                case InjectedKey.Home: return XK_Home;
                case InjectedKey.End: return XK_End;
                case InjectedKey.PageUp: return XK_Page_Up;
                case InjectedKey.PageDown: return XK_Page_Down;
                case InjectedKey.Up: return XK_Up;
                case InjectedKey.Down: return XK_Down;
                case InjectedKey.Left: return XK_Left;
                case InjectedKey.Right: return XK_Right;
                default: return 0;
            }
        }

        #endregion
    }
}
