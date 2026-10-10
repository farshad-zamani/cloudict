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

        [DllImport(CoreFoundation)]
        private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);

        [DllImport(CoreFoundation)]
        private static extern IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr[] keys, IntPtr[] values, long count,
                                                        IntPtr keyCallBacks, IntPtr valueCallBacks);

        private const uint kCFStringEncodingUTF8 = 0x08000100;

        /// <summary>
        /// Calls <c>AXIsProcessTrustedWithOptions(@{ kAXTrustedCheckOptionPrompt: @YES })</c>, which
        /// adds the application to the Accessibility list and shows Apple's request. The constants are
        /// looked up by name in the frameworks rather than restated, so their values are always the
        /// system's own.
        /// </summary>
        public static bool RequestAccessibilityTrust()
        {
            var cf = NativeLibrary.Load(CoreFoundation);
            var trueValue = Marshal.ReadIntPtr(NativeLibrary.GetExport(cf, "kCFBooleanTrue"));
            var keyCallBacks = NativeLibrary.GetExport(cf, "kCFTypeDictionaryKeyCallBacks");
            var valueCallBacks = NativeLibrary.GetExport(cf, "kCFTypeDictionaryValueCallBacks");

            var key = CFStringCreateWithCString(IntPtr.Zero, "AXTrustedCheckOptionPrompt", kCFStringEncodingUTF8);
            var options = CFDictionaryCreate(IntPtr.Zero, new[] { key }, new[] { trueValue }, 1, keyCallBacks, valueCallBacks);

            try { return AXIsProcessTrustedWithOptions(options); }
            finally
            {
                if (options != IntPtr.Zero) CFRelease(options);
                if (key != IntPtr.Zero) CFRelease(key);
            }
        }

        #endregion

        #region App Nap

        private const string ObjC = "/usr/lib/libobjc.A.dylib";

        [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
        [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, string utf8);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, ulong options, IntPtr reason);

        /// <summary>
        /// <c>NSActivityUserInitiatedAllowingIdleSystemSleep</c>: timers stay on time while the
        /// application is in the background, and the machine may still go to sleep when idle.
        /// </summary>
        private const ulong NSActivityUserInitiatedAllowingIdleSystemSleep = 0x00FFFFFFUL & ~(1UL << 20);

        private static IntPtr _activity;

        /// <summary>
        /// Opts Cloudict out of App Nap for as long as it runs.
        ///
        /// <para>An application idle in the background has its timers throttled by App Nap — a
        /// one-second check can be put off for a minute. Every one of Cloudict's checks is a timer:
        /// the badge, the microphone watch, the reading and typing loops. Napping, the helper browser
        /// was taken for having slept and was restarted by itself, and the first words after the
        /// pause were handled late and out of order. Cloudict is a background utility by design, which
        /// is exactly what <c>beginActivityWithOptions</c> is for. The activity object is retained and
        /// kept for the life of the process; ending it would end the exemption.</para>
        /// </summary>
        public static void DisableAppNap()
        {
            if (_activity != IntPtr.Zero) return;

            var processInfo = Send(objc_getClass("NSProcessInfo"), sel_registerName("processInfo"));
            var reason = Send(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"),
                              "Cloudict listens and types in the background");

            var activity = Send(processInfo, sel_registerName("beginActivityWithOptions:reason:"),
                                NSActivityUserInitiatedAllowingIdleSystemSleep, reason);

            if (activity != IntPtr.Zero)
                _activity = Send(activity, sel_registerName("retain"));
        }

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
