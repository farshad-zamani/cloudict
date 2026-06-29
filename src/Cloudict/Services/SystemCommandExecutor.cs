using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WindowsInput;

namespace Cloudict
{
    /// <summary>
    /// کلاس اجرای دستورات سیستم با استفاده از InputSimulator
    /// </summary>
    public class SystemCommandExecutor
    {
        private readonly InputSimulator _inputSimulator;
        
        // Win32 API imports برای کنترل کیبورد و زبان
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);

        [DllImport("user32.dll")]
        private static extern bool ActivateKeyboardLayout(IntPtr hkl, uint Flags);

        // پیام‌های Windows برای تغییر زبان
        private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
        private const uint KLF_ACTIVATE = 0x00000001;
        private const uint INPUTLANGCHANGE_SYSCHARSET = 0x0001;
        
        public SystemCommandExecutor()
        {
            _inputSimulator = new InputSimulator();
        }

        /// <summary>
        /// تایپ متن با استفاده از InputSimulator
        /// </summary>
        /// <param name="text">متن برای تایپ</param>
        public void TypeText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            try
            {
                _inputSimulator.Keyboard.TextEntry(text);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"خطا در تایپ متن: {ex.Message}");
            }
        }


        
        /// <summary>
        /// اجرای دستور کلیدی با InputSimulator
        /// </summary>
        /// <param name="keyCommand">دستور کلیدی</param>
        public void ExecuteKeyCommand(string keyCommand)
        {
            var normalizedKey = keyCommand.Trim().ToLower();
            
            // حذف براکت‌ها اگر وجود دارند (مثل {ENTER} به enter)
            if (normalizedKey.StartsWith("{") && normalizedKey.EndsWith("}"))
            {
                normalizedKey = normalizedKey.Substring(1, normalizedKey.Length - 2).ToLower();
            }
            
            switch (normalizedKey)
            {
                case "enter":
                case "return":
                case "انتر":
                case "اینتر":
                    _inputSimulator.Keyboard.KeyPress(VirtualKeyCode.RETURN);
                    break;
                    
                case "tab":
                case "تب":
                    _inputSimulator.Keyboard.KeyPress(VirtualKeyCode.TAB);
                    break;
                    
                case "space":
                case "spacebar":
                case "فاصله":
                case "اسپیس":
                    _inputSimulator.Keyboard.KeyPress(VirtualKeyCode.SPACE);
                    break;
                    
                case "backspace":
                case "back":
                case "بک اسپیس":
                case "پاک کردن":
                    _inputSimulator.Keyboard.KeyPress(VirtualKeyCode.BACK);
                    break;
                    
                case "delete":
                case "del":
                case "حذف":
                case "دلیت":
                    _inputSimulator.Keyboard.KeyPress(VirtualKeyCode.DELETE);
                    break;
                    
                case "escape":
                case "esc":
                case "اسکیپ":
                case "خروج":
                    _inputSimulator.Keyboard.KeyPress(VirtualKeyCode.ESCAPE);
                    break;
                    
                case "home":
                    _inputSimulator.Keyboard.KeyPress(VirtualKeyCode.HOME);
                    break;
                    
                case "end":
                    _inputSimulator.Keyboard.KeyPress(VirtualKeyCode.END);
                    break;
                    
                case "pageup":
                case "pgup":
                    _inputSimulator.Keyboard.KeyPress(VirtualKeyCode.PRIOR);
                    break;
                    
                case "pagedown":
                case "pgdn":
                    _inputSimulator.Keyboard.KeyPress(VirtualKeyCode.NEXT);
                    break;
                    
                case "up":
                case "uparrow":
                    _inputSimulator.Keyboard.KeyPress(VirtualKeyCode.UP);
                    break;
                    
                case "down":
                case "downarrow":
                    _inputSimulator.Keyboard.KeyPress(VirtualKeyCode.DOWN);
                    break;
                    
                case "left":
                case "leftarrow":
                    _inputSimulator.Keyboard.KeyPress(VirtualKeyCode.LEFT);
                    break;
                    
                case "right":
                case "rightarrow":
                    _inputSimulator.Keyboard.KeyPress(VirtualKeyCode.RIGHT);
                    break;
                    
                case "ctrl+c":
                case "copy":
                    _inputSimulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_C);
                    break;
                    
                case "ctrl+v":
                case "paste":
                    _inputSimulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_V);
                    break;
                    
                case "ctrl+x":
                case "cut":
                    _inputSimulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_X);
                    break;
                    
                case "ctrl+z":
                case "undo":
                    _inputSimulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_Z);
                    break;
                    
                case "ctrl+y":
                case "redo":
                    _inputSimulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_Y);
                    break;
                    
                case "ctrl+a":
                case "selectall":
                    _inputSimulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_A);
                    break;
                    
                case "ctrl+s":
                case "save":
                    _inputSimulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_S);
                    break;
                    
                case "alt+tab":
                    _inputSimulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.MENU, VirtualKeyCode.TAB);
                    break;
                    
                case "alt+f4":
                    _inputSimulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.MENU, VirtualKeyCode.F4);
                    break;
                    
                default:
                    // سعی در پردازش دستورات پیچیده‌تر
                    ProcessComplexKeyCommand(normalizedKey);
                    break;
            }
        }
        
        /// <summary>
        /// پردازش دستورات کلیدی پیچیده
        /// </summary>
        /// <param name="keyCommand">دستور کلیدی</param>
        private void ProcessComplexKeyCommand(string keyCommand)
        {
            try
            {
                // اگر دستور شامل + باشد، به عنوان ترکیب کلید پردازش کن
                if (keyCommand.Contains("+"))
                {
                    var parts = keyCommand.Split('+');
                    if (parts.Length == 2)
                    {
                        var modifier = GetVirtualKeyCode(parts[0].Trim());
                        var key = GetVirtualKeyCode(parts[1].Trim());
                        
                        if (modifier.HasValue && key.HasValue)
                        {
                            _inputSimulator.Keyboard.ModifiedKeyStroke(modifier.Value, key.Value);
                            return;
                        }
                    }
                }
                
                // سعی در پردازش به عنوان کلید تکی
                var singleKey = GetVirtualKeyCode(keyCommand);
                if (singleKey.HasValue)
                {
                    _inputSimulator.Keyboard.KeyPress(singleKey.Value);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"خطا در پردازش دستور پیچیده: {ex.Message}");
            }
        }
        
        /// <summary>
        /// تبدیل نام کلید به VirtualKeyCode
        /// </summary>
        /// <param name="keyName">نام کلید</param>
        /// <returns>VirtualKeyCode مربوطه</returns>
        private VirtualKeyCode? GetVirtualKeyCode(string keyName)
        {
            var normalizedKey = keyName.Trim().ToLower();
            
            switch (normalizedKey)
            {
                case "ctrl":
                case "control":
                    return VirtualKeyCode.CONTROL;
                case "alt":
                    return VirtualKeyCode.MENU;
                case "shift":
                    return VirtualKeyCode.SHIFT;
                case "win":
                case "windows":
                    return VirtualKeyCode.LWIN;
                    
                // کلیدهای خاص
                case "capslock":
                case "capslk":
                case "caps":
                    return VirtualKeyCode.CAPITAL;
                case "enter":
                case "return":
                    return VirtualKeyCode.RETURN;
                case "tab":
                    return VirtualKeyCode.TAB;
                case "space":
                case "spacebar":
                    return VirtualKeyCode.SPACE;
                case "backspace":
                case "back":
                    return VirtualKeyCode.BACK;
                case "delete":
                case "del":
                    return VirtualKeyCode.DELETE;
                case "escape":
                case "esc":
                    return VirtualKeyCode.ESCAPE;
                case "home":
                    return VirtualKeyCode.HOME;
                case "end":
                    return VirtualKeyCode.END;
                case "pageup":
                case "pgup":
                    return VirtualKeyCode.PRIOR;
                case "pagedown":
                case "pgdn":
                    return VirtualKeyCode.NEXT;
                case "insert":
                case "ins":
                    return VirtualKeyCode.INSERT;
                case "up":
                case "uparrow":
                    return VirtualKeyCode.UP;
                case "down":
                case "downarrow":
                    return VirtualKeyCode.DOWN;
                case "left":
                case "leftarrow":
                    return VirtualKeyCode.LEFT;
                case "right":
                case "rightarrow":
                    return VirtualKeyCode.RIGHT;
                    
                // کلیدهای F
                case "f1": return VirtualKeyCode.F1;
                case "f2": return VirtualKeyCode.F2;
                case "f3": return VirtualKeyCode.F3;
                case "f4": return VirtualKeyCode.F4;
                case "f5": return VirtualKeyCode.F5;
                case "f6": return VirtualKeyCode.F6;
                case "f7": return VirtualKeyCode.F7;
                case "f8": return VirtualKeyCode.F8;
                case "f9": return VirtualKeyCode.F9;
                case "f10": return VirtualKeyCode.F10;
                case "f11": return VirtualKeyCode.F11;
                case "f12": return VirtualKeyCode.F12;
                
                // حروف
                case "a": return VirtualKeyCode.VK_A;
                case "b": return VirtualKeyCode.VK_B;
                case "c": return VirtualKeyCode.VK_C;
                case "d": return VirtualKeyCode.VK_D;
                case "e": return VirtualKeyCode.VK_E;
                case "f": return VirtualKeyCode.VK_F;
                case "g": return VirtualKeyCode.VK_G;
                case "h": return VirtualKeyCode.VK_H;
                case "i": return VirtualKeyCode.VK_I;
                case "j": return VirtualKeyCode.VK_J;
                case "k": return VirtualKeyCode.VK_K;
                case "l": return VirtualKeyCode.VK_L;
                case "m": return VirtualKeyCode.VK_M;
                case "n": return VirtualKeyCode.VK_N;
                case "o": return VirtualKeyCode.VK_O;
                case "p": return VirtualKeyCode.VK_P;
                case "q": return VirtualKeyCode.VK_Q;
                case "r": return VirtualKeyCode.VK_R;
                case "s": return VirtualKeyCode.VK_S;
                case "t": return VirtualKeyCode.VK_T;
                case "u": return VirtualKeyCode.VK_U;
                case "v": return VirtualKeyCode.VK_V;
                case "w": return VirtualKeyCode.VK_W;
                case "x": return VirtualKeyCode.VK_X;
                case "y": return VirtualKeyCode.VK_Y;
                case "z": return VirtualKeyCode.VK_Z;
                
                // اعداد
                case "0": return VirtualKeyCode.VK_0;
                case "1": return VirtualKeyCode.VK_1;
                case "2": return VirtualKeyCode.VK_2;
                case "3": return VirtualKeyCode.VK_3;
                case "4": return VirtualKeyCode.VK_4;
                case "5": return VirtualKeyCode.VK_5;
                case "6": return VirtualKeyCode.VK_6;
                case "7": return VirtualKeyCode.VK_7;
                case "8": return VirtualKeyCode.VK_8;
                case "9": return VirtualKeyCode.VK_9;
                
                default:
                    return null;
            }
        }

        /// <summary>
        /// تبدیل فرمت کلیدها به فرمت قابل فهم برای SendKeys
        /// </summary>
        /// <param name="keysCombination">ترکیب کلیدها</param>
        /// <returns>فرمت تبدیل شده</returns>
        private string FormatKeysForSendKeys(string keysCombination)
        {
            if (string.IsNullOrWhiteSpace(keysCombination))
                return string.Empty;

            // تبدیل فرمت‌های مختلف به فرمت SendKeys
            string formatted = keysCombination.ToUpper();

            // جایگزینی کلیدهای خاص
            formatted = formatted.Replace("CTRL+", "^")
                                .Replace("CONTROL+", "^")
                                .Replace("ALT+", "%")
                                .Replace("SHIFT+", "+")
                                .Replace("WIN+", "^{ESC}")
                                .Replace("WINDOWS+", "^{ESC}");

            // کلیدهای خاص
            formatted = formatted.Replace("ENTER", "{ENTER}")
                                .Replace("TAB", "{TAB}")
                                .Replace("ESC", "{ESC}")
                                .Replace("ESCAPE", "{ESC}")
                                .Replace("SPACE", " ")
                                .Replace("BACKSPACE", "{BACKSPACE}")
                                .Replace("DELETE", "{DELETE}")
                                .Replace("HOME", "{HOME}")
                                .Replace("END", "{END}")
                                .Replace("PAGEUP", "{PGUP}")
                                .Replace("PAGEDOWN", "{PGDN}")
                                .Replace("UP", "{UP}")
                                .Replace("DOWN", "{DOWN}")
                                .Replace("LEFT", "{LEFT}")
                                .Replace("RIGHT", "{RIGHT}");

            // کلیدهای F
            for (int i = 1; i <= 12; i++)
            {
                formatted = formatted.Replace($"F{i}", $"{{F{i}}}");
            }

            return formatted;
        }

        /// <summary>
        /// تغییر زبان کیبورد
        /// </summary>
        /// <param name="languageCode">کد زبان (مثل fa-IR, en-US)</param>
        public void ChangeLanguage(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
                return;

            try
            {
                // تبدیل کد زبان به شناسه کیبورد
                string keyboardId = GetKeyboardIdFromLanguageCode(languageCode);
                
                if (!string.IsNullOrEmpty(keyboardId))
                {
                    ChangeKeyboardLayout(keyboardId);
                }
                else
                {
                    // اگر کد زبان شناخته نشد، از Alt+Shift استفاده کن
                    SendKeys.SendWait("%+"); // Alt+Shift
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"خطا در تغییر زبان به {languageCode}: {ex.Message}");
                
                // در صورت خطا، از روش جایگزین استفاده کن
                try
                {
                    SendKeys.SendWait("%+"); // Alt+Shift
                }
                catch
                {
                    Debug.WriteLine("خطا در استفاده از Alt+Shift برای تغییر زبان");
                }
            }
        }

        /// <summary>
        /// تبدیل کد زبان به شناسه کیبورد
        /// </summary>
        /// <param name="languageCode">کد زبان</param>
        /// <returns>شناسه کیبورد</returns>
        private string GetKeyboardIdFromLanguageCode(string languageCode)
        {
            switch (languageCode.ToLower())
            {
                case "fa-ir":
                case "persian":
                case "farsi":
                    return "00000429"; // Persian (Iran)
                
                case "en-us":
                case "english":
                case "en":
                    return "00000409"; // English (United States)
                
                case "ar":
                case "arabic":
                    return "00000401"; // Arabic (Saudi Arabia)
                
                case "fr":
                case "french":
                    return "0000040c"; // French (France)
                
                case "de":
                case "german":
                    return "00000407"; // German (Germany)
                
                case "es":
                case "spanish":
                    return "0000040a"; // Spanish (Traditional Sort)
                
                default:
                    return null;
            }
        }

        /// <summary>
        /// تغییر چیدمان کیبورد با استفاده از Win32 API
        /// </summary>
        /// <param name="keyboardId">شناسه کیبورد</param>
        private void ChangeKeyboardLayout(string keyboardId)
        {
            try
            {
                // بارگذاری چیدمان کیبورد
                IntPtr hkl = LoadKeyboardLayout(keyboardId, KLF_ACTIVATE);
                
                if (hkl != IntPtr.Zero)
                {
                    // فعال‌سازی چیدمان کیبورد
                    ActivateKeyboardLayout(hkl, 0);
                    
                    // ارسال پیام تغییر زبان به پنجره فعال
                    IntPtr foregroundWindow = GetForegroundWindow();
                    if (foregroundWindow != IntPtr.Zero)
                    {
                        PostMessage(foregroundWindow, WM_INPUTLANGCHANGEREQUEST, 
                                  IntPtr.Zero, hkl);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"خطا در تغییر چیدمان کیبورد: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// دریافت زبان فعلی کیبورد
        /// </summary>
        /// <returns>شناسه زبان فعلی</returns>
        public string GetCurrentKeyboardLanguage()
        {
            try
            {
                IntPtr foregroundWindow = GetForegroundWindow();
                uint processId;
                uint threadId = GetWindowThreadProcessId(foregroundWindow, out processId);
                IntPtr keyboardLayout = GetKeyboardLayout(threadId);
                
                // تبدیل شناسه به کد زبان
                int layoutId = (int)(keyboardLayout.ToInt64() & 0xFFFF);
                
                switch (layoutId)
                {
                    case 0x0429:
                        return "fa-IR";
                    case 0x0409:
                        return "en-US";
                    case 0x0401:
                        return "ar";
                    case 0x040c:
                        return "fr";
                    case 0x0407:
                        return "de";
                    case 0x040a:
                        return "es";
                    default:
                        return "unknown";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"خطا در دریافت زبان فعلی: {ex.Message}");
                return "unknown";
            }
        }

        /// <summary>
        /// تاخیر کوتاه برای اطمینان از اجرای صحیح دستورات
        /// </summary>
        /// <param name="milliseconds">مدت تاخیر به میلی‌ثانیه</param>
        public void Wait(int milliseconds = 100)
        {
            if (milliseconds > 0)
            {
                Thread.Sleep(milliseconds);
            }
        }
    }
}