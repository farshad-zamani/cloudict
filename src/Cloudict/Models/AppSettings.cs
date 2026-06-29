using System.Collections.Generic;
using System.Linq;

namespace Cloudict
{
    /// <summary>
    /// کلاس تنظیمات برنامه که شامل تمام پارامترهای قابل تنظیم است
    /// </summary>
    public class AppSettings
    {
        // User Interface - رابط کاربری
        /// <summary>
        /// UI language code: "en" (English, default) or "fa" (Persian).
        /// Applied at startup; switching requires an application restart.
        /// زبان رابط کاربری: "en" انگلیسی (پیش‌فرض) یا "fa" فارسی
        /// </summary>
        public string UILanguage { get; set; } = "en";

        // Speech Engine - موتور تبدیل گفتار
        /// <summary>
        /// Selected speech-to-text engine. Currently only "GoogleTranslate" is active;
        /// other engines (GoogleApi, Whisper, …) are reserved for future use.
        /// </summary>
        public string SpeechEngine { get; set; } = "GoogleTranslate";

        /// <summary>
        /// The language the user dictates in (BCP-47-ish code, e.g. "fa", "en", "ar").
        /// Used to build the Google Translate URL and, later, other engines' configuration.
        /// </summary>
        public string TypingLanguage { get; set; } = "en";

        // Text Transfer Delays - تاخیرهای انتقال متن
        /// <summary>
        /// تاخیر پردازش متن (میلی‌ثانیه)
        /// </summary>
        public int ProcessDelayMs { get; set; } = 400;

        /// <summary>
        /// تاخیر انتقال کلمه به کلمه (میلی‌ثانیه)
        /// </summary>
        public int WordByWordDelayMs { get; set; } = 300;

        /// <summary>
        /// تاخیر شروع انتقال متن (میلی‌ثانیه)
        /// </summary>
        public int TransferStartDelayMs { get; set; } = 2000;

        /// <summary>
        /// مدت مکث برای ریست میکروفون (میلی‌ثانیه)
        /// </summary>
        public int InactivityDelayMs { get; set; } = 3500;

        // Google Translate Selectors - سلکتورهای گوگل ترنسلیت
        /// <summary>
        /// XPath دکمه میکروفون در گوگل ترنسلیت
        /// </summary>
        // The Google Translate voice button is a single toggle identified by a stable, language-
        // independent jsname (it carries the 'XiUwde' class while actively listening).
        public string MicButtonXPath { get; set; } = "//button[@jsname='Sz6qce']";

        /// <summary>
        /// لیست aria-label های باکس متن در گوگل ترنسلیت
        /// </summary>
        public List<string> TextBoxAriaLabels { get; set; } = new List<string>
        {
            // The Google Translate source box aria-label is language-specific (it changes with the
            // page language). This is only a hint; the class selectors + automatic textarea
            // detection are language-agnostic and normally locate the box on their own.
            "Source text"
        };

        /// <summary>
        /// لیست class selector های باکس متن در گوگل ترنسلیت
        /// </summary>
        public List<string> TextBoxClassSelectors { get; set; } = new List<string>
        {
            "er8xn",
            "QFw9Te"
        };

        // Browser Configuration - تنظیمات مرورگر
        /// <summary>
        /// تاخیر پیش‌بارگذاری گوگل ترنسلیت (میلی‌ثانیه)
        /// </summary>
        public int PreloadDelayMs { get; set; } = 3000;

        /// <summary>
        /// تاخیر فعال‌سازی میکروفون (میلی‌ثانیه)
        /// </summary>
        public int MicActivationDelayMs { get; set; } = 1000;

        // Global Shortcut Configuration - تنظیمات شورتکی سراسری
        /// <summary>
        /// کلید اصلی شورتکی اول (پیش‌فرض: A)
        /// </summary>
        public string ShortcutKey { get; set; } = "A";

        /// <summary>
        /// فعال بودن کلید Ctrl در شورتکی اول
        /// </summary>
        public bool ShortcutCtrl { get; set; } = true;

        /// <summary>
        /// فعال بودن کلید Shift در شورتکی اول
        /// </summary>
        public bool ShortcutShift { get; set; } = false;

