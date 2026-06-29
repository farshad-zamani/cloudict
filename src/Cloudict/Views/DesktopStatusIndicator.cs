using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.IO;
using NAudio.CoreAudioApi;

namespace Cloudict
{
    /// <summary>
    /// چراغ وضعیت دسکتاپ برای نمایش حالت میکروفون
    /// </summary>
    public partial class DesktopStatusIndicator : Window
    {
        private DispatcherTimer _microphoneCheckTimer;
        private bool _isVisible = false;
        private bool _lastMicrophoneState = false;

        // Windows API for getting system tray area
        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // NAudio handles COM initialization internally

        private static string GetLogPath()
        {
            string logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cloudict", "mic_debug.log");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath));
            return logPath;
        }

        private void LogToFile(string message)
        {
            try
            {
                File.AppendAllText(GetLogPath(), $"{DateTime.Now}: {message}\n");
            }
            catch
            {
                // Ignore logging errors
            }
        }

        public DesktopStatusIndicator()
        {
            InitializeComponent();
            
            // Position window when loaded
            Loaded += (s, e) => PositionWindow();
            
            // Initialize microphone monitoring
            InitializeMicrophoneMonitor();
            
            // Show window and log
            Show();
            Debug.WriteLine("DesktopStatusIndicator: Window created and shown");
        }

        /// <summary>
        /// موقعیت‌یابی پنجره در گوشه پایین سمت راست
        /// </summary>
        private void PositionWindow()
        {
            try
            {
                // Get screen dimensions
                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;

                // Position above the system tray (taskbar)
                double taskbarHeight = SystemParameters.PrimaryScreenHeight - SystemParameters.WorkArea.Height;
                
                this.Left = screenWidth - this.Width - 10;
                this.Top = screenHeight - taskbarHeight - this.Height - 10;
                
                System.Diagnostics.Debug.WriteLine($"موقعیت چراغ: Left={this.Left}, Top={this.Top}");
            }
            catch (Exception ex)
            {
                // Fallback positioning
                this.Left = SystemParameters.PrimaryScreenWidth - 30;
                this.Top = SystemParameters.PrimaryScreenHeight - 50;
                System.Diagnostics.Debug.WriteLine($"خطا در موقعیت‌یابی: {ex.Message}");
            }
        }



