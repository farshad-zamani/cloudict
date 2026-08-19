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

namespace Cloudict
{
    /// <summary>
    /// چراغ وضعیت دسکتاپ برای نمایش حالت میکروفون
    /// </summary>
    public partial class DesktopStatusIndicator : Window
    {
        private DispatcherTimer _microphoneCheckTimer;
        private bool _isVisible = false;
        /// <summary>
        /// Last observed microphone state. Null until the first poll, so the very first check always
        /// paints the light even when the microphone starts out idle.
        /// </summary>
        private bool? _lastMicrophoneState;

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

        // Microphone detection is delegated to the platform layer (see IsMicrophoneActiveInSystem).

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

                // Repaint only on a real transition. This poll runs every 500 ms and used to append
                // a line to a log file on every single pass — roughly 173,000 lines a day, which had
                // grown past 1.4 GB on a machine that had been running the app for months.
                if (isMicActive == _lastMicrophoneState) return;

                _lastMicrophoneState = isMicActive;
                UpdateStatusLight(isMicActive);

                Debug.WriteLine($"[DesktopStatusIndicator] microphone {(isMicActive ? "active" : "idle")}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DesktopStatusIndicator] microphone check failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Whether any application is currently capturing the microphone.
        ///
        /// <para>The WASAPI code this used to run inline now lives in the platform layer behind
        /// <see cref="Cloudict.Abstractions.IMicrophoneMonitor"/>, so the status light works the same
        /// way on every platform and simply reports nothing where detection is unavailable. It also
        /// stops the old implementation's habit of appending to a log file on every poll.</para>
        /// </summary>
        public static bool IsMicrophoneActiveInSystem()
        {
            try
            {
                return AppServices.Platform.MicrophoneMonitor.IsMicrophoneInUse();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DesktopStatusIndicator] microphone probe failed: {ex.Message}");
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