        /// <summary>
        /// فعال بودن کلید Alt در شورتکی اول
        /// </summary>
        public bool ShortcutAlt { get; set; } = true;

        /// <summary>
        /// فعال بودن شورتکی سراسری
        /// </summary>
        public bool GlobalShortcutEnabled { get; set; } = true;

        // Second Global Shortcut Configuration - تنظیمات شورتکی دوم (Stop)
        /// <summary>
        /// کلید اصلی شورتکی دوم (پیش‌فرض: S)
        /// </summary>
        public string StopShortcutKey { get; set; } = "S";

        /// <summary>
        /// فعال بودن کلید Ctrl در شورتکی دوم
        /// </summary>
        public bool StopShortcutCtrl { get; set; } = true;

        /// <summary>
        /// فعال بودن کلید Shift در شورتکی دوم
        /// </summary>
        public bool StopShortcutShift { get; set; } = false;

        /// <summary>
        /// فعال بودن کلید Alt در شورتکی دوم
        /// </summary>
        public bool StopShortcutAlt { get; set; } = true;

        /// <summary>
        /// مینیمایز کردن برنامه به سیستم ترای به جای تسک بار
        /// </summary>
        public bool MinimizeToTray { get; set; } = false;

        // Voice Commands Configuration - تنظیمات دستورات صوتی
        /// <summary>
        /// لیست دستورات صوتی تعریف شده توسط کاربر
        /// </summary>
        public List<VoiceCommand> VoiceCommands { get; set; } = new List<VoiceCommand>();

        /// <summary>
        /// Voice commands stored per typing/dictation language (e.g. "fa", "en").
        /// The active set is chosen by <see cref="TypingLanguage"/>.
        /// </summary>
        public Dictionary<string, List<VoiceCommand>> VoiceCommandSets { get; set; } = new Dictionary<string, List<VoiceCommand>>();

        /// <summary>
        /// فعال بودن سیستم دستورات صوتی
        /// </summary>
        public bool EnableVoiceCommands { get; set; } = true;

        /// <summary>
        /// حساسیت به حروف بزرگ و کوچک در دستورات
        /// </summary>
        public bool CaseSensitiveCommands { get; set; } = false;

        /// <summary>
        /// تاخیر تشخیص دستور (میلی‌ثانیه)
        /// </summary>
        public int CommandDetectionDelay { get; set; } = 50;

        /// <summary>
        /// سازنده پیش‌فرض که مقادیر اولیه را تنظیم می‌کند
        /// </summary>
        public AppSettings()
        {
            // مقادیر پیش‌فرض در بالا تنظیم شده‌اند
            // دستورات صوتی را فقط در صورت null بودن مقداردهی کن
            if (VoiceCommands == null)
            {
                VoiceCommands = new List<VoiceCommand>();
            }
        }

        /// <summary>
        /// بررسی اعتبار تنظیمات
        /// </summary>
        /// <returns>true اگر تنظیمات معتبر باشند</returns>
        public bool IsValid()
        {
            // بررسی محدوده مقادیر عددی (حداقل 50ms، حداکثر 10000ms)
            if (ProcessDelayMs < 50 || ProcessDelayMs > 10000) return false;
            if (WordByWordDelayMs < 50 || WordByWordDelayMs > 10000) return false;
            if (TransferStartDelayMs < 50 || TransferStartDelayMs > 10000) return false;
            if (InactivityDelayMs < 50 || InactivityDelayMs > 10000) return false;
            if (PreloadDelayMs < 50 || PreloadDelayMs > 10000) return false;
            if (MicActivationDelayMs < 50 || MicActivationDelayMs > 10000) return false;

            // بررسی وجود سلکتورها
            if (string.IsNullOrWhiteSpace(MicButtonXPath)) return false;
            if (TextBoxAriaLabels == null || TextBoxAriaLabels.Count == 0) return false;
            if (TextBoxClassSelectors == null || TextBoxClassSelectors.Count == 0) return false;

            // بررسی تنظیمات دستورات صوتی
            if (CommandDetectionDelay < 10 || CommandDetectionDelay > 1000) return false;

            return true;
        }

