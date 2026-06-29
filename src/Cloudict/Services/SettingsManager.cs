using System;
using System.IO;
using Newtonsoft.Json;
using System.Windows;

namespace Cloudict
{
    /// <summary>
    /// کلاس مدیریت تنظیمات برنامه
    /// </summary>
    public class SettingsManager
    {
        private static readonly string SettingsFileName = "settings.json";
        private static readonly string BackupFileName = "settings.backup.json";
        
        /// <summary>
        /// مسیر فایل تنظیمات
        /// </summary>
        public string SettingsFilePath { get; }
        
        /// <summary>
        /// مسیر فایل پشتیبان تنظیمات
        /// </summary>
        public string BackupFilePath { get; }

        /// <summary>
        /// سازنده کلاس SettingsManager
        /// </summary>
        public SettingsManager()
        {
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            SettingsFilePath = Path.Combine(appDirectory, SettingsFileName);
            BackupFilePath = Path.Combine(appDirectory, BackupFileName);
        }

        /// <summary>
        /// بارگذاری تنظیمات از فایل JSON
        /// </summary>
        /// <returns>تنظیمات بارگذاری شده یا تنظیمات پیش‌فرض در صورت خطا</returns>
        public AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonConvert.DeserializeObject<AppSettings>(json);
                    
                    // بررسی اعتبار تنظیمات بارگذاری شده
                    if (settings != null && settings.IsValid())
                    {
                        // دستورات صوتی را مقداردهی اولیه کن اگر null یا خالی باشد
                        if (settings.VoiceCommands == null || settings.VoiceCommands.Count == 0)
                        {
                            settings.VoiceCommands = AppSettings.GetDefaultCommands();
                        }
                        return settings;
                    }
                    else
                    {
                        // در صورت نامعتبر بودن، از تنظیمات پیش‌فرض استفاده کن
                        MessageBox.Show(Loc.Get("SettingsMgr_InvalidLoaded"),
                                      Loc.Get("SettingsMgr_LoadError_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                        return GetDefaultSettings();
                    }
                }
            }
            catch (Exception ex)
            {
                // در صورت خطا، سعی کن از فایل پشتیبان استفاده کنی
                try
                {
                    if (File.Exists(BackupFilePath))
                    {
                        string backupJson = File.ReadAllText(BackupFilePath);
                        var backupSettings = JsonConvert.DeserializeObject<AppSettings>(backupJson);
                        
                        if (backupSettings != null && backupSettings.IsValid())
                        {
                            MessageBox.Show(Loc.Get("SettingsMgr_LoadedFromBackup", ex.Message),
                                          Loc.Get("SettingsMgr_RestoredFromBackup_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
                            return backupSettings;
                        }
                    }
                }
                catch
                {
                    // اگر فایل پشتیبان هم کار نکرد، از تنظیمات پیش‌فرض استفاده کن
                }

                MessageBox.Show(Loc.Get("SettingsMgr_LoadErrorDefaults", ex.Message),
                              Loc.Get("SettingsMgr_LoadError_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // در صورت عدم وجود فایل یا خطا، تنظیمات پیش‌فرض را برگردان
            return GetDefaultSettings();
        }

        /// <summary>
        /// ذخیره تنظیمات در فایل JSON
        /// </summary>
        /// <param name="settings">تنظیمات برای ذخیره</param>
        /// <returns>true در صورت موفقیت</returns>
        public bool SaveSettings(AppSettings settings)
        {
            if (settings == null)
            {
                MessageBox.Show(Loc.Get("SettingsMgr_Invalid"), Loc.Get("SettingsMgr_SaveError_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (!settings.IsValid())
            {
                MessageBox.Show(Loc.Get("SettingsMgr_InvalidEntered"),
                              Loc.Get("SettingsMgr_ValidationError_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            try
            {
                // ایجاد فایل پشتیبان قبل از ذخیره
                CreateBackup();

                // تبدیل به JSON با فرمت زیبا
                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                
                // ذخیره در فایل
                File.WriteAllText(SettingsFilePath, json);
                
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("SettingsMgr_SaveError", ex.Message),
                              Loc.Get("SettingsMgr_SaveError_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// دریافت تنظیمات پیش‌فرض
        /// </summary>
        /// <returns>تنظیمات پیش‌فرض</returns>
        public AppSettings GetDefaultSettings()
        {
            var settings = new AppSettings();
            // همیشه دستورات پیش‌فرض را اضافه کن
            settings.VoiceCommands = AppSettings.GetDefaultCommands();
            return settings;
        }

        /// <summary>
        /// ایجاد فایل پشتیبان از تنظیمات فعلی
        /// </summary>
        private void CreateBackup()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    File.Copy(SettingsFilePath, BackupFilePath, true);
                }
            }
            catch
            {
                // اگر نتوانست پشتیبان بگیرد، ادامه بده
                // این خطا حیاتی نیست
            }
        }

        /// <summary>
        /// بررسی وجود فایل تنظیمات
        /// </summary>
        /// <returns>true اگر فایل تنظیمات وجود داشته باشد</returns>
        public bool SettingsFileExists()
        {
            return File.Exists(SettingsFilePath);
        }

        /// <summary>
        /// حذف فایل تنظیمات (برای بازگردانی به پیش‌فرض)
        /// </summary>
        /// <returns>true در صورت موفقیت</returns>
        public bool ResetToDefaults()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    File.Delete(SettingsFilePath);
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("SettingsMgr_ResetError", ex.Message),
                              Loc.Get("Common_Error_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
    }
}