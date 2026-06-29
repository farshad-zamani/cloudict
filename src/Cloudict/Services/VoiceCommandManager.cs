using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloudict
{
    /// <summary>
    /// مدیر دستورات صوتی که عملیات CRUD را بر روی دستورات انجام می‌دهد
    /// </summary>
    public class VoiceCommandManager
    {
        private readonly AppSettings _settings;
        private List<VoiceCommand> _commands = new List<VoiceCommand>();

        /// <summary>
        /// رویداد تغییر در لیست دستورات
        /// </summary>
        public event EventHandler CommandsChanged;

        /// <summary>
        /// دریافت لیست تمام دستورات
        /// </summary>
        public List<VoiceCommand> Commands => _commands?.ToList() ?? new List<VoiceCommand>();

        /// <summary>
        /// دریافت لیست دستورات فعال
        /// </summary>
        public List<VoiceCommand> ActiveCommands => _commands?.Where(c => c.IsEnabled).ToList() ?? new List<VoiceCommand>();

        /// <summary>
        /// تعداد کل دستورات
        /// </summary>
        public int TotalCount => _commands?.Count ?? 0;

        /// <summary>
        /// تعداد دستورات فعال
        /// </summary>
        public int ActiveCount => _commands?.Count(c => c.IsEnabled) ?? 0;

        /// <summary>
        /// سازنده مدیر دستورات صوتی
        /// </summary>
        /// <param name="settings">تنظیمات برنامه</param>
        public VoiceCommandManager(AppSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            LoadCommands();
        }

        /// <summary>
        /// بارگذاری دستورات از تنظیمات
        /// </summary>
        public void LoadCommands()
        {
            _commands.Clear();

            // Use the command set for the active typing/dictation language.
            var active = _settings.GetVoiceCommandsFor(_settings.TypingLanguage);
            _settings.VoiceCommands = active; // keep the flat list mirrored to the active language
            _commands.AddRange(active);

            OnCommandsChanged();
        }

        /// <summary>
        /// ذخیره دستورات در تنظیمات
        /// </summary>
        private void SaveCommands()
        {
            _settings.VoiceCommands = _commands;
            _settings.SetVoiceCommandsFor(_settings.TypingLanguage, _commands);
            OnCommandsChanged();
        }

        /// <summary>
        /// دریافت شناسه بعدی برای دستور جدید
        /// </summary>
        /// <returns>شناسه یکتا</returns>
        private int GetNextId()
        {
            return _commands.Count > 0 ? _commands.Max(c => c.Id) + 1 : 1;
        }

        /// <summary>
        /// اضافه کردن دستور جدید
        /// </summary>
        /// <param name="phrase">عبارت دستور</param>
        /// <param name="actionType">نوع عمل</param>
        /// <param name="actionValue">مقدار عمل</param>
        /// <param name="isEnabled">وضعیت فعال بودن</param>
        /// <returns>دستور ایجاد شده</returns>
        public VoiceCommand AddCommand(string phrase, CommandActionType actionType, string actionValue, bool isEnabled = true)
        {
            if (string.IsNullOrWhiteSpace(phrase))
                throw new ArgumentException("عبارت دستور نمی‌تواند خالی باشد", nameof(phrase));

            // بررسی تکراری نبودن عبارت
            if (_commands.Any(c => string.Equals(c.Phrase, phrase.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"دستور با عبارت '{phrase}' قبلاً وجود دارد");

            var command = new VoiceCommand
            {
                Id = GetNextId(),
                Phrase = phrase.Trim(),
                ActionType = actionType,
                ActionValue = actionValue?.Trim() ?? string.Empty,
                IsEnabled = isEnabled,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            _commands.Add(command);
            SaveCommands();

            return command;
        }

        /// <summary>
        /// به‌روزرسانی دستور موجود
        /// </summary>
        /// <param name="commandId">شناسه دستور</param>
        /// <param name="phrase">عبارت جدید</param>
        /// <param name="actionType">نوع عمل جدید</param>
        /// <param name="actionValue">مقدار عمل جدید</param>
        /// <param name="isEnabled">وضعیت فعال بودن</param>
        /// <returns>true اگر به‌روزرسانی موفق باشد</returns>
        public bool UpdateCommand(int commandId, string phrase, CommandActionType actionType, string actionValue, bool isEnabled)
        {
            var command = _commands.FirstOrDefault(c => c.Id == commandId);
            if (command == null)
                return false;

            if (string.IsNullOrWhiteSpace(phrase))
                throw new ArgumentException("عبارت دستور نمی‌تواند خالی باشد", nameof(phrase));

            // بررسی تکراری نبودن عبارت (به جز خود دستور فعلی)
            if (_commands.Any(c => c.Id != commandId && string.Equals(c.Phrase, phrase.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"دستور با عبارت '{phrase}' قبلاً وجود دارد");

            command.Phrase = phrase.Trim();
            command.ActionType = actionType;
            command.ActionValue = actionValue?.Trim() ?? string.Empty;
            command.IsEnabled = isEnabled;
            command.UpdatedAt = DateTime.Now;

            SaveCommands();
            return true;
        }

        /// <summary>
        /// حذف دستور
        /// </summary>
        /// <param name="commandId">شناسه دستور</param>
        /// <returns>true اگر حذف موفق باشد</returns>
        public bool DeleteCommand(int commandId)
        {
            var command = _commands.FirstOrDefault(c => c.Id == commandId);
            if (command == null)
                return false;

            _commands.Remove(command);
            SaveCommands();
            return true;
        }

        /// <summary>
        /// دریافت دستور بر اساس شناسه
        /// </summary>
        /// <param name="commandId">شناسه دستور</param>
        /// <returns>دستور یافت شده یا null</returns>
        public VoiceCommand GetCommand(int commandId)
        {
            return _commands.FirstOrDefault(c => c.Id == commandId);
        }

        /// <summary>
        /// جستجوی دستورات بر اساس عبارت
        /// </summary>
        /// <param name="searchTerm">عبارت جستجو</param>
        /// <returns>لیست دستورات یافت شده</returns>
        public List<VoiceCommand> SearchCommands(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
                return Commands;

            var term = searchTerm.Trim().ToLower();
            return _commands.Where(c => 
                c.Phrase.ToLower().Contains(term) ||
                c.ActionValue.ToLower().Contains(term) ||
                c.ActionType.ToString().ToLower().Contains(term)
            ).ToList();
        }

        /// <summary>
        /// فعال/غیرفعال کردن دستور
        /// </summary>
        /// <param name="commandId">شناسه دستور</param>
        /// <param name="isEnabled">وضعیت فعال بودن</param>
        /// <returns>true اگر تغییر موفق باشد</returns>
        public bool ToggleCommand(int commandId, bool isEnabled)
        {
            var command = _commands.FirstOrDefault(c => c.Id == commandId);
            if (command == null)
                return false;

            command.IsEnabled = isEnabled;
            command.UpdatedAt = DateTime.Now;
            SaveCommands();
            return true;
        }

        /// <summary>
        /// فعال/غیرفعال کردن همه دستورات
        /// </summary>
        /// <param name="isEnabled">وضعیت فعال بودن</param>
        public void ToggleAllCommands(bool isEnabled)
        {
            foreach (var command in _commands)
            {
                command.IsEnabled = isEnabled;
                command.UpdatedAt = DateTime.Now;
            }
            SaveCommands();
        }

        /// <summary>
        /// بازنشانی دستورات به حالت پیش‌فرض
        /// </summary>
        public void ResetToDefaults()
        {
            _commands.Clear();
            _commands.AddRange(AppSettings.GetDefaultCommands());
            SaveCommands();
        }

        /// <summary>
        /// بازخوانی دستورات از تنظیمات
        /// </summary>
        public void RefreshCommands()
        {
            LoadCommands();
            OnCommandsChanged();
        }

        /// <summary>
        /// دریافت دستورات بر اساس نوع عمل
        /// </summary>
        /// <param name="actionType">نوع عمل</param>
        /// <returns>لیست دستورات با نوع عمل مشخص</returns>
        public List<VoiceCommand> GetCommandsByActionType(CommandActionType actionType)
        {
            return _commands.Where(c => c.ActionType == actionType).ToList();
        }

        /// <summary>
        /// بررسی وجود دستور با عبارت مشخص
        /// </summary>
        /// <param name="phrase">عبارت دستور</param>
        /// <param name="excludeId">شناسه دستور برای استثنا (اختیاری)</param>
        /// <returns>true اگر دستور وجود داشته باشد</returns>
        public bool CommandExists(string phrase, int? excludeId = null)
        {
            if (string.IsNullOrWhiteSpace(phrase))
                return false;

            return _commands.Any(c => 
                (excludeId == null || c.Id != excludeId) &&
                string.Equals(c.Phrase, phrase.Trim(), StringComparison.OrdinalIgnoreCase)
            );
        }

        /// <summary>
        /// دریافت آمار دستورات
        /// </summary>
        /// <returns>آمار دستورات</returns>
        public Dictionary<string, int> GetStatistics()
        {
            var stats = new Dictionary<string, int>
            {
                ["Total"] = TotalCount,
                ["Active"] = ActiveCount,
                ["Inactive"] = TotalCount - ActiveCount
            };

            // آمار بر اساس نوع عمل
            var actionTypes = Enum.GetValues(typeof(CommandActionType)).Cast<CommandActionType>();
            foreach (var actionType in actionTypes)
            {
                var count = _commands.Count(c => c.ActionType == actionType);
                if (count > 0)
                {
                    stats[actionType.ToString()] = count;
                }
            }

            return stats;
        }

        /// <summary>
        /// اعلام تغییر در دستورات
        /// </summary>
        protected virtual void OnCommandsChanged()
        {
            CommandsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// اعتبارسنجی دستور
        /// </summary>
        /// <param name="command">دستور برای اعتبارسنجی</param>
        /// <returns>لیست خطاهای اعتبارسنجی</returns>
        public List<string> ValidateCommand(VoiceCommand command)
        {
            var errors = new List<string>();

            if (command == null)
            {
                errors.Add("دستور نمی‌تواند null باشد");
                return errors;
            }

            if (string.IsNullOrWhiteSpace(command.Phrase))
                errors.Add("عبارت دستور نمی‌تواند خالی باشد");
            else if (command.Phrase.Trim().Length < 2)
                errors.Add("عبارت دستور باید حداقل 2 کاراکتر باشد");

            // بررسی نیاز به ActionValue برای برخی انواع دستورات
            var requiresActionValue = new[]
            {
                CommandActionType.TypeText,
                CommandActionType.SendKeys,

            };

            if (requiresActionValue.Contains(command.ActionType) && string.IsNullOrWhiteSpace(command.ActionValue))
                errors.Add($"نوع دستور {command.ActionType} نیاز به مقدار عمل دارد");

            return errors;
        }
    }
}