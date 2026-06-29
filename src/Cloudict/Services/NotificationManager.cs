using System;
using System.IO;
using System.Runtime.Versioning;
using System.Drawing;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace Cloudict
{
    [SupportedOSPlatform("windows")]
    public static class NotificationManager
    {
        // Single shared NotifyIcon — registered by MainWindow via Register(...).
        // Previously NotificationManager created its own NotifyIcon with
        // SystemIcons.Information, which is what produced the extra blue "i"
        // icons in the tray when balloon tips fired.
        private static NotifyIcon _notifyIcon;
        private static bool _ownsNotifyIcon = false;
        private static System.Threading.Timer _timer;
        private static readonly object _lock = new object();
        private static int _pendingNotificationCount = 0;
        private static bool _isShowingNotification = false;
        private static NotificationItem _queuedNotification = null;

        /// <summary>
        /// Register the application's primary NotifyIcon. Call this once after
        /// the icon is created. Balloon tips will be shown through this icon
        /// instead of a separate one (which would appear as a duplicate tray
        /// entry).
        /// </summary>
        public static void Register(NotifyIcon icon)
        {
            lock (_lock)
            {
                if (_notifyIcon != null && _ownsNotifyIcon)
                {
                    try { _notifyIcon.Visible = false; _notifyIcon.Dispose(); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Dispose previous owned NotifyIcon failed: {ex.Message}"); }
                }
                _notifyIcon = icon;
                _ownsNotifyIcon = false;
            }
        }
        
        private class NotificationItem
        {
            public string Title { get; set; }
            public string Message { get; set; }
            public ToolTipIcon Icon { get; set; }
            public int Timeout { get; set; }
        }
        
        [SupportedOSPlatform("windows")]
        static NotificationManager()
        {
            // No-op. The owning window is expected to call Register(...).
            // If a notification is requested before Register, EnsureFallbackIcon()
            // will lazily create a minimal placeholder icon so we don't crash.
        }

        [SupportedOSPlatform("windows")]
        private static void EnsureFallbackIcon()
        {
            if (_notifyIcon != null) return;
            try
            {
                Icon appIcon = null;
                try
                {
                    var exePath = Path.Combine(AppContext.BaseDirectory, "Cloudict.exe");
                    if (File.Exists(exePath))
                    {
                        appIcon = Icon.ExtractAssociatedIcon(exePath);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Fallback icon extraction failed: {ex.Message}");
                }

                _notifyIcon = new NotifyIcon
                {
                    Icon = appIcon ?? SystemIcons.Application,
                    Visible = false,
                    Text = "Cloudict"
                };
                _ownsNotifyIcon = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"خطا در ایجاد fallback NotifyIcon: {ex.Message}");
            }
        }

        /// <summary>
        /// لغو نوتیفیکیشن‌های فعال
        /// </summary>
        private static void CancelActiveNotifications()
        {
            try
            {
                // لغو تایمر قبلی ابتدا
                _timer?.Dispose();
                _timer = null;

                // Note: we no longer toggle _notifyIcon.Visible here or call
                // Shell_NotifyIcon directly. The old NIM_DELETE/NIM_ADD dance
                // was registering a *separate* icon record via the Win32 API
                // (using the active foreground window's hWnd) that the WinForms
                // NotifyIcon had no way to clean up — that's what caused
                // duplicate / orphan blue-info icons in the system tray.
                //
                // The owning window owns NotifyIcon visibility now (see
                // MainWindow.SetupNotifyIcon and OnStateChanged); we only
                // suppress queued balloon tips.

                _pendingNotificationCount = 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"خطا در لغو نوتیفیکیشن‌های فعال: {ex.Message}");
            }
        }
        
        /// <summary>
        /// نمایش نوتیفیکیشن با مدیریت صف هوشمند
        /// </summary>
        /// <param name="title">عنوان نوتیفیکیشن</param>
        /// <param name="message">متن نوتیفیکیشن</param>
        /// <param name="icon">آیکون نوتیفیکیشن</param>
        /// <param name="timeout">مدت زمان نمایش (میلی‌ثانیه)</param>
        [SupportedOSPlatform("windows")]
        private static void ShowNotificationWithQueueManagement(string title, string message, ToolTipIcon icon, int timeout)
        {
            lock (_lock)
            {
                try
                {
                    var newNotification = new NotificationItem
                    {
                        Title = title,
                        Message = message,
                        Icon = icon,
                        Timeout = timeout
                    };

                    // اگر هیچ نوتیفیکیشنی در حال نمایش نیست، بلافاصله نمایش بده
                    if (!_isShowingNotification)
                    {
                        ShowNotificationImmediately(newNotification);
                    }
                    else
                    {
                        // اگر نوتیفیکیشنی در حال نمایش است، فقط آخرین نوتیفیکیشن را در صف نگه دار
                        _queuedNotification = newNotification;
                    }
                }
                catch (Exception ex)
                {
                    // fallback to MessageBox in case of any error
                    MessageBoxImage messageBoxIcon = icon == ToolTipIcon.Error ? MessageBoxImage.Error :
                                                    icon == ToolTipIcon.Warning ? MessageBoxImage.Warning :
                                                    MessageBoxImage.Information;
                    MessageBox.Show($"{message}\n\nError: {ex.Message}", title, MessageBoxButton.OK, messageBoxIcon);
                }
            }
        }

        /// <summary>
        /// نمایش فوری نوتیفیکیشن
        /// </summary>
        /// <param name="notification">نوتیفیکیشن برای نمایش</param>
        private static void ShowNotificationImmediately(NotificationItem notification)
        {
            try
            {
                EnsureFallbackIcon();

                if (_notifyIcon != null)
                {
                    _isShowingNotification = true;
                    _pendingNotificationCount++;

                    // اگر آیکن متعلق به MainWindow باشد، Visibility آن را دست‌کاری نمی‌کنیم
                    // (MainWindow خودش آن را مدیریت می‌کند). فقط در حالت fallback آن را
                    // visible می‌کنیم تا بالن نمایش پیدا کند.
                    if (_ownsNotifyIcon && !_notifyIcon.Visible)
                    {
                        _notifyIcon.Visible = true;
                    }

                    // نمایش نوتیفیکیشن
                    _notifyIcon.ShowBalloonTip(notification.Timeout, notification.Title, notification.Message, notification.Icon);

                    // تایمر برای پایان نمایش و بررسی صف — تحت قفل تا کالبک قبلی و
                    // ساخت تایمر جدید با هم تداخل نکنند
                    lock (_lock)
                    {
                        _timer?.Dispose();
                        _timer = new System.Threading.Timer(OnNotificationFinished, null, notification.Timeout + 1000, System.Threading.Timeout.Infinite);
                    }
                }
                else
                {
                    // fallback to MessageBox if NotifyIcon is not available
                    MessageBoxImage messageBoxIcon = notification.Icon == ToolTipIcon.Error ? MessageBoxImage.Error :
                                                    notification.Icon == ToolTipIcon.Warning ? MessageBoxImage.Warning :
                                                    MessageBoxImage.Information;
                    MessageBox.Show(notification.Message, notification.Title, MessageBoxButton.OK, messageBoxIcon);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"خطا در نمایش نوتیفیکیشن: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Callback برای پایان نمایش نوتیفیکیشن
        /// </summary>
        private static void OnNotificationFinished(object state)
        {
            try
            {
                lock (_lock)
                {
                    _timer?.Dispose();
                    _timer = null;
                    _pendingNotificationCount = Math.Max(0, _pendingNotificationCount - 1);
                    _isShowingNotification = false;

                    // اگر نوتیفیکیشنی در صف است، آن را نمایش بده
                    if (_queuedNotification != null)
                    {
                        var nextNotification = _queuedNotification;
                        _queuedNotification = null;
                        ShowNotificationImmediately(nextNotification);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"خطا در پایان نمایش نوتیفیکیشن: {ex.Message}");
            }
        }

        /// <summary>
        /// نمایش نوتیفیکیشن اجرای دستور
        /// </summary>
        /// <param name="commandName">نام دستور</param>
        /// <param name="description">توضیحات اضافی</param>
        public static void ShowCommandExecutedNotification(string commandName, string description = "")
        {
            string title = Loc.Get("Notif_CommandExec_Title");
            string text = !string.IsNullOrEmpty(description) ? $"{commandName}\n{description}" : commandName;
            ShowNotificationWithQueueManagement(title, text, ToolTipIcon.Info, 3000);
        }

        /// <summary>
        /// نمایش نوتیفیکیشن خطا
        /// </summary>
        /// <param name="message">پیام خطا</param>
        public static void ShowErrorNotification(string message)
        {
            ShowNotificationWithQueueManagement(Loc.Get("Common_Error_Title"), Loc.Get("Notif_ErrorMsg", message), ToolTipIcon.Error, 3000);
        }

        /// <summary>
        /// نمایش نوتیفیکیشن عمومی
        /// </summary>
        /// <param name="title">عنوان</param>
        /// <param name="message">پیام</param>
        /// <param name="icon">آیکون</param>
        /// <param name="timeout">مدت زمان نمایش (میلی‌ثانیه)</param>
        public static void ShowBalloonTip(string title, string message, ToolTipIcon icon = ToolTipIcon.Info, int timeout = 3000)
        {
            ShowNotificationWithQueueManagement(title, message, icon, timeout);
        }
        
        /// <summary>
        /// مخفی کردن balloon tip
        /// </summary>
        private static void HideBalloonTip(object state)
        {
            lock (_lock)
            {
                try
                {
                    // فقط در حالت fallback (icon متعلق به خود NotificationManager) آن را hide می‌کنیم.
                    // اگر icon از طریق Register از MainWindow آمده باشد، Visibility آن
                    // فقط توسط MainWindow کنترل می‌شود.
                    if (_notifyIcon != null && _ownsNotifyIcon)
                    {
                        _notifyIcon.Visible = false;
                    }
                    _timer?.Dispose();
                    _timer = null;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"خطا در مخفی کردن نوتیفیکیشن: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// پاک‌سازی منابع
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static void Cleanup()
        {
            lock (_lock)
            {
                try
                {
                    // لغو timer قبلی
                    _timer?.Dispose();
                    _timer = null;
                    
                    _pendingNotificationCount = 0;
                    _isShowingNotification = false;
                    _queuedNotification = null;
                    
                    if (_notifyIcon != null)
                    {
                        _notifyIcon.Visible = false;
                        _notifyIcon.Dispose();
                        _notifyIcon = null;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"خطا در پاک‌سازی NotificationManager: {ex.Message}");
                }
            }
        }
    }
}