        /// <summary>
        /// راه‌اندازی مانیتور میکروفون
        /// </summary>
        private void InitializeMicrophoneMonitor()
        {
            _microphoneCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500) // Check every 500ms
            };
            _microphoneCheckTimer.Tick += CheckMicrophoneStatus;
        }

        /// <summary>
        /// بررسی وضعیت میکروفون و به‌روزرسانی چراغ
        /// </summary>
        private void CheckMicrophoneStatus(object sender, EventArgs e)
        {
            try
            {
                bool isMicActive = IsMicrophoneActiveInSystem();
                
                // همیشه وضعیت چراغ را به‌روزرسانی کن، حتی اگر تغییری نکرده باشد
                // چون ممکن است وضعیت سیستم تغییر کرده باشد
                _lastMicrophoneState = isMicActive;
                UpdateStatusLight(isMicActive);
                
                Debug.WriteLine($"وضعیت میکروفون سیستم: {(isMicActive ? "فعال" : "غیرفعال")}");
                // نوشتن در فایل log برای debug
                File.AppendAllText(GetLogPath(), $"{DateTime.Now}: وضعیت میکروفون سیستم: {(isMicActive ? "فعال" : "غیرفعال")}\n");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"خطا در بررسی وضعیت میکروفون: {ex.Message}");
                File.AppendAllText(GetLogPath(), $"{DateTime.Now}: خطا در بررسی وضعیت میکروفون: {ex.Message}\n");
            }
        }

        /// <summary>
        /// بررسی فعال بودن میکروفون در سیستم با استفاده از NAudio
        /// </summary>
        /// <returns>true اگر میکروفون فعال باشد</returns>
        public static bool IsMicrophoneActiveInSystem()
        {
            try
            {
                using (var deviceEnumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator())
                {
                    // Get default capture device
                    var captureDevice = deviceEnumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Console);
                    
                    if (captureDevice == null)
                    {
                        File.AppendAllText(GetLogPath(), $"{DateTime.Now}: دستگاه capture پیدا نشد\n");
                        return false;
                    }

                    // Check if device is active
                    if (captureDevice.State != NAudio.CoreAudioApi.DeviceState.Active)
                    {
                        File.AppendAllText(GetLogPath(), $"{DateTime.Now}: دستگاه فعال نیست: State={captureDevice.State}\n");
                        return false;
                    }

                    // Check if muted
                    bool isMuted = captureDevice.AudioEndpointVolume.Mute;
                    File.AppendAllText(GetLogPath(), $"{DateTime.Now}: Muted: {isMuted}\n");

                    if (isMuted)
                    {
                        return false;
                    }

                    // Get session manager
                    var sessionManager = captureDevice.AudioSessionManager;
                    var sessions = sessionManager.Sessions;
                    
                    File.AppendAllText(GetLogPath(), $"{DateTime.Now}: تعداد sessions: {sessions.Count}\n");

                    bool hasActiveRecordingSession = false;

                    // Check each session
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        try
                        {
                            var session = sessions[i];
                            
                            // Skip system sounds sessions
                            if (session.IsSystemSoundsSession)
                            {
                                File.AppendAllText(GetLogPath(), $"{DateTime.Now}: Session {i}: System sounds session - نادیده گرفته شد\n");
                                continue;
                            }

                            var processId = session.GetProcessID;
                            var sessionState = session.State;
                            
                            File.AppendAllText(GetLogPath(), $"{DateTime.Now}: Session {i}: State={sessionState}, PID={processId}, IsSystemSounds={session.IsSystemSoundsSession}\n");

                            // Only consider sessions that are active and have a valid process ID
                            if (sessionState == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive && processId != 0)
                            {
                                hasActiveRecordingSession = true;
                                File.AppendAllText(GetLogPath(), $"{DateTime.Now}: Session فعال پیدا شد: PID={processId}\n");
                                break; // Found an active session, no need to check others
                            }
                        }
                        catch (Exception ex)
                        {
                            File.AppendAllText(GetLogPath(), $"{DateTime.Now}: خطا در بررسی session {i}: {ex.Message}\n");
                        }
                    }

                    File.AppendAllText(GetLogPath(), $"{DateTime.Now}: نتیجه نهایی: ActiveRecordingSession={hasActiveRecordingSession}\n");
                    return hasActiveRecordingSession;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"خطا در بررسی وضعیت میکروفون: {ex.Message}");
                File.AppendAllText(GetLogPath(), $"{DateTime.Now}: خطای کلی: {ex.Message}\n");
                // در صورت خطا، false برگردان تا چراغ قرمز باشد
                return false;
            }
        }

        public void SetStatus(bool isActive)
        {
            UpdateStatusLight(isActive);
        }

        private void UpdateStatusLight(bool isActive)
        {
            Dispatcher.Invoke(() =>
            {
                if (StatusLight != null)
                {
                    StatusLight.Fill = isActive ? Brushes.Green : Brushes.Red;
                    Debug.WriteLine($"DesktopStatusIndicator: Status light updated to {(isActive ? "Green" : "Red")}");
                }
            });
        }

        /// <summary>
        /// نمایش چراغ وضعیت
        /// </summary>
        public new void Show()
        {
            if (!_isVisible)
            {
                _isVisible = true;
                this.Visibility = Visibility.Visible;
                base.Show();
                _microphoneCheckTimer?.Start();
                
                // Initial status update
                UpdateStatusLight(false);
                System.Diagnostics.Debug.WriteLine("چراغ وضعیت نمایش داده شد");
            }
        }

        /// <summary>
        /// مخفی کردن چراغ وضعیت
        /// </summary>
        public new void Hide()
        {
            if (_isVisible)
            {
                _isVisible = false;
                this.Visibility = Visibility.Hidden;
                _microphoneCheckTimer?.Stop();
                System.Diagnostics.Debug.WriteLine("چراغ وضعیت مخفی شد");
            }
        }

        /// <summary>
        /// تنظیم وضعیت چراغ به صورت دستی
        /// </summary>
        /// <param name="isActive">وضعیت میکروفون</param>
        public void SetMicrophoneStatus(bool isActive)
        {
            _lastMicrophoneState = isActive;
            UpdateStatusLight(isActive);
        }

        /// <summary>
        /// بررسی نمایان بودن چراغ
        /// </summary>
        public new bool IsVisible => _isVisible;

        /// <summary>
        /// آزادسازی منابع
        /// </summary>
        public void Dispose()
        {
            _microphoneCheckTimer?.Stop();
            this.Close();
        }
    }
}