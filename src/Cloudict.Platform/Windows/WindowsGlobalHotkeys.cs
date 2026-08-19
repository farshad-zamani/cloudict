using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Cloudict.Abstractions;

namespace Cloudict.Platform.Windows
{
    /// <summary>
    /// System-wide shortcuts via <c>RegisterHotKey</c>.
    ///
    /// <para>The 2.x implementation hooked WPF's <c>HwndSource</c> on the main window, tying global
    /// shortcuts to the UI framework. This version owns a dedicated thread with its own message
    /// loop and registers with a null window handle — Windows then posts <c>WM_HOTKEY</c> straight
    /// to that thread's queue, so no window, window class or <c>WndProc</c> is involved. It depends
    /// on nothing above the OS, which is what lets X11 and Carbon satisfy the same interface later
    /// without the UI knowing the difference.</para>
    ///
    /// <para>Registration and unregistration are marshalled onto that thread because Windows ties a
    /// hotkey to the thread that created it — calling <c>UnregisterHotKey</c> from anywhere else
    /// silently fails.</para>
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class WindowsGlobalHotkeys : IGlobalHotkeys
    {
        private const int WM_HOTKEY = 0x0312;
        private const int WM_QUIT = 0x0012;

        /// <summary>Private message telling the pump to drain the pending-work queue.</summary>
        private const int WM_APP_WORK = 0x8000 + 1;

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;

        /// <summary>Stops the shortcut repeating while the key is held down.</summary>
        private const uint MOD_NOREPEAT = 0x4000;

        private readonly Dictionary<int, Action> _callbacks = new Dictionary<int, Action>();
        private readonly ConcurrentQueue<Action> _pending = new ConcurrentQueue<Action>();
        private readonly object _gate = new object();
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);

        private Thread _pumpThread;
        private uint _threadId;
        private int _nextId = 9000;
        private volatile bool _disposed;

        public bool IsSupported => true;
        public string UnsupportedReasonKey => null;

        /// <summary>
        /// Ignored on Windows: this implementation registers against its own thread rather than a
        /// window, so it needs no handle from the UI.
        /// </summary>
        public void Attach(IntPtr nativeWindowHandle) { }

        public bool Register(HotkeyBinding binding, Action onPressed)
        {
            if (_disposed || binding == null || !binding.IsValid || onPressed == null) return false;

            ushort vk = VirtualKeys.ToVirtualKey(binding.Key);
            if (vk == 0) return false;

            uint modifiers = MOD_NOREPEAT;
            if (binding.Modifiers.HasFlag(KeyModifiers.Control)) modifiers |= MOD_CONTROL;
            if (binding.Modifiers.HasFlag(KeyModifiers.Alt)) modifiers |= MOD_ALT;
            if (binding.Modifiers.HasFlag(KeyModifiers.Shift)) modifiers |= MOD_SHIFT;
            if (binding.Modifiers.HasFlag(KeyModifiers.Meta)) modifiers |= MOD_WIN;

            EnsurePump();

            int id;
            lock (_gate) id = _nextId++;

            bool registered = false;
            RunOnPump(() =>
            {
                registered = RegisterHotKey(IntPtr.Zero, id, modifiers, vk);
                if (registered)
                {
                    lock (_gate) _callbacks[id] = onPressed;
                }
                else
                {
                    Debug.WriteLine($"[WindowsGlobalHotkeys] {binding} rejected — most likely already claimed by another application");
                }
            });

            return registered;
        }

        public void UnregisterAll()
        {
            if (_pumpThread == null) return;

            RunOnPump(() =>
            {
                List<int> ids;
                lock (_gate)
                {
                    ids = new List<int>(_callbacks.Keys);
                    _callbacks.Clear();
                }

                foreach (var id in ids) UnregisterHotKey(IntPtr.Zero, id);
            });
        }

        #region Message pump

        private void EnsurePump()
        {
            if (_pumpThread != null) return;

            lock (_gate)
            {
                if (_pumpThread != null) return;

                _pumpThread = new Thread(Pump)
                {
                    IsBackground = true,
                    Name = "Cloudict global hotkeys"
                };
                _pumpThread.SetApartmentState(ApartmentState.STA);
                _pumpThread.Start();
            }

            if (!_ready.Wait(TimeSpan.FromSeconds(5)))
                Debug.WriteLine("[WindowsGlobalHotkeys] hotkey thread did not start in time");
        }

        private void Pump()
        {
            _threadId = GetCurrentThreadId();

            // Force the OS to create a message queue for this thread before anyone posts to it.
            PeekMessage(out _, IntPtr.Zero, WM_APP_WORK, WM_APP_WORK, 0);
            _ready.Set();

            while (!_disposed)
            {
                int result = GetMessage(out MSG msg, IntPtr.Zero, 0, 0);
                if (result <= 0) break;   // 0 = WM_QUIT, -1 = error

                if (msg.message == WM_HOTKEY)
                {
                    Action callback;
                    lock (_gate) _callbacks.TryGetValue(msg.wParam.ToInt32(), out callback);

                    if (callback != null)
                    {
                        // Run off the pump so a slow handler cannot stall subsequent shortcuts.
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            try { callback(); }
                            catch (Exception ex) { Debug.WriteLine($"[WindowsGlobalHotkeys] handler threw: {ex.Message}"); }
                        });
                    }
                }
                else if (msg.message == WM_APP_WORK)
                {
                    DrainPending();
                }
            }

            DrainPending();
        }

        private void DrainPending()
        {
            while (_pending.TryDequeue(out var work))
            {
                try { work(); }
                catch (Exception ex) { Debug.WriteLine($"[WindowsGlobalHotkeys] queued work threw: {ex.Message}"); }
            }
        }

        /// <summary>
        /// Queues work for the hotkey thread and waits for it, because a hotkey belongs to the
        /// thread that registered it.
        /// </summary>
        private void RunOnPump(Action work)
        {
            if (GetCurrentThreadId() == _threadId) { work(); return; }

            using var done = new ManualResetEventSlim(false);
            _pending.Enqueue(() => { try { work(); } finally { done.Set(); } });

            if (!PostThreadMessage(_threadId, WM_APP_WORK, IntPtr.Zero, IntPtr.Zero))
            {
                Debug.WriteLine($"[WindowsGlobalHotkeys] PostThreadMessage failed ({Marshal.GetLastWin32Error()})");
                return;
            }

            if (!done.Wait(TimeSpan.FromSeconds(5)))
                Debug.WriteLine("[WindowsGlobalHotkeys] hotkey thread did not answer in time");
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                UnregisterAll();
                _disposed = true;
                if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                _pumpThread?.Join(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsGlobalHotkeys] dispose: {ex.Message}");
            }
            finally
            {
                _disposed = true;
                _ready.Dispose();
            }
        }

        #region Win32

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public int message;
            public IntPtr wParam, lParam;
            public int time;
            public int pt_x, pt_y;
        }

        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max, uint remove);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool PostThreadMessage(uint threadId, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

        #endregion
    }
}
