using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cloudict
{
    /// <summary>
    /// کلاس ردیابی کلمات جدید برای جلوگیری از تکرار پردازش دستورات
    /// </summary>
    public class WordTracker
    {
        private List<string> _processedWords;
        private string _lastProcessedText;
        private HashSet<string> _executedCommands;
        private Dictionary<string, int> _commandExecutionCount;
        
        public WordTracker()
        {
            _processedWords = new List<string>();
            _lastProcessedText = string.Empty;
            _executedCommands = new HashSet<string>();
            _commandExecutionCount = new Dictionary<string, int>();
        }
        
        /// <summary>
        /// دریافت کلمات جدید از متن ورودی
        /// </summary>
        /// <param name="currentText">متن فعلی</param>
        /// <returns>لیست کلمات جدید</returns>
        public List<string> GetNewWords(string currentText)
        {
            var newWords = new List<string>();
            
            if (string.IsNullOrEmpty(currentText))
            {
                Reset();
                return newWords;
            }

            var currentWords = SplitTextToWords(currentText);
            
            // بررسی تغییر کامل متن یا کاهش طول
            bool isNewSession = false;
            if (currentText.Length < _lastProcessedText.Length)
            {
                isNewSession = true;
            }
            else if (!string.IsNullOrEmpty(_lastProcessedText))
            {
                // بررسی اینکه آیا متن فعلی ادامه متن قبلی است یا نه
                string commonPrefix = GetCommonPrefix(_lastProcessedText, currentText);
                if (commonPrefix.Length < Math.Min(_lastProcessedText.Length * 0.8, _lastProcessedText.Length - 10))
                {
                    isNewSession = true;
                }
            }
            
            if (isNewSession)
            {
                Reset();
                _processedWords = currentWords;
                _lastProcessedText = currentText;
                return new List<string>(currentWords); // همه کلمات جدید هستند
            }
            
            // پیدا کردن کلمات جدید
            for (int i = _processedWords.Count; i < currentWords.Count; i++)
            {
                newWords.Add(currentWords[i]);
            }
            
            _processedWords = currentWords;
            _lastProcessedText = currentText;
            
            return newWords;
        }
        
        /// <summary>
        /// بررسی اینکه آیا متن جدید محتوای جدیدی دارد یا نه
        /// </summary>
        /// <param name="currentText">متن فعلی</param>
        /// <returns>true اگر محتوای جدیدی وجود دارد</returns>
        public bool HasNewContent(string currentText)
        {
            if (string.IsNullOrEmpty(currentText))
            {
                if (!string.IsNullOrEmpty(_lastProcessedText))
                {
                    Reset();
                    return false;
                }
                return false;
            }

            // اگر اولین بار است
            if (string.IsNullOrEmpty(_lastProcessedText))
            {
                _lastProcessedText = currentText;
                return true;
            }

            // بررسی تغییر در متن
            if (currentText != _lastProcessedText)
            {
                // بررسی اینکه آیا متن جدید ادامه متن قبلی است
                if (currentText.Length > _lastProcessedText.Length && 
                    currentText.StartsWith(_lastProcessedText.TrimEnd()))
                {
                    _lastProcessedText = currentText;
                    return true;
                }
                // بررسی تغییر کامل متن
                else if (currentText.Length < _lastProcessedText.Length || 
                         !currentText.StartsWith(_lastProcessedText.Substring(0, Math.Min(_lastProcessedText.Length, currentText.Length))))
                {
                    Reset();
                    _lastProcessedText = currentText;
                    return true;
                }
                
                _lastProcessedText = currentText;
                return true;
            }

            return false;
        }
        
        /// <summary>
        /// پیدا کردن پیشوند مشترک دو رشته
        /// </summary>
        private string GetCommonPrefix(string str1, string str2)
        {
            int minLength = Math.Min(str1.Length, str2.Length);
            int commonLength = 0;
            
            for (int i = 0; i < minLength; i++)
            {
                if (str1[i] == str2[i])
                    commonLength++;
                else
                    break;
            }
            
            return str1.Substring(0, commonLength);
        }
        
        /// <summary>
        /// بررسی اینکه آیا دستور قبلاً اجرا شده یا نه
        /// </summary>
        /// <param name="command">دستور</param>
        /// <returns>true اگر دستور جدید باشد</returns>
        public bool IsCommandNew(string command)
        {
            // همیشه true برمی‌گرداند تا دستورات همیشه اجرا شوند
            // حتی اگر پشت سر هم تکرار شوند
            return true;
        }
        
        /// <summary>
        /// ثبت دستور اجرا شده
        /// </summary>
        /// <param name="command">دستور اجرا شده</param>
        public void MarkCommandAsExecuted(string command)
        {
            var commandKey = command.Trim().ToLower();
            _executedCommands.Add(commandKey);
            
            // افزایش شمارنده اجرای دستور
            if (_commandExecutionCount.ContainsKey(commandKey))
            {
                _commandExecutionCount[commandKey]++;
            }
            else
            {
                _commandExecutionCount[commandKey] = 1;
            }
        }
        
        /// <summary>
        /// بازنشانی تاریخچه
        /// </summary>
        public void Reset()
        {
            _processedWords.Clear();
            _executedCommands.Clear();
            _commandExecutionCount.Clear();
            _lastProcessedText = string.Empty;
        }
        
        /// <summary>
        /// پاک کردن تاریخچه دستورات اجرا شده (برای شروع جلسه جدید)
        /// </summary>
        public void ClearExecutedCommands()
        {
            _executedCommands.Clear();
            _commandExecutionCount.Clear();
        }
        
        /// <summary>
        /// تقسیم متن به کلمات
        /// </summary>
        /// <param name="text">متن ورودی</param>
        /// <returns>لیست کلمات</returns>
        private List<string> SplitTextToWords(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new List<string>();
                
            return Regex.Split(text.Trim(), @"\s+")
                       .Where(w => !string.IsNullOrEmpty(w))
                       .ToList();
        }
    }
}