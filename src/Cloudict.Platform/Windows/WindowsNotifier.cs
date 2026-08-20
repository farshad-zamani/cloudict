using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Cloudict.Abstractions;

namespace Cloudict.Platform.Windows
{
    /// <summary>
    /// Desktop notifications on Windows, as balloon tips from a tray icon.
    ///
    /// <para>Windows will only show a balloon on behalf of a registered notification icon, so this
    /// owns one. It is the app's <em>only</em> tray icon — 2.x had a bug where a second one appeared
    /// as a stray blue "i" because notifications created their own — and it doubles as the tray
    /// presence the user sees, including the click that restores the window.</para>
    ///
    /// <para>The icon needs a window to send its callbacks to, so this creates a message-only window
    /// on a dedicated thread, the same shape as the global-hotkey listener.</para>
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class WindowsNotifier : INotifier, ITrayPresence
    {
        private const int WM_APP_TRAY = 0x8000 + 100;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_DESTROY = 0x0002;

        private const uint NIM_ADD = 0x00000000;
        private const uint NIM_MODIFY = 0x00000001;
        private const uint NIM_DELETE = 0x00000002;

        private const uint NIF_MESSAGE = 0x00000001;
        private const uint NIF_ICON = 0x00000002;
        private const uint NIF_TIP = 0x00000004;
        private const uint NIF_INFO = 0x00000010;

        private const uint NIIF_INFO = 0x00000001;

        private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
        private readonly object _gate = new object();

        private Thread _pump;
        private IntPtr _hwnd;
        private IntPtr _icon;
        private WndProcDelegate _wndProc;   // kept alive: the OS holds a raw pointer
        private volatile bool _disposed;
        private bool _iconAdded;
        private string _tooltip = "Cloudict";

        private static void Log(string message) => DiagnosticLog.Write("WindowsNotifier", message);

        public bool IsSupported => true;

        /// <summary>Raised when the user clicks the tray icon, to bring the window back.</summary>
        public event EventHandler Activated;

        /// <summary>Raised when the user right-clicks the tray icon and asks to quit.</summary>
        public event EventHandler ExitRequested;

        public WindowsNotifier()
        {
            _pump = new Thread(Pump) { IsBackground = true, Name = "Cloudict tray icon" };
            _pump.SetApartmentState(ApartmentState.STA);
            _pump.Start();

            if (!_ready.Wait(TimeSpan.FromSeconds(5)))
                Debug.WriteLine("[WindowsNotifier] tray window did not come up in time");
        }

        public void Show(string title, string message)
        {
            if (_disposed || _hwnd == IntPtr.Zero || !_iconAdded) return;

            try
            {
                var data = CreateData();
                data.uFlags = NIF_INFO;
                data.szInfoTitle = Truncate(title, 63);
                data.szInfo = Truncate(message, 255);
                data.dwInfoFlags = NIIF_INFO;

                Log(Shell_NotifyIcon(NIM_MODIFY, ref data)
                    ? $"notification shown: {title} - {message}"
                    : "Shell_NotifyIcon(NIM_MODIFY) failed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsNotifier] notification failed: {ex.Message}");
            }
        }

        public void SetTooltip(string text)
        {
            _tooltip = Truncate(string.IsNullOrWhiteSpace(text) ? "Cloudict" : text, 127);
            if (!_iconAdded) return;

            var data = CreateData();
            data.uFlags = NIF_TIP;
            Shell_NotifyIcon(NIM_MODIFY, ref data);
        }

        /// <summary>Balloon text is truncated by the API anyway; doing it here keeps it predictable.</summary>
        private static string Truncate(string value, int max) =>
            string.IsNullOrEmpty(value) ? string.Empty :
            value.Length <= max ? value : value.Substring(0, max);

        #region Message pump

        private void Pump()
        {
            try
            {
                _wndProc = WndProc;

                var className = "CloudictTray_" + Guid.NewGuid().ToString("N");
                var wc = new WNDCLASSEX
                {
                    cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                    hInstance = GetModuleHandle(null),
                    lpszClassName = className
                };

                if (RegisterClassEx(ref wc) == 0)
                {
                    Debug.WriteLine($"[WindowsNotifier] RegisterClassEx failed ({Marshal.GetLastWin32Error()})");
                    _ready.Set();
                    return;
                }

                _hwnd = CreateWindowEx(0, className, className, 0, 0, 0, 0, 0,
                                       HWND_MESSAGE, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

                if (_hwnd == IntPtr.Zero)
                {
                    Debug.WriteLine($"[WindowsNotifier] CreateWindowEx failed ({Marshal.GetLastWin32Error()})");
                    _ready.Set();
                    return;
                }

                LoadIcon();
                AddIcon();
                _ready.Set();

                while (!_disposed && GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsNotifier] pump stopped: {ex.Message}");
                _ready.Set();
            }
        }

        /// <summary>
        /// Loads the icon from the executable's own resources, which is the same artwork the taskbar
        /// and window use.
        /// </summary>
        private void LoadIcon()
        {
            try
            {
                var exe = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exe))
                {
                    _icon = ExtractIcon(GetModuleHandle(null), exe, 0);
                    Log($"icon extracted from {exe}: 0x{_icon.ToInt64():X}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsNotifier] could not load the application icon: {ex.Message}");
            }

            // Better a generic icon than none: without one the tray entry is invisible.
            if (_icon == IntPtr.Zero || _icon == new IntPtr(1))
                _icon = LoadIcon(IntPtr.Zero, (IntPtr)32512);   // IDI_APPLICATION
        }

        private void AddIcon()
        {
            var data = CreateData();
            data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;

            _iconAdded = Shell_NotifyIcon(NIM_ADD, ref data);
            Log(_iconAdded
                ? $"tray icon added (hwnd 0x{_hwnd.ToInt64():X}, icon 0x{_icon.ToInt64():X})"
                : $"Shell_NotifyIcon(NIM_ADD) failed ({Marshal.GetLastWin32Error()})");
        }

        private NOTIFYICONDATA CreateData() => new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uCallbackMessage = WM_APP_TRAY,
            hIcon = _icon,
            szTip = _tooltip
        };

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_APP_TRAY)
            {
                switch ((int)lParam)
                {
                    case WM_LBUTTONUP:
                    case WM_LBUTTONDBLCLK:
                        Raise(Activated);
                        break;

                    case WM_RBUTTONUP:
                        // No native menu: a right-click restores the window, where every action
                        // already has a button. Reimplementing a Win32 popup menu would add a
                        // second place for the same commands to drift out of step.
                        Raise(Activated);
                        break;
                }

                return IntPtr.Zero;
            }

            if (msg == WM_DESTROY)
            {
                PostQuitMessage(0);
                return IntPtr.Zero;
            }

            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        private void Raise(EventHandler handler) =>
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { handler?.Invoke(this, EventArgs.Empty); }
                catch (Exception ex) { Debug.WriteLine($"[WindowsNotifier] handler threw: {ex.Message}"); }
            });

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                lock (_gate)
                {
                    if (_iconAdded)
                    {
                        var data = CreateData();
                        Shell_NotifyIcon(NIM_DELETE, ref data);
                        _iconAdded = false;
                    }
                }

                if (_hwnd != IntPtr.Zero) PostMessage(_hwnd, WM_DESTROY, IntPtr.Zero, IntPtr.Zero);
                _pump?.Join(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsNotifier] dispose: {ex.Message}");
            }
            finally
            {
                _ready.Dispose();
            }
        }

        #region Win32

        private delegate IntPtr WndProcDelegate(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public int cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public int message;
            public IntPtr wParam, lParam;
            public int time;
            public int pt_x, pt_y;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr ExtractIcon(IntPtr hInst, string file, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr iconName);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern void PostQuitMessage(int exitCode);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string moduleName);

        #endregion
    }
}
