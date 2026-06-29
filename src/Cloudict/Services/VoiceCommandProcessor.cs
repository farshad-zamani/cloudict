using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cloudict
{
    /// <summary>
    /// نتیجه پردازش دستورات صوتی
    /// </summary>
    public class CommandProcessResult
    {
        public string OriginalText { get; set; }
        public string ProcessedText { get; set; }
        public List<VoiceCommand> CommandsExecuted { get; set; }
        public bool HasCommands { get; set; }
        public bool CommandExecuted => CommandsExecuted.Count > 0;

        public CommandProcessResult()
        {
            CommandsExecuted = new List<VoiceCommand>();
        }
    }

    /// <summary>
    /// پردازشگر دستورات صوتی با قابلیت ردیابی کلمات جدید
    /// </summary>
    public class VoiceCommandProcessor
    {
        private readonly List<VoiceCommand> _commands;
        private readonly SystemCommandExecutor _systemExecutor;
        private readonly bool _caseSensitive;
        private readonly WordTracker _wordTracker;

        // Phrase regex is built per command on every Tick; caching avoids re-parsing
        // and lets us flip on RegexOptions.Compiled for the hot path.
        private static readonly ConcurrentDictionary<string, Regex> _phraseRegexCache =
            new ConcurrentDictionary<string, Regex>();
        private static readonly Regex _whitespaceRegex =
            new Regex(@"\s+", RegexOptions.Compiled);

        private Regex GetPhraseRegex(string commandPhrase)
        {
            string key = (_caseSensitive ? "cs:" : "ci:") + commandPhrase;
            return _phraseRegexCache.GetOrAdd(key, _ =>
            {
                var options = RegexOptions.Compiled | (_caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
                return new Regex($@"\b{Regex.Escape(commandPhrase)}\b", options);
            });
        }

        /// <summary>
        /// سازنده پردازشگر دستورات صوتی
        /// </summary>
        /// <param name="commands">لیست دستورات فعال</param>
        /// <param name="caseSensitive">حساسیت به حروف بزرگ و کوچک</param>
        public VoiceCommandProcessor(List<VoiceCommand> commands, bool caseSensitive = false)
        {
            _commands = commands?.Where(c => c.IsEnabled).ToList() ?? new List<VoiceCommand>();
            _systemExecutor = new SystemCommandExecutor();
            _caseSensitive = caseSensitive;
            _wordTracker = new WordTracker();
        }

        /// <summary>
        /// پردازش متن ورودی برای تشخیص و جایگزینی کلمات دستوری با عبارات تعریف شده
        /// </summary>
        /// <param name="inputText">متن ورودی</param>
        /// <returns>نتیجه پردازش شامل متن پردازش شده با کلمات جایگزین شده</returns>
        public CommandProcessResult ProcessText(string inputText)
        {
            var result = new CommandProcessResult
            {
                OriginalText = inputText,
                ProcessedText = inputText,
                CommandsExecuted = new List<VoiceCommand>()
            };

            if (string.IsNullOrWhiteSpace(inputText) || _commands.Count == 0)
            {
                return result;
            }

            // بررسی تغییرات در متن برای تشخیص عبارات جدید
            var hasNewContent = _wordTracker.HasNewContent(inputText);
            
            if (!hasNewContent)
            {
                // اگر محتوای جدیدی نیست، فقط متن را برگردان
                return result;
            }

            // مرتب‌سازی دستورات بر اساس تعداد کلمات (بیشتر اول) سپس طول عبارت برای اولویت‌بندی بهتر
            var sortedCommands = _commands
                .OrderByDescending(c => c.Phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length)
                .ThenByDescending(c => c.Phrase.Length)
                .ToList();

            // لیست دستورات اجرا شده برای جلوگیری از تکرار
            var executedCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var commandsToExecute = new List<VoiceCommand>();

            // ابتدا تمام دستورات موجود در متن را شناسایی کن
            foreach (var command in sortedCommands)
            {
                // بررسی اینکه دستور قبلاً اجرا نشده باشد و در متن وجود دارد
                if (!executedCommands.Contains(command.Phrase) && 
                    _wordTracker.IsCommandNew(command.Phrase) && 
                    ContainsCommand(result.ProcessedText, command.Phrase))
                {
                    commandsToExecute.Add(command);
                    result.CommandsExecuted.Add(command);
                    
                    // اضافه کردن به لیست دستورات اجرا شده
                    executedCommands.Add(command.Phrase);
                    
                    // علامت‌گذاری دستور به عنوان اجرا شده
                    _wordTracker.MarkCommandAsExecuted(command.Phrase);
                }
            }

            // جایگزینی و اجرای دستورات در متن
            foreach (var command in commandsToExecute)
            {
                if (command.ActionType == CommandActionType.TypeText && !string.IsNullOrWhiteSpace(command.ActionValue))
                {
                    result.ProcessedText = ReplaceCommandWord(result.ProcessedText, command.Phrase, command.ActionValue);
                }
                else
                {
                    // حذف دستور از متن
                    result.ProcessedText = RemoveCommandFromText(result.ProcessedText, command.Phrase);
                    
                    // اجرای دستورات سیستمی
                    ExecuteSystemCommand(command);
                }
            }

            // پاک‌سازی متن نهایی
            result.ProcessedText = CleanupText(result.ProcessedText);

            return result;
        }



        /// <summary>
        /// دریافت متن جایگزین برای کلمه دستوری
        /// </summary>
        /// <param name="commandPhrase">عبارت دستور</param>
        /// <returns>متن جایگزین یا null اگر دستور یافت نشد</returns>
        private string GetReplacementText(string commandPhrase)
        {
            if (string.IsNullOrWhiteSpace(commandPhrase))
                return null;

            var command = _commands
                .FirstOrDefault(c => c.IsEnabled && 
                    string.Equals(c.Phrase, commandPhrase, 
                        _caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));

            if (command != null && command.ActionType == CommandActionType.TypeText)
            {
                return command.ActionValue;
            }

            return null;
        }

        /// <summary>
        /// جایگزینی کلمه دستوری با متن تعریف شده
        /// </summary>
        /// <param name="text">متن اصلی</param>
        /// <param name="commandPhrase">عبارت دستور</param>
        /// <param name="replacementText">متن جایگزین</param>
        /// <returns>متن با کلمه جایگزین شده</returns>
        private string ReplaceCommandWord(string text, string commandPhrase, string replacementText)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(commandPhrase))
                return text;

            var regex = GetPhraseRegex(commandPhrase);
            string result = regex.Replace(text, replacementText ?? "");

            // پاک کردن فاصله‌های اضافی
            result = _whitespaceRegex.Replace(result, " ").Trim();

            return result;
        }

        /// <summary>
        /// بررسی وجود دستور در متن
        /// </summary>
        /// <param name="text">متن مورد بررسی</param>
        /// <param name="commandPhrase">عبارت دستور</param>
        /// <returns>true اگر دستور در متن موجود باشد</returns>
        private bool ContainsCommand(string text, string commandPhrase)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(commandPhrase))
                return false;

            var comparison = _caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            
            // تقسیم متن و دستور به کلمات
            var words = text.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var commandWords = commandPhrase.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            if (commandWords.Length == 0) return false;

            // جستجوی الگوی دستور در متن
            for (int i = 0; i <= words.Length - commandWords.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < commandWords.Length; j++)
                {
                    if (!string.Equals(words[i + j], commandWords[j], comparison))
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return true;
            }

            return false;
        }

        /// <summary>
        /// حذف دستور از متن
        /// </summary>
        /// <param name="text">متن اصلی</param>
        /// <param name="commandPhrase">عبارت دستور برای حذف</param>
        /// <returns>متن بدون دستور</returns>
        private string RemoveCommandFromText(string text, string commandPhrase)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(commandPhrase))
                return text;

            var regex = GetPhraseRegex(commandPhrase);
            string result = regex.Replace(text, "");

            // پاک کردن فاصله‌های اضافی
            result = _whitespaceRegex.Replace(result, " ").Trim();

            return result;
        }



        /// <summary>
        /// پاک‌سازی متن از فاصله‌های اضافی
        /// </summary>
        /// <param name="text">متن برای پاک‌سازی</param>
        /// <returns>متن پاک‌سازی شده</returns>
        private string CleanupText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // حذف فاصله‌های اضافی
            text = _whitespaceRegex.Replace(text, " ");

            return text.Trim();
        }

        /// <summary>
        /// بررسی اینکه آیا متن شامل دستور است یا خیر
        /// </summary>
        /// <param name="text">متن مورد بررسی</param>
        /// <returns>true اگر متن شامل حداقل یک دستور باشد</returns>
        public bool HasAnyCommand(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || _commands.Count == 0)
                return false;

            return _commands.Any(command => ContainsCommand(text, command.Phrase));
        }

        /// <summary>
        /// بررسی اینکه آیا یک کلمه واحد دستور است یا خیر
        /// </summary>
        /// <param name="word">کلمه مورد بررسی</param>
        /// <returns>true اگر کلمه دستور باشد</returns>
        public bool IsSingleWordCommand(string word)
        {
            if (string.IsNullOrWhiteSpace(word) || _commands.Count == 0)
                return false;

            var comparison = _caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            
            // بررسی دستورات تک کلمه‌ای
            return _commands.Any(command => 
                command.IsEnabled && 
                !command.Phrase.Contains(" ") && // فقط دستورات تک کلمه‌ای
                string.Equals(word.Trim(), command.Phrase.Trim(), comparison));
        }

        /// <summary>
        /// جایگزینی کلمه دستوری تک‌کلمه‌ای
        /// </summary>
        /// <param name="word">کلمه دستور</param>
        /// <returns>متن جایگزین یا کلمه اصلی اگر دستور نباشد</returns>
        public string ReplaceSingleWordCommand(string word)
        {
            if (string.IsNullOrWhiteSpace(word) || _commands.Count == 0)
                return word;

            var comparison = _caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            
            // پیدا کردن دستور مطابق
            var command = _commands.FirstOrDefault(c => 
                c.IsEnabled && 
                !c.Phrase.Contains(" ") && // فقط دستورات تک کلمه‌ای
                string.Equals(word.Trim(), c.Phrase.Trim(), comparison));

            if (command != null && command.ActionType == CommandActionType.TypeText && !string.IsNullOrWhiteSpace(command.ActionValue))
            {
                return command.ActionValue;
            }

            return word;
        }

        /// <summary>
        /// ریست کردن تاریخچه کلمات (برای شروع جلسه جدید)
        /// </summary>
        public void ResetWordHistory()
        {
            _wordTracker.Reset();
        }

        /// <summary>
        /// اجرای دستورات سیستمی
        /// </summary>
        /// <param name="command">دستور برای اجرا</param>
        private void ExecuteSystemCommand(VoiceCommand command)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.ActionValue))
                return;

            try
            {
                switch (command.ActionType)
                {
                    case CommandActionType.SendKeys:
                        _systemExecutor.ExecuteKeyCommand(command.ActionValue);
                        break;
                        
                    case CommandActionType.ChangeToFarsi:
                        _systemExecutor.ChangeLanguage("fa-IR");
                        break;
                        
                    case CommandActionType.ChangeToEnglish:
                        _systemExecutor.ChangeLanguage("en-US");
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"خطا در اجرای دستور سیستمی {command.Phrase}: {ex.Message}");
            }
        }

    }
}