        /// <summary>
        /// دریافت لیست دستورات پیش‌فرض
        /// </summary>
        /// <returns>لیست دستورات صوتی پیش‌فرض</returns>
        public static List<VoiceCommand> GetDefaultCommands()
        {
            var commands = new List<VoiceCommand>
            {
                // دستورات تایپی - علائم نگارشی
                new VoiceCommand(1, "دو نقطه", CommandActionType.TypeText, ":"),
                new VoiceCommand(2, "ویرگول", CommandActionType.TypeText, "،"),
                new VoiceCommand(3, "نقطش", CommandActionType.TypeText, "."),
                new VoiceCommand(4, "علامت سوال", CommandActionType.TypeText, "؟"),
                new VoiceCommand(5, "علامت تعجب", CommandActionType.TypeText, "!"),
                new VoiceCommand(6, "نقطه‌ ویر", CommandActionType.TypeText, "؛"),
                new VoiceCommand(7, "خط تیره", CommandActionType.TypeText, "ـ"),
                new VoiceCommand(8, "پرانتز باز", CommandActionType.TypeText, "("),
                new VoiceCommand(9, "پرانتز بسته", CommandActionType.TypeText, ")"),
                
                // دستورات کلیدی (تک کلمه)
                new VoiceCommand(10, "اینتر", CommandActionType.SendKeys, "Enter"),
                new VoiceCommand(11, "تبش", CommandActionType.SendKeys, "Tab"),
                new VoiceCommand(12, "بک بک", CommandActionType.SendKeys, "Backspace"),
                new VoiceCommand(13, "اسپیس", CommandActionType.SendKeys, "Space"),
                new VoiceCommand(14, "دلیت", CommandActionType.SendKeys, "Delete"),
                
                // دستورات تغییر زبان (تک کلمه)
                new VoiceCommand(15, "فارسیش", CommandActionType.ChangeToFarsi, "fa-IR"),
                new VoiceCommand(16, "انگلیش", CommandActionType.ChangeToEnglish, "en-US"),
                
                // دستور حذف کلمات آخر
                new VoiceCommand(17, "پاپاک", CommandActionType.SendKeys, "Ctrl+Backspace")
            };
            
            return commands;
        }

        /// <summary>
        /// Default command set for a language. Persian ships with a full ready-made set; every
        /// other language (including English) starts empty so the user creates their own commands
        /// (guided by the help in the Add Voice Command window).
        /// </summary>
        public static List<VoiceCommand> GetDefaultCommandsForLanguage(string lang)
        {
            lang = string.IsNullOrWhiteSpace(lang) ? "fa" : lang.ToLowerInvariant();
            if (lang == "fa") return GetDefaultCommands();
            return new List<VoiceCommand>();
        }

        /// <summary>Returns (and lazily seeds) the voice-command set for the given language.</summary>
        public List<VoiceCommand> GetVoiceCommandsFor(string lang)
        {
            lang = string.IsNullOrWhiteSpace(lang) ? "fa" : lang.ToLowerInvariant();
            if (VoiceCommandSets == null) VoiceCommandSets = new Dictionary<string, List<VoiceCommand>>();

            if (!VoiceCommandSets.TryGetValue(lang, out var list) || list == null)
            {
                // Migrate any legacy flat list into Persian on first run; otherwise seed defaults.
                if (lang == "fa" && VoiceCommands != null && VoiceCommands.Count > 0)
                    list = new List<VoiceCommand>(VoiceCommands);
                else
                    list = GetDefaultCommandsForLanguage(lang);
                VoiceCommandSets[lang] = list;
            }
            return list;
        }

        /// <summary>Stores the voice-command set for the given language.</summary>
        public void SetVoiceCommandsFor(string lang, List<VoiceCommand> list)
        {
            lang = string.IsNullOrWhiteSpace(lang) ? "fa" : lang.ToLowerInvariant();
            if (VoiceCommandSets == null) VoiceCommandSets = new Dictionary<string, List<VoiceCommand>>();
            VoiceCommandSets[lang] = list ?? new List<VoiceCommand>();
        }
    }
}