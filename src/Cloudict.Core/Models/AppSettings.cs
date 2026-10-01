using System;
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
        public int ProcessDelayMs { get; set; } = 600;

        /// <summary>
        /// تاخیر انتقال کلمه به کلمه (میلی‌ثانیه)
        /// </summary>
        public int WordByWordDelayMs { get; set; } = 700;

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

        /// <summary>
        /// Whether the microphone badge appears in the corner of the screen. On by default: the
        /// helper browser and the application being dictated into normally cover Cloudict's own
        /// window, so this is the only indication that speaking will produce text.
        /// </summary>
        public bool ShowStatusIndicator { get; set; } = true;

        /// <summary>
        /// Whether words are typed straight into the focused application rather than collected in
        /// Cloudict's own box.
        ///
        /// <para>Remembered between runs. It is the one control on the main window that decides
        /// where dictation actually goes, and someone who works this way works this way every time —
        /// having it come back off meant the first sentence of every session went into the wrong
        /// place.</para>
        /// </summary>
        public bool LiveTransferEnabled { get; set; } = false;

        /// <summary>
        /// Whether the helper browser opens by itself when Cloudict starts.
        ///
        /// <para>On by default: nothing can be dictated until it is open, so making the user press a
        /// button first is a step with only one sensible answer. It can be switched off for a
        /// machine where Chrome should not launch unasked.</para>
        /// </summary>
        public bool OpenBrowserOnStartup { get; set; } = true;

        /// <summary>
        /// Whether the speech engine listens to the machine's own audio instead of the microphone.
        ///
        /// <para>Remembered between runs, but re-checked at every start: the helper it depends on can
        /// be uninstalled between one run and the next, and a button left on over a mode that is not
        /// actually running would be worse than one that resets.</para>
        /// </summary>
        public bool SystemAudioEnabled { get; set; } = false;

        /// <summary>
        /// Whether Cloudict asks GitHub, once a day, whether a newer release exists.
        ///
        /// <para>On by default, and read-only: it reports what is available and links to it, never
        /// downloads or installs anything by itself. Turning it off stops the request entirely for
        /// anyone who would rather the application did not reach the network on its own.</para>
        /// </summary>
        public bool CheckForUpdates { get; set; } = true;

        /// <summary>When the last check ran, so it happens once a day rather than at every launch.</summary>
        public DateTime LastUpdateCheck { get; set; } = DateTime.MinValue;

        /// <summary>A version the user chose to pass over, so the same bar does not keep returning.</summary>
        public string SkippedUpdateVersion { get; set; } = "";

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
            return NormaliseLanguage(lang) == "fa" ? GetDefaultCommands() : new List<VoiceCommand>();
        }

        /// <summary>
        /// Set once the pre-3.0 flat <see cref="VoiceCommands"/> list has been folded into
        /// <see cref="VoiceCommandSets"/>. After that the flat list is never read again.
        /// </summary>
        public bool LegacyVoiceCommandsMigrated { get; set; }

        /// <summary>
        /// Folds the pre-3.0 flat command list into the per-language sets, exactly once.
        ///
        /// <para>Before this, that one list was doing three contradictory jobs. The settings loader
        /// refilled it with the Persian defaults whenever it was empty; the command manager mirrored
        /// whichever language was active into it; and the per-language lookup treated anything in it
        /// as Persian commands awaiting migration. Together that meant English commands could be
        /// adopted as Persian ones, deleted Persian commands came back on the next launch, and what
        /// a language showed depended on which language had last been active rather than on what
        /// the user had set up for it.</para>
        ///
        /// <para>A file with no per-language sets at all is from before 3.0, when commands were
        /// Persian-only, so its list is Persian. In a 3.x file the list was a mirror of whichever
        /// language was active, and is worth anything only when it holds Persian commands that the
        /// Persian set has lost — the state the old settings-window bug left behind. Whether the
        /// list is Persian is decided by its content: letters that exist in Persian and not in
        /// English or Arabic. That is what keeps an English user's commands out of the Persian set,
        /// which the old rule — "anything in the flat list is Persian" — let through.</para>
        ///
        /// <para>A Persian set that already holds commands is never touched.</para>
        /// </summary>
        public void MigrateLegacyVoiceCommands()
        {
            if (LegacyVoiceCommandsMigrated) return;
            LegacyVoiceCommandsMigrated = true;

            if (VoiceCommandSets == null) VoiceCommandSets = new Dictionary<string, List<VoiceCommand>>();

            var legacy = VoiceCommands;
            VoiceCommands = new List<VoiceCommand>();
            if (legacy == null || legacy.Count == 0) return;

            var preThreeZero = VoiceCommandSets.Count == 0;
            var looksPersian = legacy.Any(c => ContainsPersianLetter(c?.Phrase));

            VoiceCommandSets.TryGetValue("fa", out var persian);
            var persianEmpty = persian == null || persian.Count == 0;

            if (preThreeZero || (persianEmpty && looksPersian))
                VoiceCommandSets["fa"] = legacy.Where(c => c != null).Select(c => c.Clone()).ToList();
        }

        /// <summary>پ چ ژ گ and the Persian forms of ک and ی — absent from English and Arabic.</summary>
        internal static bool ContainsPersianLetter(string text) =>
            !string.IsNullOrEmpty(text) &&
            text.IndexOfAny(new[] { 'پ', 'چ', 'ژ', 'گ', 'ک', 'ی' }) >= 0;

        /// <summary>
        /// Returns the voice-command set for a language, seeding it with that language's defaults
        /// the first time it is asked for.
        ///
        /// <para>Only a language that has never had a set is seeded. A set that exists but is empty
        /// is one the user emptied, and stays empty.</para>
        /// </summary>
        public List<VoiceCommand> GetVoiceCommandsFor(string lang)
        {
            lang = NormaliseLanguage(lang);
            MigrateLegacyVoiceCommands();

            if (!VoiceCommandSets.TryGetValue(lang, out var list) || list == null)
            {
                list = GetDefaultCommandsForLanguage(lang);
                VoiceCommandSets[lang] = list;
            }

            return list;
        }

        /// <summary>Stores the voice-command set for the given language.</summary>
        public void SetVoiceCommandsFor(string lang, List<VoiceCommand> list)
        {
            lang = NormaliseLanguage(lang);
            MigrateLegacyVoiceCommands();
            VoiceCommandSets[lang] = list ?? new List<VoiceCommand>();
        }

        private static string NormaliseLanguage(string lang) =>
            string.IsNullOrWhiteSpace(lang) ? "en" : lang.Trim().ToLowerInvariant();
    }
}