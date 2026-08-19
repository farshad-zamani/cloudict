using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cloudict.Abstractions;

namespace Cloudict.Platform.Linux
{
    /// <summary>
    /// System-wide shortcuts on X11 via <c>XGrabKey</c>.
    ///
    /// <para>A grab on the root window intercepts the key whichever application has focus. Two X11
    /// details shape this implementation:</para>
    ///
    /// <para><b>A grab belongs to the connection that made it.</b> Events go only to that client, so
    /// the grab and the event loop must live on one display connection. Grabbing from a second
    /// connection does not add a listener — it fails with <c>BadAccess</c>, because the key is
    /// already held. Everything here therefore runs on the listener thread's own connection, and
    /// <see cref="Register"/> hands work to it rather than touching X itself.</para>
    ///
    /// <para><b>Lock keys are part of the modifier mask.</b> X reports Caps, Num and Scroll Lock in
    /// the same field as Ctrl and Alt, so a grab for Ctrl+Alt+A simply does not fire while Num Lock
    /// is on. The fix is to register the combination once for every permutation of those bits.</para>
    ///
    /// <para>Wayland has no equivalent: the compositor owns the keyboard and no protocol lets a plain
    /// application claim a global key. The user is pointed at their desktop's own shortcut settings
    /// and <c>cloudict --toggle</c> instead.</para>
    /// </summary>
    internal sealed class LinuxGlobalHotkeys : IGlobalHotkeys
    {
        /// <summary>Every permutation of the lock modifiers, so grabs survive Caps/Num/Scroll Lock.</summary>
        private static readonly uint[] IgnoredModifierMasks = BuildIgnoredMasks();

        private const int KeyPress = 2;

        /// <summary>How often the listener wakes to apply new grabs and drain events.</summary>
        private const int PollIntervalMs = 25;

        private readonly object _gate = new object();
        private readonly List<Registration> _registrations = new List<Registration>();
        private readonly Queue<Registration> _pending = new Queue<Registration>();

        private Thread _listener;
        private volatile bool _disposed;

        /// <summary>Kept alive for the lifetime of the object: X holds a raw pointer to it.</summary>
        private static X11Interop.XErrorHandler _errorHandler;

        public bool IsSupported { get; private set; }
        public string UnsupportedReasonKey { get; private set; }

        private sealed class Registration
        {
            public byte Keycode;
            public nint KeysymToResolve;
            public uint Modifiers;
            public Action OnPressed;
            public ManualResetEventSlim Applied;
            public bool Success;
        }

        public LinuxGlobalHotkeys()
        {
            var session = LinuxSession.Detect();

            if (session == LinuxDisplayServer.Wayland && !LinuxSession.HasReachableXServer())
            {
                IsSupported = false;
                UnsupportedReasonKey = "Platform_Err_LinuxHotkeysWayland";
                return;
            }

            // Probe on a throwaway connection; the listener opens its own to own the grabs.
            try
            {
                var probe = X11Interop.XOpenDisplay(null);
                if (probe == IntPtr.Zero)
                {
                    IsSupported = false;
                    UnsupportedReasonKey = "Platform_Err_LinuxNoDisplay";
                    return;
                }

                X11Interop.XCloseDisplay(probe);
                IsSupported = true;

                // XWayland can still deliver grabs, but only while an X11 window has focus.
                if (session == LinuxDisplayServer.Wayland)
                    UnsupportedReasonKey = "Platform_Warn_LinuxHotkeysWaylandPartial";
            }
            catch (DllNotFoundException)
            {
                IsSupported = false;
                UnsupportedReasonKey = "Platform_Err_LinuxX11LibsMissing";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LinuxGlobalHotkeys] init failed: {ex.Message}");
                IsSupported = false;
                UnsupportedReasonKey = "Platform_Err_LinuxHotkeysUnavailable";
            }
        }

        /// <summary>Not needed on X11: grabs are made against the root window, not the app's own.</summary>
        public void Attach(IntPtr nativeWindowHandle) { }

        public bool Register(HotkeyBinding binding, Action onPressed)
        {
            if (!IsSupported || _disposed || binding == null || !binding.IsValid || onPressed == null)
                return false;

            var keysym = X11Interop.KeysymForKey(binding.Key);
            if (keysym == 0) return false;

            // The keycode lookup needs a display connection, so the listener resolves it.
            var registration = new Registration
            {
                KeysymToResolve = keysym,
                Modifiers = ToX11Modifiers(binding.Modifiers),
                OnPressed = onPressed,
                Applied = new ManualResetEventSlim(false)
            };


            lock (_gate)
            {
                _pending.Enqueue(registration);
                EnsureListener();
            }

            // Wait briefly so the caller gets a truthful answer rather than an optimistic one.
            registration.Applied.Wait(TimeSpan.FromSeconds(2));
            var ok = registration.Success;
            registration.Applied.Dispose();

            if (!ok)
                Debug.WriteLine($"[LinuxGlobalHotkeys] {binding} could not be grabbed — most likely already taken");

            return ok;
        }

        public void UnregisterAll()
        {
            lock (_gate)
            {
                // The listener owns the connection, so it performs the ungrab on its next pass.
                foreach (var reg in _registrations) reg.OnPressed = null;
                _ungrabRequested = true;
            }
        }

        private volatile bool _ungrabRequested;

        private void EnsureListener()
        {
            if (_listener != null) return;

            _listener = new Thread(Listen)
            {
                IsBackground = true,
                Name = "Cloudict global hotkeys (X11)"
            };
            _listener.Start();
        }

