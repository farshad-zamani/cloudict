using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows;
using System.Collections.Generic;

namespace Cloudict
{
    /// <summary>
    /// مدیریت شورتکی‌های سراسری برای کنترل میکروفون
    /// </summary>
    public class GlobalShortcutManager
    {
        // Windows API imports
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Modifier keys
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;

        // Hotkey IDs
        private const int HOTKEY_ID_TOGGLE = 9000;  // Ctrl+Alt+A (toggle)
        private const int HOTKEY_ID_STOP = 9001;    // Ctrl+Alt+S (stop)

        private IntPtr _windowHandle;
        private HwndSource _source;
        private AppSettings _settings;
        private Action _onToggleShortcutPressed;
        private Action _onStopShortcutPressed;
        private Dictionary<int, bool> _registeredHotkeys;

        public GlobalShortcutManager(Window window, AppSettings settings, Action onToggleShortcutPressed, Action onStopShortcutPressed)
        {
            _settings = settings;
            _onToggleShortcutPressed = onToggleShortcutPressed;
            _onStopShortcutPressed = onStopShortcutPressed;
            _registeredHotkeys = new Dictionary<int, bool>();
            
            // Get window handle
            var helper = new WindowInteropHelper(window);
            _windowHandle = helper.Handle;
            
            // Add hook for window messages
            _source = HwndSource.FromHwnd(_windowHandle);
            _source.AddHook(HwndHook);
        }

        /// <summary>
        /// ثبت تمام شورتکی‌های سراسری
        /// </summary>
        public bool RegisterShortcuts()
        {
            if (!_settings.GlobalShortcutEnabled)
                return false;

            // Unregister existing hotkeys first
            UnregisterShortcuts();

            bool success = true;

            // Register first shortcut (toggle) - customizable
            uint toggleModifiers = 0;
            if (_settings.ShortcutCtrl) toggleModifiers |= MOD_CONTROL;
            if (_settings.ShortcutShift) toggleModifiers |= MOD_SHIFT;
            if (_settings.ShortcutAlt) toggleModifiers |= MOD_ALT;
            
            uint toggleVkCode = GetVirtualKeyCode(_settings.ShortcutKey ?? "A");
            if (toggleVkCode != 0 && toggleModifiers != 0)
            {
                bool registered = RegisterHotKey(_windowHandle, HOTKEY_ID_TOGGLE, toggleModifiers, toggleVkCode);
                _registeredHotkeys[HOTKEY_ID_TOGGLE] = registered;
                success &= registered;
            }

            // Register second shortcut (stop) - customizable
            uint stopModifiers = 0;
            if (_settings.StopShortcutCtrl) stopModifiers |= MOD_CONTROL;
            if (_settings.StopShortcutShift) stopModifiers |= MOD_SHIFT;
            if (_settings.StopShortcutAlt) stopModifiers |= MOD_ALT;
            
            uint stopVkCode = GetVirtualKeyCode(_settings.StopShortcutKey ?? "S");
            if (stopVkCode != 0 && stopModifiers != 0)
            {
                bool registered = RegisterHotKey(_windowHandle, HOTKEY_ID_STOP, stopModifiers, stopVkCode);
                _registeredHotkeys[HOTKEY_ID_STOP] = registered;
                success &= registered;
            }

            return success;
        }

        /// <summary>
        /// لغو ثبت تمام شورتکی‌های سراسری
        /// </summary>
        public void UnregisterShortcuts()
        {
            foreach (var hotkeyId in _registeredHotkeys.Keys)
            {
                UnregisterHotKey(_windowHandle, hotkeyId);
            }
            _registeredHotkeys.Clear();
        }

        /// <summary>
        /// پردازش پیام‌های ویندوز
        /// </summary>
        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;

            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                
                if (id == HOTKEY_ID_TOGGLE)
                {
                    _onToggleShortcutPressed?.Invoke();
                    handled = true;
                }
                else if (id == HOTKEY_ID_STOP)
                {
                    _onStopShortcutPressed?.Invoke();
                    handled = true;
                }
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// تبدیل نام کلید به کد مجازی
        /// </summary>
        private uint GetVirtualKeyCode(string keyName)
        {
            if (string.IsNullOrEmpty(keyName))
                return 0;

            keyName = keyName.ToUpper();

            // Letters A-Z
            if (keyName.Length == 1 && keyName[0] >= 'A' && keyName[0] <= 'Z')
            {
                return (uint)keyName[0];
            }

            // Numbers 0-9
            if (keyName.Length == 1 && keyName[0] >= '0' && keyName[0] <= '9')
            {
                return (uint)keyName[0];
            }

            // Function keys
            if (keyName.StartsWith("F") && keyName.Length > 1)
            {
                if (int.TryParse(keyName.Substring(1), out int fNum) && fNum >= 1 && fNum <= 12)
                {
                    return (uint)(0x70 + fNum - 1); // VK_F1 to VK_F12
                }
            }

            // Special keys
            switch (keyName)
            {
                case "SPACE": return 0x20;
                case "ENTER": return 0x0D;
                case "TAB": return 0x09;
                case "ESC": return 0x1B;
                case "DELETE": return 0x2E;
                case "INSERT": return 0x2D;
                case "HOME": return 0x24;
                case "END": return 0x23;
                case "PAGEUP": return 0x21;
                case "PAGEDOWN": return 0x22;
                case "UP": return 0x26;
                case "DOWN": return 0x28;
                case "LEFT": return 0x25;
                case "RIGHT": return 0x27;
                default: return 0;
            }
        }

        /// <summary>
        /// آزادسازی منابع
        /// </summary>
        public void Dispose()
        {
            UnregisterShortcuts();
            _source?.RemoveHook(HwndHook);
        }
    }
}