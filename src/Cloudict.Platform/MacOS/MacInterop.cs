using System;
using System.Runtime.InteropServices;

namespace Cloudict.Platform.MacOS
{
    /// <summary>
    /// Bindings for the macOS frameworks Cloudict needs. Frameworks are referenced by their bundle
    /// path, which is how the runtime resolves them on macOS.
    /// </summary>
    internal static class MacInterop
    {
        private const string ApplicationServices =
            "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

        private const string CoreFoundation =
            "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        #region Quartz event services (typing)

        /// <summary>Creates a keyboard event. Passing keycode 0 is fine when the text is supplied separately.</summary>
        [DllImport(ApplicationServices)]
        public static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, bool keyDown);

        /// <summary>
        /// Replaces the event's payload with literal UTF-16 text. This is what lets Cloudict type
        /// Persian regardless of the active input source — no key on the layout has to produce it.
        /// </summary>
        [DllImport(ApplicationServices, CharSet = CharSet.Unicode)]
        public static extern void CGEventKeyboardSetUnicodeString(IntPtr @event, int stringLength, [MarshalAs(UnmanagedType.LPWStr)] string unicodeString);

        [DllImport(ApplicationServices)]
        public static extern void CGEventSetFlags(IntPtr @event, ulong flags);

        [DllImport(ApplicationServices)]
        public static extern void CGEventPost(int tap, IntPtr @event);

        [DllImport(CoreFoundation)]
        public static extern void CFRelease(IntPtr cf);

        /// <summary>Post at the HID level, so the event behaves like real hardware input.</summary>
        public const int kCGHIDEventTap = 0;

        // CGEventFlags
        public const ulong kCGEventFlagMaskShift = 0x00020000;
        public const ulong kCGEventFlagMaskControl = 0x00040000;
        public const ulong kCGEventFlagMaskAlternate = 0x00080000;   // Option
        public const ulong kCGEventFlagMaskCommand = 0x00100000;

        #endregion

        #region Accessibility permission

        /// <summary>
        /// Whether this process may post synthetic events. macOS withholds that until the user grants
        /// Accessibility in System Settings, and there is no way to request it programmatically —
        /// passing the prompt option merely opens the pane for them.
        /// </summary>
        [DllImport(ApplicationServices)]
        public static extern bool AXIsProcessTrustedWithOptions(IntPtr options);

        [DllImport(ApplicationServices)]
        public static extern bool AXIsProcessTrusted();

        #endregion

        #region Carbon hotkeys

        private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

        [StructLayout(LayoutKind.Sequential)]
        public struct EventTypeSpec
        {
            public uint eventClass;
            public uint eventKind;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EventHotKeyID
        {
            public uint signature;
            public uint id;
        }

        public delegate int EventHandlerProcPtr(IntPtr callRef, IntPtr eventRef, IntPtr userData);

        [DllImport(Carbon)]
        public static extern int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyID hotKeyId,
                                                     IntPtr target, uint options, out IntPtr hotKeyRef);

        [DllImport(Carbon)]
        public static extern int UnregisterEventHotKey(IntPtr hotKeyRef);

        [DllImport(Carbon)]
        public static extern IntPtr GetEventDispatcherTarget();

        [DllImport(Carbon)]
        public static extern int InstallEventHandler(IntPtr target, EventHandlerProcPtr handler, int numTypes,
                                                     EventTypeSpec[] typeList, IntPtr userData, out IntPtr handlerRef);

        [DllImport(Carbon)]
        public static extern int RemoveEventHandler(IntPtr handlerRef);

        [DllImport(Carbon)]
        public static extern int GetEventParameter(IntPtr eventRef, uint name, uint type, IntPtr outActualType,
                                                   int bufferSize, IntPtr outActualSize, out EventHotKeyID outData);

        [DllImport(Carbon)]
        public static extern int ReceiveNextEvent(int numTypes, IntPtr typeList, double timeout,
                                                  bool pullEvent, out IntPtr outEvent);

        [DllImport(Carbon)]
        public static extern int SendEventToEventTarget(IntPtr eventRef, IntPtr target);

        [DllImport(Carbon)]
        public static extern void ReleaseEvent(IntPtr eventRef);

        public const uint kEventClassKeyboard = 0x6B657962;   // 'keyb'
        public const uint kEventHotKeyPressed = 5;
        public const uint kEventParamDirectObject = 0x2D2D2D2D;   // '----'
        public const uint typeEventHotKeyID = 0x686B6964;          // 'hkid'

        // Carbon modifier masks (unrelated to the CGEventFlags above).
        public const uint cmdKey = 0x0100;
        public const uint shiftKey = 0x0200;
        public const uint optionKey = 0x0800;
        public const uint controlKey = 0x1000;

        #endregion
    }
}