        /// <summary>
        /// Owns the display connection: applies grabs, drains key events, releases on shutdown.
        ///
        /// <para>It polls with <c>XPending</c> rather than blocking in <c>XNextEvent</c>, so newly
        /// registered shortcuts can be grabbed promptly without needing to interrupt a blocking
        /// call. Shortcuts are rare enough that a 25 ms tick costs nothing measurable.</para>
        /// </summary>
        private void Listen()
        {
            IntPtr display = IntPtr.Zero;
            IntPtr root = IntPtr.Zero;

            try
            {
                display = X11Interop.XOpenDisplay(null);
                if (display == IntPtr.Zero) return;

                root = X11Interop.XDefaultRootWindow(display);

                // A failed grab must not take the process down via Xlib's fatal default handler.
                _errorHandler = SilentErrorHandler;
                X11Interop.XSetErrorHandler(_errorHandler);

                X11Interop.XSelectInput(display, root, X11Interop.KeyPressMask);
                X11Interop.XSync(display, false);

                var buffer = new byte[256];   // XEvent is a union; 192 bytes is the 64-bit size.

                while (!_disposed)
                {
                    ApplyPendingGrabs(display, root);
                    ApplyUngrabIfRequested(display, root);

                    while (!_disposed && X11Interop.XPending(display) > 0)
                    {
                        X11Interop.XNextEvent(display, buffer);
                        if (BitConverter.ToInt32(buffer, 0) != KeyPress) continue;

                        // XKeyEvent on 64-bit: state at offset 80, keycode at 84.
                        uint state = BitConverter.ToUInt32(buffer, 80);
                        uint keycode = BitConverter.ToUInt32(buffer, 84);

                        Dispatch(keycode, state);
                    }

                    Thread.Sleep(PollIntervalMs);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LinuxGlobalHotkeys] listener stopped: {ex.Message}");
            }
            finally
            {
                if (display != IntPtr.Zero)
                {
                    try
                    {
                        Ungrab(display, root);
                        X11Interop.XCloseDisplay(display);
                    }
                    catch (Exception ex) { Debug.WriteLine($"[LinuxGlobalHotkeys] cleanup: {ex.Message}"); }
                }
            }
        }

        private void ApplyPendingGrabs(IntPtr display, IntPtr root)
        {
            while (true)
            {
                Registration reg;
                lock (_gate)
                {
                    if (_pending.Count == 0) return;
                    reg = _pending.Dequeue();
                }

                reg.Keycode = X11Interop.XKeysymToKeycode(display, reg.KeysymToResolve);

                if (reg.Keycode != 0)
                {
                    foreach (var extra in IgnoredModifierMasks)
                    {
                        X11Interop.XGrabKey(display, reg.Keycode, reg.Modifiers | extra, root,
                                            true, X11Interop.GrabModeAsync, X11Interop.GrabModeAsync);
                    }

                    X11Interop.XSync(display, false);
                    reg.Success = true;

                    lock (_gate) _registrations.Add(reg);
                }

                reg.Applied.Set();
            }
        }

        private void ApplyUngrabIfRequested(IntPtr display, IntPtr root)
        {
            if (!_ungrabRequested) return;
            _ungrabRequested = false;
            Ungrab(display, root);
        }

        private void Ungrab(IntPtr display, IntPtr root)
        {
            lock (_gate)
            {
                foreach (var reg in _registrations)
                    foreach (var extra in IgnoredModifierMasks)
                        X11Interop.XUngrabKey(display, reg.Keycode, reg.Modifiers | extra, root);

                _registrations.Clear();
            }

            X11Interop.XSync(display, false);
        }

        private void Dispatch(uint keycode, uint state)
        {
            Action handler = null;

            lock (_gate)
            {
                foreach (var reg in _registrations)
                {
                    if (reg.Keycode != keycode) continue;

                    // Compare only the real modifiers; the lock bits are noise.
                    if ((state & ~X11Interop.AllLockMask) == reg.Modifiers)
                    {
                        handler = reg.OnPressed;
                        break;
                    }
                }
            }

            if (handler == null) return;

            // Run off the listener so a slow handler cannot delay later shortcuts.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { handler(); }
                catch (Exception ex) { Debug.WriteLine($"[LinuxGlobalHotkeys] handler threw: {ex.Message}"); }
            });
        }

        private static uint ToX11Modifiers(KeyModifiers modifiers)
        {
            uint mask = 0;
            if (modifiers.HasFlag(KeyModifiers.Shift)) mask |= X11Interop.ShiftMask;
            if (modifiers.HasFlag(KeyModifiers.Control)) mask |= X11Interop.ControlMask;
            if (modifiers.HasFlag(KeyModifiers.Alt)) mask |= X11Interop.Mod1Mask;
            if (modifiers.HasFlag(KeyModifiers.Meta)) mask |= X11Interop.Mod4Mask;
            return mask;
        }

        private static uint[] BuildIgnoredMasks()
        {
            uint[] locks = { 0, X11Interop.LockMask, X11Interop.Mod2Mask, X11Interop.Mod5Mask };
            var masks = new List<uint>();

            foreach (var a in locks)
                foreach (var b in locks)
                    foreach (var c in locks)
                    {
                        uint combined = a | b | c;
                        if (!masks.Contains(combined)) masks.Add(combined);
                    }

            return masks.ToArray();
        }

        private static int SilentErrorHandler(IntPtr display, IntPtr errorEvent) => 0;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _listener?.Join(TimeSpan.FromSeconds(2)); }
            catch (Exception ex) { Debug.WriteLine($"[LinuxGlobalHotkeys] dispose: {ex.Message}"); }
        }
    }
}
