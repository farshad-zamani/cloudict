using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace Cloudict.App.Services
{
    /// <summary>
    /// Remembers which application the user was working in, so a click on Cloudict's corner badge
    /// can hand the focus straight back to it. macOS only.
    ///
    /// <para>On macOS a click on any window activates the application that owns it — there is no
    /// equivalent of Windows' non-activating window style for an ordinary window. Clicking the badge
    /// therefore made Cloudict the active application, and the words that followed were typed into
    /// Cloudict instead of the user's document. The frontmost application is sampled a few times a
    /// second while Cloudict is not the one in front; after a click, that application is activated
    /// again, which on macOS restores its own window and its own text cursor exactly where they
    /// were.</para>
    ///
    /// <para>Sampled on the UI thread, which on macOS is the main thread with its run loop's
    /// autorelease pool around every tick, so the Objective-C objects these calls return are released
    /// properly.</para>
    /// </summary>
    internal sealed class MacFrontmostApp : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private int _lastOtherPid;

        public MacFrontmostApp()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += (_, _) => Sample();
            _timer.Start();
            Sample();
        }

        private void Sample()
        {
            try
            {
                var pid = FrontmostPid();
                if (pid > 0 && pid != Environment.ProcessId) _lastOtherPid = pid;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MacFrontmostApp] sample: {ex.Message}");
            }
        }

        /// <summary>Activates the application that was in front before Cloudict was clicked.</summary>
        public void ReturnFocus()
        {
            var pid = _lastOtherPid;
            if (pid <= 0) return;

            try
            {
                var app = SendPtr(objc_getClass("NSRunningApplication"),
                                  sel_registerName("runningApplicationWithProcessIdentifier:"), pid);
                if (app == IntPtr.Zero) return;

                // Ignoring other apps is required on macOS 13 and earlier to take the activation back
                // from Cloudict; from macOS 14 the flag is ignored and the hand-over from the active
                // application — which Cloudict is at this moment — is what makes it work.
                SendBool(app, sel_registerName("activateWithOptions:"), NSApplicationActivateIgnoringOtherApps);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MacFrontmostApp] could not return the focus: {ex.Message}");
            }
        }

        private static int FrontmostPid()
        {
            var workspace = SendPtr(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace"));
            if (workspace == IntPtr.Zero) return 0;

            var app = SendPtr(workspace, sel_registerName("frontmostApplication"));
            return app == IntPtr.Zero ? 0 : SendInt(app, sel_registerName("processIdentifier"));
        }

        public void Dispose() => _timer.Stop();

        private const ulong NSApplicationActivateIgnoringOtherApps = 1 << 1;
        private const string ObjC = "/usr/lib/libobjc.A.dylib";

        [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
        [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtr(IntPtr receiver, IntPtr selector);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtr(IntPtr receiver, IntPtr selector, int value);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern int SendInt(IntPtr receiver, IntPtr selector);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern byte SendBool(IntPtr receiver, IntPtr selector, ulong value);
    }
}
