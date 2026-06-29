using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using WebDriverManager;
using WebDriverManager.DriverConfigs.Impl;
using WebDriverManager.Helpers;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using System.Text.RegularExpressions;
using System.Windows.Navigation;
using System.Runtime.Versioning;
using Window = System.Windows.Window;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace Cloudict
{
    [SupportedOSPlatform("windows")]
    public partial class MainWindow : GlassWindow
    {
        #region Fields

        // Core Components
        private IWebDriver _driver;
        private bool _isListening = false;
        private CancellationTokenSource _cancellationTokenSource;
        private NotifyIcon _notifyIcon;
        private string _configFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cloudict", "config.json");
        
        // Settings Management
        private AppSettings _settings;
        private SettingsManager _settingsManager;

        // Browser Initialization
        private bool _preloadBrowser = false;
        private IWebDriver _preloadedDriver = null;
        private bool _browserReady = false;

        // UI Elements Identification - Updated with correct XPath
        private const string MIC_BUTTON_XPATH = "//*[@id=\"yDmH0d\"]/c-wiz/div/div[2]/c-wiz/div[2]/c-wiz/div[1]/div[2]/div[2]/div/c-wiz/div[5]/div/div[1]/c-wiz/span[2]/span/button/div[3]";
        
        // Alternative XPaths for different states of the start button
        private static readonly string[] MIC_BUTTON_XPATHS = {
            "//*[@id=\"yDmH0d\"]/c-wiz/div/div[2]/c-wiz/div[2]/c-wiz/div[1]/div[2]/div[2]/div/c-wiz/div[5]/div/div[1]/c-wiz/span[2]/span/button/div[3]", // Main XPath
            "//*[@id=\"yDmH0d\"]/c-wiz/div/div[2]/c-wiz/div[2]/c-wiz/div[1]/div[2]/div[2]/div/c-wiz/div[5]/div/div[1]/c-wiz/span[2]/span/button", // Without div[3]
            "//*[@id=\"yDmH0d\"]/c-wiz/div/div[2]/c-wiz/div[2]/c-wiz/div[1]/div[2]/div[2]/div/c-wiz/div[4]/div/div[1]/c-wiz/span[2]/span/button/div[3]", // Old div[4] version
            "//*[@id=\"yDmH0d\"]/c-wiz/div/div[2]/c-wiz/div[2]/c-wiz/div[1]/div[2]/div[2]/div/c-wiz/div[4]/div/div[1]/c-wiz/span[2]/span/button" // Old without div[3]
        };
        private const string STOP_BUTTON_XPATH = "/html/body/c-wiz/div/div[2]/c-wiz/div[2]/c-wiz/div[1]/div[2]/div[2]/div/c-wiz/div[5]/div/div[1]/c-wiz/span[2]/span/div";
        private const string SOURCE_TEXT_ARIA_LABEL = "نوشتار مبدأ";

        // Additional XPaths for direct control buttons
        private const string SPEECH_TRANSLATION_DIV_XPATH = "//div[text()='ترجمه گفتار' or text()='Translate by voice']";
        private const string STOP_SPEECH_TRANSLATION_BUTTON_XPATH = "//button[@aria-label='متوقف کردن ترجمه گفتار' or @aria-label='Stop translation by voice']";



        // Variables to track recognized text
        private string _currentRecognizedText = string.Empty;
        private string _lastProcessedText = string.Empty;
        private string _lastAddedWord = string.Empty;
        private List<string> _pendingWords = new List<string>();

        // متغیرهای جدید برای شمارش کلمات و کنترل روند انتقال
        private int _lastProcessedWordIndex = -1;  // شمارشگر کلمه‌های پردازش شده
        private List<string> _allWords = new List<string>();  // لیست تمام کلمات شناسایی شده در بافر
        private bool _transferStarted = false;  // آیا انتقال شروع شده است
        private bool _initialDelayCompleted = false;  // آیا تاخیر اولیه تکمیل شده است
        private DateTime _firstTextDetectedTime;  // زمان تشخیص اولین متن

        // Timer for text processing
        private DispatcherTimer _processingTimer;
        private bool _isProcessing = false;

        // Timer for detecting inactivity after typing
        private DispatcherTimer _inactivityTimer;
        private DateTime _lastTextUpdateTime;
        private bool _hasRecognizedText = false;

        // Live text transfer variables
        private bool _isLiveTransferActive = false;
        private string _lastSentText = string.Empty;

        // Global shortcut and status indicator
        private GlobalShortcutManager _shortcutManager;
        private DesktopStatusIndicator _statusIndicator;

        // Voice Command System
        private VoiceCommandProcessor _voiceCommandProcessor;
        private VoiceCommandManager _voiceCommandManager;
        private SystemCommandExecutor _systemCommandExecutor;
        
        // متغیرهای جدید برای نگهداری دستور در حافظه
        private VoiceCommand _pendingCommand = null;  // دستور در انتظار اجرا
        private bool _isCommandPending = false;  // آیا دستوری در انتظار اجرا است
        private bool _isProcessingCommand = false;  // آیا در حال پردازش دستور است
        private string _textBeforeCommand = string.Empty;  // متن قبل از دستور
        
        // License checking removed - now handled in App.xaml.cs

        #endregion

        #region Windows API for SendInput and Focus Management
        
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
        
        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);
        
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern IntPtr GetMessageExtraInfo();

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        #endregion

        public MainWindow()
        {
            InitializeComponent();

            // Initialize settings
            _settingsManager = new SettingsManager();
            _settings = _settingsManager.LoadSettings() ?? new AppSettings();

            UpdateEngineUiState();

            // Setup initial state
            if (OperatingSystem.IsWindows())
            {
                SetupNotifyIcon();
            }

            // Initialize UI
            lblRecognizedText.Text = "";
            lblFinalText.Text = "";
            btnMicrophone.IsEnabled = false;
            btnStartMic.IsEnabled = false;
            btnStopMic.IsEnabled = false;
            statusText.Text = Loc.Get("Main_St_PreparingBrowser");

            // Setup event handlers
            btnCopyFinal.Click += BtnCopyText_Click;
            btnClearFinal.Click += BtnClearText_Click;
            btnCopyRecognized.Click += BtnCopyRecognized_Click;
            btnClearRecognized.Click += BtnClearRecognized_Click;
            btnFastTransfer.Click += BtnFastTransfer_Click;
            btnLiveTransfer.Click += BtnLiveTransfer_Click;
            this.Closing += MainWindow_Closing;

            // Track text changes to update final text
            lblRecognizedText.TextChanged += LblRecognizedText_TextChanged;

            // Setup the timer for delayed processing
            _processingTimer = new DispatcherTimer();
            _processingTimer.Interval = TimeSpan.FromMilliseconds(_settings.ProcessDelayMs);
            _processingTimer.Tick += ProcessingTimer_Tick;

            // Setup the timer for inactivity detection
            _inactivityTimer = new DispatcherTimer();
            _inactivityTimer.Interval = TimeSpan.FromMilliseconds(_settings.InactivityDelayMs);
            _inactivityTimer.Tick += InactivityTimer_Tick;

            // Initialize global shortcut and status indicator
            InitializeGlobalShortcut();
            InitializeStatusIndicator();
            
            // نمایش چراغ وضعیت از ابتدا
            _statusIndicator?.Show();
            
            // به‌روزرسانی وضعیت چراغ بر اساس وضعیت واقعی میکروفون
            UpdateStatusIndicatorBasedOnMicrophone();

            // Initialize Voice Command System
            InitializeVoiceCommandSystem();

            // Preload browser
            if (_preloadBrowser)
            {
                SetupBrowserDriver(true);
            }
            else
            {
                SetupBrowserDriver(false);
            }
        }

        private void ProcessingTimer_Tick(object sender, EventArgs e)
        {
            if (_isProcessing || _isProcessingCommand)
                return;

            _isProcessing = true;

            try
            {
                // اگر انتقال شروع نشده، بررسی کنیم که آیا تاخیر اولیه انجام شده است
                if (!_transferStarted && _hasRecognizedText)
                {
                    // اگر تاخیر اولیه تکمیل نشده، بررسی کنیم که آیا زمان کافی گذشته است
                    if (!_initialDelayCompleted)
                    {
                        TimeSpan elapsed = DateTime.Now - _firstTextDetectedTime;
                        if (elapsed.TotalMilliseconds >= _settings.TransferStartDelayMs) // تاخیر برای شروع انتقال
                        {
                            _initialDelayCompleted = true;
                            statusText.Text = Loc.Get("Main_St_StartTransfer");
                        }
                        else
                        {
                            // هنوز تاخیر اولیه در حال انجام است
                            _processingTimer.Start();
                            _isProcessing = false;
                            return;
                        }
                    }

                    // شروع انتقال
                    _transferStarted = true;
                }

                // اگر انتقال شروع شده، کلمات را پردازش کنیم
                if (_transferStarted && _allWords.Count > 0)
                {
                    // بررسی کنیم که آیا کلمه بعدی برای انتقال وجود دارد
                    int nextWordIndex = _lastProcessedWordIndex + 1;

                    if (nextWordIndex < _allWords.Count)
                    {
                        // یافتن کلمه بعدی برای انتقال
                        string word = _allWords[nextWordIndex];

                        // اطمینان از اینکه کلمه خالی نیست
                        if (!string.IsNullOrEmpty(word))
                        {
                            // بررسی دستورات صوتی
                            if (_voiceCommandProcessor != null)
                            {
                                bool commandExecuted = false;
                                
                                // مرحله 1: بررسی دستورات دو کلمه‌ای ابتدا (اولویت بالاتر)
                                // فقط اگر حداقل یک کلمه قبلی وجود دارد
                                if (nextWordIndex > 0)
                                {
                                    string previousWord = _allWords[nextWordIndex - 1];
                                    string twoWordCommand = previousWord + " " + word;
                                    
                                    var twoWordResult = _voiceCommandProcessor.ProcessText(twoWordCommand);
                                    if (twoWordResult.CommandExecuted)
                                    {
                                        commandExecuted = true;
                                        
                                        // نمایش نوتیفیکیشن اجرای دستور - فقط عنوان دقیق دستور
                                        var executedCommand = twoWordResult.CommandsExecuted?.FirstOrDefault();
                                        string commandTitle = executedCommand?.Phrase ?? twoWordCommand;
                                        NotificationManager.ShowCommandExecutedNotification($"دستور '{commandTitle}' اجرا شد");
                                        
                                        // دستور دو کلمه‌ای اجرا شد
                                        // باید کلمه قبلی را از متن حذف کنیم (اگر تایپ شده)
                                        if (_isLiveTransferActive && !string.IsNullOrEmpty(_lastSentText))
                                        {
                                            // حذف کلمه قبلی از متن ارسال شده
                                            string lastWordToRemove = " " + previousWord;
                                            if (_lastSentText.EndsWith(lastWordToRemove))
                                            {
                                                // ارسال backspace برای حذف کلمه قبلی
                                                for (int i = 0; i < lastWordToRemove.Length; i++)
                                                {
                                                    SendKeys.SendWait("{BACKSPACE}");
                                                }
                                                _lastSentText = _lastSentText.Substring(0, _lastSentText.Length - lastWordToRemove.Length);
                                            }
                                            else if (_lastSentText.EndsWith(previousWord))
                                            {
                                                // حذف کلمه قبلی بدون اسپیس
                                                for (int i = 0; i < previousWord.Length; i++)
                                                {
                                                    SendKeys.SendWait("{BACKSPACE}");
                                                }
                                                _lastSentText = _lastSentText.Substring(0, _lastSentText.Length - previousWord.Length);
                                            }
                                            
                                            // ارسال متن جایگزین
                                            string replacementText = twoWordResult.ProcessedText;
                                            if (!string.IsNullOrEmpty(replacementText))
                                            {
                                                SendTextToActiveWindowImproved(replacementText);
                                                _lastSentText += replacementText;
                                            }
                                        }
                                        else if (!_isLiveTransferActive)
                                        {
                                            // در حالت عادی، کلمه قبلی را از متن نهایی حذف کن
                                            // فقط اگر کلمه قبلی واقعاً در متن نهایی تایپ شده باشد
                                            string finalText = lblFinalText.Text;
                                            
                                            // بررسی اینکه آیا کلمه قبلی در انتهای متن وجود دارد
                                            bool previousWordWasTyped = false;
                                            string lastWordToRemove = " " + previousWord;
                                            if (finalText.EndsWith(lastWordToRemove))
                                            {
                                                lblFinalText.Text = finalText.Substring(0, finalText.Length - lastWordToRemove.Length);
                                                previousWordWasTyped = true;
                                            }
                                            else if (finalText.EndsWith(previousWord))
                                            {
                                                lblFinalText.Text = finalText.Substring(0, finalText.Length - previousWord.Length);
                                                previousWordWasTyped = true;
                                            }
                                            
                                            // اضافه کردن متن جایگزین
                                            string replacementText = twoWordResult.ProcessedText;
                                            if (!string.IsNullOrEmpty(replacementText))
                                            {
                                                // اگر کلمه قبلی حذف شده، متن جایگزین را در همان موقعیت قرار بده
                                                if (previousWordWasTyped)
                                                {
                                                    lblFinalText.Text += replacementText;
                                                    lblFinalText.CaretIndex = lblFinalText.Text.Length;
                                                }
                                                else
                                                {
                                                    // اگر کلمه قبلی تایپ نشده بود، در موقعیت کرسر قرار بده
                                                    int caretPosition = lblFinalText.CaretIndex;
                                                    string textBeforeCaret = lblFinalText.Text.Substring(0, caretPosition);
                                                    string textAfterCaret = lblFinalText.Text.Substring(caretPosition);
                                                    lblFinalText.Text = textBeforeCaret + replacementText + textAfterCaret;
                                                    lblFinalText.CaretIndex = caretPosition + replacementText.Length;
                                                }
                                                lblFinalText.Focus();
                                            }
                                        }
                                    }
                                }
                                
                                // مرحله 2: بررسی دستورات تک کلمه‌ای فقط اگر دستور دو کلمه‌ای اجرا نشده
                                if (!commandExecuted)
                                {
                                    var singleWordResult = _voiceCommandProcessor.ProcessText(word);
                                    if (singleWordResult.CommandExecuted)
                                    {
                                        commandExecuted = true;
                                        
                                        // نمایش نوتیفیکیشن اجرای دستور - فقط عنوان دقیق دستور
                                        var executedCommand = singleWordResult.CommandsExecuted?.FirstOrDefault();
                                        string commandTitle = executedCommand?.Phrase ?? word;
                                        NotificationManager.ShowCommandExecutedNotification($"دستور '{commandTitle}' اجرا شد");
                                        
                                        // دستور تک کلمه‌ای اجرا شد - متن جایگزین را ارسال کن
                                        string replacementText = singleWordResult.ProcessedText;
                                        if (_isLiveTransferActive)
                                        {
                                            // در حالت لایو: ارسال متن جایگزین
                                            if (!string.IsNullOrEmpty(replacementText))
                                            {
                                                SendTextToActiveWindowImproved(replacementText);
                                                _lastSentText += replacementText;
                                            }
                                        }
                                        else
                                        {
                                            // در حالت عادی: اضافه کردن متن جایگزین به lblFinalText
                                            if (!string.IsNullOrEmpty(replacementText))
                                            {
                                                int caretPosition = lblFinalText.CaretIndex;
                                                string textBeforeCaret = lblFinalText.Text.Substring(0, caretPosition);
                                                string textAfterCaret = lblFinalText.Text.Substring(caretPosition);
                                                lblFinalText.Text = textBeforeCaret + replacementText + textAfterCaret;
                                                lblFinalText.CaretIndex = caretPosition + replacementText.Length;
                                                lblFinalText.Focus();
                                            }
                                        }
                                    }
                                }
                                
                                // اگر هر نوع دستوری اجرا شد، کلمه فعلی را تایپ نکن
                                if (commandExecuted)
                                {
                                    _lastProcessedWordIndex = nextWordIndex;
                                    _processingTimer.Start();
                                    _isProcessing = false;
                                    return;
                                }
                            }
                            
                            // اگر هیچ دستوری اجرا نشد، کلمه عادی است و باید تایپ شود
                            
                            // کلمه پردازش شده و آماده انتقال است
                            
                            // بررسی حالت انتقال لایو
                        if (_isLiveTransferActive)
                        {
                            // انتقال لایو: ارسال متن به پنجره فعال با رعایت تاخیر
                            // محاسبه موقعیت کلمه در متن اصلی برای تعیین نیاز به اسپیس
                            string recognizedText = lblRecognizedText.Text;
                            int wordStartIndex = 0;
                            
                            // پیدا کردن موقعیت کلمه فعلی در متن اصلی
                            for (int i = 0; i < nextWordIndex; i++)
                            {
                                if (i < _allWords.Count)
                                {
                                    int foundIndex = recognizedText.IndexOf(_allWords[i], wordStartIndex);
                                    if (foundIndex >= 0)
                                    {
                                        wordStartIndex = foundIndex + _allWords[i].Length;
                                    }
                                }
                            }
                            
                            // پیدا کردن موقعیت کلمه فعلی
                            int currentWordIndex = recognizedText.IndexOf(word, wordStartIndex);
                            
                            // تعیین متن برای ارسال با همان منطق انتقال به باکس متن نهایی
                            string textToSend = word;
                            
                            // اضافه کردن اسپیس فقط اگر _lastSentText خالی نباشد و کاراکتر آخر آن اسپیس نباشد
                            if (!string.IsNullOrEmpty(_lastSentText) && !_lastSentText.EndsWith(" "))
                            {
                                textToSend = " " + textToSend;
                            }
                            
                            try
                            {
                                // رعایت تاخیر WordByWordDelayMs در حالت لایو
                                Task.Run(async () => {
                                    await Task.Delay(_settings.WordByWordDelayMs);
                                    Dispatcher.Invoke(() => {
                                        try
                                        {
                                            SendTextToActiveWindowImproved(textToSend);
                                            _lastSentText += textToSend;
                                        }
                                        catch (Exception taskEx)
                                        {
                                            statusText.Text = Loc.Get("Main_St_SendErrorPrefix") + taskEx.Message;
                                        }
                                    });
                                });
                            }
                            catch (Exception ex)
                            {
                                statusText.Text = Loc.Get("Main_St_SendErrorPrefix") + ex.Message;
                            }
                        }
                        else
                        {
                            // حالت عادی: انتقال کلمه به متن نهایی
                            // Get the current caret position in the final text box
                            int caretPosition = lblFinalText.CaretIndex;

                            // Add space if needed
                            string newText = word;
                            if (!string.IsNullOrEmpty(lblFinalText.Text) && caretPosition > 0 &&
                                lblFinalText.Text[caretPosition - 1] != ' ')
                            {
                                newText = " " + newText;
                            }

                            // Get the text before and after caret position
                            string textBeforeCaret = lblFinalText.Text.Substring(0, caretPosition);
                            string textAfterCaret = lblFinalText.Text.Substring(caretPosition);

                            // Insert new text at caret position
                            lblFinalText.Text = textBeforeCaret + newText + textAfterCaret;

                            // Update caret position
                            lblFinalText.CaretIndex = caretPosition + newText.Length;

                            // Focus on final text box
                            lblFinalText.Focus();
                        }

                        // بروزرسانی متغیرهای کنترلی
                        _lastAddedWord = word;
                        _lastProcessedWordIndex = nextWordIndex;
                        }
                        else
                        {
                            // اگر کلمه خالی است، آن را رد کن و به کلمه بعدی برو
                            _lastProcessedWordIndex = nextWordIndex;
                        }

                        // ادامه دادن پردازش در تایمر بعدی
                        _processingTimer.Start();
                    }
                    else if (_allWords.Count > 0 && nextWordIndex >= _allWords.Count)
                    {
                        // اینجا تمام کلمات موجود پردازش شده‌اند
                        // نیازی به انجام کاری نیست، صبر میکنیم تا کلمات جدید اضافه شوند
                    }
                }
            }
            catch (Exception ex)
            {
                statusText.Text = Loc.Get("Main_St_ProcessErrorPrefix") + ex.Message;
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private void InactivityTimer_Tick(object sender, EventArgs e)
        {
            _inactivityTimer.Stop();
            
            // متوقف کردن تایمر پردازش برای جلوگیری از ادامه انتقال کلمه به کلمه
            _processingTimer.Stop();

            // Only reset microphone if there was text and we are still listening
            if (_hasRecognizedText && _isListening && !string.IsNullOrWhiteSpace(lblRecognizedText.Text))
            {
                // غیرفعال کردن باکس متن تشخیص داده شده قبل از ریست میکروفون
                lblRecognizedText.IsEnabled = false;
                
                // نمایش نوتیفیکیشن ریست میکروفون
                NotificationManager.ShowBalloonTip("میکروفون ریست شد", "میکروفون به دلیل عدم فعالیت ریست شد");
                // پیاده‌سازی منطق جدید: متوقف کردن انتقال کلمه به کلمه، مکث 0.5 ثانیه، ارسال یکجای باقی متن
                if (_isLiveTransferActive)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            // مکث 0.5 ثانیه برای اطمینان از توقف انتقال کلمه به کلمه
                            await Task.Delay(500);
                            
                            // ارسال یکجای باقی متن
                            Dispatcher.Invoke(() =>
                            {
                                BtnFastTransfer_Click(null, null);
                                statusText.Text = Loc.Get("Main_St_QuickTransferReset");
                            });
                            
                            // Wait a moment for the fast transfer to complete
                            await Task.Delay(200);
                            
                            // Then reset microphone by clicking Stop then Start after delay
                            try
                            {
                                // Click the Stop button
                                await Task.Run(() => btnStopMic_Click_Internal());

                                // Wait for Google Translate to settle back to idle before restarting
                                await Task.Delay(700);

                                // Click the Start button
                                await Task.Run(() => btnStartMic_Click_Internal());

                                Dispatcher.Invoke(() =>
                        {
                            statusText.Text = Loc.Get("Main_St_MicReset");
                            // فعال کردن مجدد باکس متن تشخیص داده شده بعد از ریست میکروفون
                            lblRecognizedText.IsEnabled = true;
                        });
                            }
                            catch (Exception micEx)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    statusText.Text = Loc.Get("Main_St_MicResetErrorPrefix") + micEx.Message;
                                    // فعال کردن مجدد باکس متن تشخیص داده شده در صورت بروز خطا
                                    lblRecognizedText.IsEnabled = true;
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                statusText.Text = Loc.Get("Main_St_AutoTransferErrorPrefix") + ex.Message;
                                // فعال کردن مجدد باکس متن تشخیص داده شده در صورت بروز خطا
                                lblRecognizedText.IsEnabled = true;
                            });
                        }
                    });
                }
                else
                {
                    // First click fast transfer button to quickly send all pending text
                    Dispatcher.Invoke(() =>
                    {
                        BtnFastTransfer_Click(null, null);
                        statusText.Text = Loc.Get("Main_St_QuickTransferReset");
                    });

                    // Wait a moment for the fast transfer to complete
                    Thread.Sleep(200);

                    // Then reset microphone by clicking Stop then Start after delay
                    Task.Run(async () =>
                    {
                        try
                        {
                            // Click the Stop button
                            await Task.Run(() => btnStopMic_Click_Internal());

                            // Wait for Google Translate to settle back to idle before restarting
                            await Task.Delay(700);

                            // Click the Start button
                            await Task.Run(() => btnStartMic_Click_Internal());

                            Dispatcher.Invoke(() =>
                            {
                                statusText.Text = Loc.Get("Main_St_MicReset");
                                // فعال کردن مجدد باکس متن تشخیص داده شده بعد از ریست میکروفون
                                lblRecognizedText.IsEnabled = true;
                            });
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                statusText.Text = Loc.Get("Main_St_MicResetErrorPrefix") + ex.Message;
                                // فعال کردن مجدد باکس متن تشخیص داده شده در صورت بروز خطا
                                lblRecognizedText.IsEnabled = true;
                            });
                        }
                    });
                }
            }
        }

        private bool _isProcessingText = false;
        
        private void LblRecognizedText_TextChanged(object sender, TextChangedEventArgs e)
        {
            // جلوگیری از پردازش مکرر
            if (_isProcessingText)
                return;
                
            _isProcessingText = true;
            
            try
            {
                // Stop any running inactivity timer
                _inactivityTimer.Stop();

                if (string.IsNullOrEmpty(lblRecognizedText.Text))
                    return;

                string newText = lblRecognizedText.Text;

                // Voice command processing disabled - handled by ProcessingTimer_Tick
                // فراخوانی ProcessVoiceCommands حذف شده - پردازش دستورات در ProcessingTimer_Tick انجام می‌شود

                // ذخیره زمان اولین تشخیص متن اگر تنظیم نشده است
                if (!_hasRecognizedText)
                {
                    _firstTextDetectedTime = DateTime.Now;
                    _hasRecognizedText = true;
                }

                _lastTextUpdateTime = DateTime.Now;

                // Start inactivity timer - will fire if 3 seconds pass with no new text
                _inactivityTimer.Start();

                // بررسی تغییرات متن و به‌روزرسانی لیست کلمات کامل
                UpdateWordsList(newText);

                // Start processing timer if not already running
                if (!_processingTimer.IsEnabled)
                {
                    _processingTimer.Start();
                }

                // Update the last processed text
                _lastProcessedText = newText;
            }
            finally
            {
                _isProcessingText = false;
            }
        }

        // متد جدید برای به‌روزرسانی لیست کلمات
        private void UpdateWordsList(string newText)
        {
            if (string.IsNullOrEmpty(newText))
                return;

            // تقسیم متن کامل به کلمات
            string[] currentWords = Regex.Split(newText.Trim(), @"\s+");

            // ایجاد لیست جدید کلمات
            List<string> updatedWords = new List<string>(currentWords);

            // به‌روزرسانی لیست اصلی کلمات
            _allWords = updatedWords;
        }

        private void BtnFastTransfer_Click(object sender, RoutedEventArgs e)
        {
            // Stop the timer
            _processingTimer.Stop();
            _inactivityTimer.Stop();

            if (_allWords.Count > 0)
            {
                // Get the current caret position
                int caretPosition = lblFinalText.CaretIndex;

                // انتقال تمام کلمات باقی‌مانده از آخرین کلمه پردازش شده تا انتهای لیست
                int startIdx = _lastProcessedWordIndex + 1;
                if (startIdx < 0) startIdx = 0;

                if (startIdx < _allWords.Count)
                {
                    // Extract remaining words
                    List<string> remainingWords = _allWords.GetRange(startIdx, _allWords.Count - startIdx);
                    string allWords = string.Join(" ", remainingWords);
                    
                    // حذف دستورات از متن قبل از انتقال
                    if (_voiceCommandProcessor != null && !string.IsNullOrEmpty(allWords))
                    {
                        var processResult = _voiceCommandProcessor.ProcessText(allWords);
                        allWords = processResult.ProcessedText;
                    }

                    if (!string.IsNullOrEmpty(allWords))
                    {
                        // Add space if needed
                        if (!string.IsNullOrEmpty(lblFinalText.Text) && caretPosition > 0 &&
                            lblFinalText.Text[caretPosition - 1] != ' ')
                        {
                            allWords = " " + allWords;
                        }

                        // Get the text before and after caret position
                        string textBeforeCaret = lblFinalText.Text.Substring(0, caretPosition);
                        string textAfterCaret = lblFinalText.Text.Substring(caretPosition);

                        // Insert all words at once
                        lblFinalText.Text = textBeforeCaret + allWords + textAfterCaret;

                        // Update caret position
                        lblFinalText.CaretIndex = caretPosition + allWords.Length;

                        // Update last added word
                        if (remainingWords.Count > 0)
                            _lastAddedWord = remainingWords.Last();

                        // بروزرسانی شمارشگر کلمات پردازش شده به آخرین کلمه
                        _lastProcessedWordIndex = _allWords.Count - 1;

                        // Focus on final text box
                        lblFinalText.Focus();

                        // اگر حالت انتقال لایو فعال است، متن را به برنامه خارجی ارسال کن
                        if (_isLiveTransferActive)
                        {
                            // پیاده‌سازی منطق جدید: متوقف کردن انتقال کلمه به کلمه، مکث 0.5 ثانیه، ارسال یکجای باقی متن
                            Task.Run(async () => {
                                try
                                {
                                    // مکث 0.5 ثانیه برای اطمینان از توقف انتقال کلمه به کلمه
                                    await Task.Delay(500);
                                    
                                    // ارسال یکجای باقی متن
                                    Dispatcher.Invoke(() => {
                                        try
                                        {
                                            // ارسال متن دقیقاً مطابق باقی‌مانده متن تشخیص داده شده
                                            string recognizedText = lblRecognizedText.Text;
                                            string sentText = _lastSentText.Replace(" ", ""); // حذف اسپیس‌های اضافی از متن ارسال شده
                                            
                                            // پیدا کردن موقعیت شروع متن باقی‌مانده در متن اصلی
                                            int remainingStartIndex = 0;
                                            if (!string.IsNullOrEmpty(sentText))
                                            {
                                                // پیدا کردن آخرین کلمه ارسال شده در متن اصلی
                                                string[] sentWords = sentText.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                                if (sentWords.Length > 0)
                                                {
                                                    string lastSentWord = sentWords[sentWords.Length - 1];
                                                    int lastWordIndex = recognizedText.LastIndexOf(lastSentWord);
                                                    if (lastWordIndex >= 0)
                                                    {
                                                        remainingStartIndex = lastWordIndex + lastSentWord.Length;
                                                    }
                                                }
                                            }
                                            
                                            // استخراج متن باقی‌مانده از متن اصلی
                                            string textToSend = "";
                                            if (remainingStartIndex < recognizedText.Length)
                                            {
                                                textToSend = recognizedText.Substring(remainingStartIndex);
                                            }
                                            else
                                            {
                                                textToSend = allWords; // fallback به روش قبلی
                                            }
                                            
                                            // حذف اسپیس‌های اضافی از ابتدای متن
                                            textToSend = textToSend.TrimStart();
                                            
                                            // اعمال منطق اسپیس مطابق با انتقال به باکس متن نهایی
                                            if (!string.IsNullOrEmpty(_lastSentText) && !_lastSentText.EndsWith(" ") && !string.IsNullOrEmpty(textToSend))
                                            {
                                                textToSend = " " + textToSend;
                                            }
                                            
                                            // اضافه کردن اسپیس در انتهای متن برای جلوگیری از چسبیدن کلمات بعدی
                                            if (!string.IsNullOrEmpty(textToSend) && !textToSend.EndsWith(" "))
                                            {
                                                textToSend += " ";
                                            }
                                            
                                            SendTextToActiveWindowImproved(textToSend);
                                            _lastSentText += textToSend;
                                            
                                            if (sender != null)
                                                statusText.Text = Loc.Get("Main_St_AllTransferredSent");
                                        }
                                        catch (Exception taskEx)
                                        {
                                            if (sender != null)
                                                statusText.Text = Loc.Get("Main_St_TransferredButSendErrorPrefix") + taskEx.Message;
                                        }
                                    });
                                }
                                catch (Exception ex)
                                {
                                    Dispatcher.Invoke(() => {
                                        if (sender != null)
                                            statusText.Text = Loc.Get("Main_St_QuickTransferErrorPrefix") + ex.Message;
                                    });
                                }
                            });
                        }
                        else
                        {
                            if (sender != null)
                                statusText.Text = Loc.Get("Main_St_AllTransferred");
                        }
                    }
                }
                else if (sender != null)
                {
                    statusText.Text = Loc.Get("Main_St_NoTextToTransfer");
                }
            }
            else if (sender != null)
            {
                statusText.Text = Loc.Get("Main_St_NoTextToTransfer");
            }

            // Clear the recognized text and reset last processed text
            lblRecognizedText.Text = "";
            _lastProcessedText = "";
            _hasRecognizedText = false;

            // بازنشانی متغیرهای کنترل انتقال
            ResetTransferVariables();
        }

        // متد جدید برای بازنشانی متغیرهای کنترل انتقال پس از انتقال سریع یا بازنشانی میکروفون
        private void ResetTransferVariables()
        {
            _lastProcessedWordIndex = -1;
            _allWords.Clear();
            _transferStarted = false;
            _initialDelayCompleted = false;
        }

        /// <summary>
        /// پاک‌سازی کامل وضعیت تشخیص گفتار. این متد پس از توقف میکروفون فراخوانی
        /// می‌شود تا متن قبلی در باکس‌های UI، بافر کلمات و textarea گوگل ترنسلیت
        /// باقی نماند و هنگام شروع مجدد، متن قدیمی به‌اشتباه دوباره پردازش نشود.
        /// چرا این مهم است: اگر پنجره مرورگر کرومیوم مینیمایز شود، Chrome صفحه را
        /// throttle می‌کند و textarea می‌تواند متن قدیمی را نگه دارد؛ هنگام شروع
        /// مجدد polling، همان متن دوباره به‌عنوان «متن جدید» به lblRecognizedText
        /// ست می‌شود و چرخهٔ انتقال از سر گرفته می‌شود.
        /// </summary>
        private void ClearAllRecognitionState(bool clearFinalText = true)
        {
            try
            {
                // متغیرهای داخلی
                _currentRecognizedText = string.Empty;
                _lastProcessedText = string.Empty;
                _lastAddedWord = string.Empty;
                _lastSentText = string.Empty;
                _pendingWords.Clear();
                _hasRecognizedText = false;

                ResetTransferVariables();

                // تاریخچهٔ دستورات صوتی نیز ریست شود تا فریز شدن آخرین دستور
                // باعث نشود کلمه‌ای دوباره به‌عنوان دستور تطبیق پیدا کند.
                try { _voiceCommandProcessor?.ResetWordHistory(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ResetWordHistory failed: {ex.Message}"); }

                // باکس‌های UI — حتماً روی UI thread
                if (Dispatcher.CheckAccess())
                {
                    lblRecognizedText.Text = string.Empty;
                    lblRecognizedText.IsEnabled = true;
                    if (clearFinalText)
                    {
                        lblFinalText.Text = string.Empty;
                    }
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        lblRecognizedText.Text = string.Empty;
                        lblRecognizedText.IsEnabled = true;
                        if (clearFinalText)
                        {
                            lblFinalText.Text = string.Empty;
                        }
                    });
                }

                // textarea گوگل ترنسلیت — کلیدی برای رفع باگ مینیمایز شدن مرورگر
                ClearGoogleTranslateTextarea();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ClearAllRecognitionState failed: {ex.Message}");
            }
        }

        /// <summary>
        /// پاک کردن متن باقی‌مانده در textarea گوگل ترنسلیت با JavaScript.
        /// این کار جلوی reload شدن متن قدیمی توسط polling در StartListeningAsync
        /// را می‌گیرد (مشکل بازتولید متن پس از مینیمایز شدن مرورگر).
        /// </summary>
        private void ClearGoogleTranslateTextarea()
        {
            try
            {
                if (_driver == null) return;
                var js = _driver as IJavaScriptExecutor;
                if (js == null) return;

                js.ExecuteScript(@"
                    try {
                        var ariaLabels = ['متن منبع', 'Source text', 'متن برای ترجمه', 'Text to translate', 'نوشتار مبدأ', 'نوشتن متن'];
                        var cleared = false;
                        for (var i = 0; i < ariaLabels.length; i++) {
                            var el = document.querySelector('textarea[aria-label=""' + ariaLabels[i] + '""]');
                            if (el) {
                                el.value = '';
                                el.dispatchEvent(new Event('input', { bubbles: true }));
                                el.dispatchEvent(new Event('change', { bubbles: true }));
                                cleared = true;
                            }
                        }
                        // fallback: any visible textarea
                        if (!cleared) {
                            var tas = document.querySelectorAll('textarea');
                            for (var j = 0; j < tas.length; j++) {
                                if (tas[j].offsetParent !== null) {
                                    tas[j].value = '';
                                    tas[j].dispatchEvent(new Event('input', { bubbles: true }));
                                }
                            }
                        }
                        // پاک کردن دکمهٔ X اگر در صفحه ظاهر شده باشد
                        var clearBtn = document.querySelector('button[aria-label=""Clear source text""], button[aria-label=""پاک کردن متن منبع""]');
                        if (clearBtn) { try { clearBtn.click(); } catch(e) {} }
                        return true;
                    } catch (e) {
                        return false;
                    }
                ");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ClearGoogleTranslateTextarea failed: {ex.Message}");
            }
        }

        private void BtnCopyRecognized_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(lblRecognizedText.Text))
            {
                try
                {
                    // Ensure we're on the UI thread and clipboard is available
                    Dispatcher.Invoke(() =>
                    {
                        System.Windows.Clipboard.Clear();
                        System.Windows.Clipboard.SetText(lblRecognizedText.Text);
                    });
                    statusText.Text = Loc.Get("Main_St_RecognizedCopied");
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    // Clipboard is busy, retry asynchronously without losing exceptions
                    string textToCopy = lblRecognizedText.Text;
                    Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        await Task.Delay(100);
                        try
                        {
                            System.Windows.Clipboard.SetText(textToCopy);
                            statusText.Text = Loc.Get("Main_St_RecognizedCopied");
                        }
                        catch (Exception ex)
                        {
                            statusText.Text = Loc.Get("Main_St_CopyErrorPrefix") + ex.Message;
                        }
                    }));
                }
                catch (Exception ex)
                {
                    statusText.Text = Loc.Get("Main_St_CopyErrorPrefix") + ex.Message;
                }
            }
        }

        private void BtnClearRecognized_Click(object sender, RoutedEventArgs e)
        {
            lblRecognizedText.Text = "";
            _pendingWords.Clear();
            _lastProcessedText = "";
            _processingTimer.Stop();
            
            // Reset voice command history when clearing text
            ResetVoiceCommandHistory();
            _inactivityTimer.Stop();
            _hasRecognizedText = false;

            // بازنشانی متغیرهای کنترل انتقال
            ResetTransferVariables();

            statusText.Text = Loc.Get("Main_St_RecognizedCleared");
        }

        private void WebLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }

        private void BtnCopyText_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(lblFinalText.Text))
            {
                try
                {
                    // Ensure we're on the UI thread and clipboard is available
                    Dispatcher.Invoke(() =>
                    {
                        System.Windows.Clipboard.Clear();
                        System.Windows.Clipboard.SetText(lblFinalText.Text);
                    });
                    statusText.Text = Loc.Get("Main_St_FinalCopied");
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    // Clipboard is busy, retry asynchronously without losing exceptions
                    string textToCopy = lblFinalText.Text;
                    Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        await Task.Delay(100);
                        try
                        {
                            System.Windows.Clipboard.SetText(textToCopy);
                            statusText.Text = Loc.Get("Main_St_FinalCopied");
                        }
                        catch (Exception ex)
                        {
                            statusText.Text = Loc.Get("Main_St_CopyErrorPrefix") + ex.Message;
                        }
                    }));
                }
                catch (Exception ex)
                {
                    statusText.Text = Loc.Get("Main_St_CopyErrorPrefix") + ex.Message;
                }
            }
        }

        private void BtnClearText_Click(object sender, RoutedEventArgs e)
        {
            lblFinalText.Text = "";
            statusText.Text = Loc.Get("Main_St_FinalCleared");
        }

        private void BtnLiveTransfer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _isLiveTransferActive = btnLiveTransfer.IsChecked == true;
                
                if (_isLiveTransferActive)
                {
                    // Enable live transfer mode
                    lblFinalText.IsEnabled = false;
                    lblFinalText.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)); // Darker background
                    statusText.Text = Loc.Get("Main_St_LiveOn");
                    _lastSentText = string.Empty;
                    
                    // راه‌اندازی و نمایش چراغ وضعیت
                    if (_statusIndicator == null)
                    {
                        InitializeStatusIndicator();
                    }
                    _statusIndicator?.Show();
                    
                    // به‌روزرسانی وضعیت چراغ بر اساس وضعیت میکروفون
                    UpdateStatusIndicatorBasedOnMicrophone();
                }
                else
                {
                    // Disable live transfer mode
                    lblFinalText.IsEnabled = true;
                    lblFinalText.Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)); // Original background
                    statusText.Text = Loc.Get("Main_St_LiveOff");
                    
                    // چراغ وضعیت همچنان نمایش داده می‌شود تا وضعیت میکروفون را نشان دهد
                }
            }
            catch (Exception ex)
            {
                statusText.Text = Loc.Get("Main_St_LiveToggleErrorPrefix") + ex.Message;
                // Reset toggle state on error
                btnLiveTransfer.IsChecked = false;
                _isLiveTransferActive = false;
                lblFinalText.IsEnabled = true;
                lblFinalText.Background = new SolidColorBrush(Color.FromRgb(42, 42, 42));
            }
        }

        private void SendTextToActiveWindow(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            try
            {
                // Convert text to char array for character-by-character sending
                char[] chars = text.ToCharArray();
                INPUT[] inputs = new INPUT[chars.Length * 2]; // Each character needs key down and key up

                for (int i = 0; i < chars.Length; i++)
                {
                    // Key down
                    inputs[i * 2] = new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        u = new InputUnion
                        {
                            ki = new KEYBDINPUT
                            {
                                wVk = 0,
                                wScan = chars[i],
                                dwFlags = KEYEVENTF_UNICODE,
                                time = 0,
                                dwExtraInfo = GetMessageExtraInfo()
                            }
                        }
                    };

                    // Key up
                    inputs[i * 2 + 1] = new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        u = new InputUnion
                        {
                            ki = new KEYBDINPUT
                            {
                                wVk = 0,
                                wScan = chars[i],
                                dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                                time = 0,
                                dwExtraInfo = GetMessageExtraInfo()
                            }
                        }
                    };
                }

                // Send the input
                uint result = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
                
                if (result == 0)
                {
                    statusText.Text = Loc.Get("Main_St_SendToActiveError");
                }
            }
            catch (Exception ex)
            {
                statusText.Text = Loc.Get("Main_St_SendErrorPrefix") + ex.Message;
            }
        }

        private void SendTextToActiveWindowImproved(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            try
            {
                // Get the current foreground window
                IntPtr foregroundWindow = GetForegroundWindow();
                
                // Get window title to identify problematic applications
                var windowTitle = new System.Text.StringBuilder(256);
                GetWindowText(foregroundWindow, windowTitle, windowTitle.Capacity);
                string title = windowTitle.ToString().ToLower();
                
                // Check if this is a known problematic application
                bool isProblematicApp = title.Contains("notepad") || 
                                       title.Contains("wordpad") || 
                                       title.Contains("calculator") ||
                                       title.Contains("paint");
                
                bool success = false;
                
                if (isProblematicApp)
                {
                    // For problematic apps, try SendKeys first with extra delays
                    success = SendTextWithSendKeysEnhanced(text);
                    
                    if (!success)
                    {
                        // Fallback to SendInput with longer delays
                        success = SendTextWithSendInputEnhanced(text);
                    }
                }
                else
                {
                    // For normal apps, try SendInput first
                    success = SendTextWithSendInput(text);
                    
                    if (!success)
                    {
                        // Fallback to SendKeys
                        SendTextWithSendKeys(text);
                    }
                }
            }
            catch (Exception ex)
            {
                statusText.Text = Loc.Get("Main_St_SendErrorPrefix") + ex.Message;
            }
        }

        private bool SendTextWithSendInput(string text)
        {
            try
            {
                char[] chars = text.ToCharArray();
                
                // Send characters one by one with delays for problematic apps
                foreach (char c in chars)
                {
                    // Skip control characters that might cause issues
                    if (char.IsControl(c) && c != '\t' && c != '\n' && c != '\r')
                        continue;
                        
                    INPUT[] inputs = new INPUT[2];
                    
                    // Key down
                    inputs[0] = new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        u = new InputUnion
                        {
                            ki = new KEYBDINPUT
                            {
                                wVk = 0,
                                wScan = c,
                                dwFlags = KEYEVENTF_UNICODE,
                                time = 0,
                                dwExtraInfo = GetMessageExtraInfo()
                            }
                        }
                    };

                    // Key up
                    inputs[1] = new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        u = new InputUnion
                        {
                            ki = new KEYBDINPUT
                            {
                                wVk = 0,
                                wScan = c,
                                dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                                time = 0,
                                dwExtraInfo = GetMessageExtraInfo()
                            }
                        }
                    };

                    uint result = SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
                    
                    if (result == 0)
                    {
                        return false; // SendInput failed
                    }
                    
                    // Longer delay between characters for better compatibility
                    Thread.Sleep(25);
                }
                
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void SendTextWithSendKeys(string text)
        {
            try
            {
                // Use SendKeys as fallback method with character-by-character sending
                char[] chars = text.ToCharArray();
                
                foreach (char c in chars)
                {
                    // Skip control characters that might cause issues
                    if (char.IsControl(c) && c != '\t' && c != '\n' && c != '\r')
                        continue;
                        
                    // Send each character individually with proper escaping
                    string charToSend = c.ToString();
                    
                    // Escape special SendKeys characters
                    if ("{}()[]^%~+".Contains(c))
                    {
                        charToSend = "{" + c + "}";
                    }
                    
                    System.Windows.Forms.SendKeys.SendWait(charToSend);
                    
                    // Small delay between characters
                    Thread.Sleep(15);
                }
            }
            catch (Exception ex)
            {
                statusText.Text = Loc.Get("Main_St_SendKeysErrorPrefix") + ex.Message;
            }
        }
        
        private bool SendTextWithSendKeysEnhanced(string text)
        {
            try
            {
                // For problematic apps, send text word by word with longer delays
                string[] words = text.Split(' ');
                
                for (int i = 0; i < words.Length; i++)
                {
                    string word = words[i];
                    
                    // Send each character of the word
                    foreach (char c in word)
                    {
                        // Skip control characters except space, tab, and newline
                        if (char.IsControl(c) && c != ' ' && c != '\t' && c != '\n' && c != '\r')
                            continue;
                        
                        string charToSend = c.ToString();
                        
                        // Escape special characters for SendKeys
                        if ("{}()[]^%~+".Contains(c))
                        {
                            charToSend = "{" + c + "}";
                        }
                        
                        System.Windows.Forms.SendKeys.SendWait(charToSend);
                        Thread.Sleep(30); // Longer delay for problematic apps
                    }
                    
                    // Add space between words (except for the last word)
                    if (i < words.Length - 1)
                    {
                        System.Windows.Forms.SendKeys.SendWait(" ");
                        Thread.Sleep(50); // Extra delay after space
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        private bool SendTextWithSendInputEnhanced(string text)
        {
            try
            {
                // For problematic apps, use longer delays with SendInput
                foreach (char c in text)
                {
                    // Skip non-essential control characters
                    if (char.IsControl(c) && c != ' ' && c != '\t' && c != '\n' && c != '\r')
                        continue;
                    
                    INPUT[] inputs = new INPUT[2];
                    
                    // Key down
                    inputs[0] = new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        u = new InputUnion
                        {
                            ki = new KEYBDINPUT
                            {
                                wVk = 0,
                                wScan = c,
                                dwFlags = KEYEVENTF_UNICODE,
                                time = 0,
                                dwExtraInfo = GetMessageExtraInfo()
                            }
                        }
                    };

                    // Key up
                    inputs[1] = new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        u = new InputUnion
                        {
                            ki = new KEYBDINPUT
                            {
                                wVk = 0,
                                wScan = c,
                                dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                                time = 0,
                                dwExtraInfo = GetMessageExtraInfo()
                            }
                        }
                    };
                    
                    uint result = SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
                    
                    if (result == 0)
                        return false;
                    
                    Thread.Sleep(50); // Much longer delay for problematic apps
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        /// <summary>
        /// Loads the app icon from the embedded multi-resolution app-icon.ico. Using the embedded
        /// resource (rather than ExtractAssociatedIcon, which yields only a single 32px frame and
        /// can be served stale from Windows' icon cache) makes the tray icon AND the notification
        /// balloon show the current logo crisply at every size.
        /// </summary>
        private static System.Drawing.Icon LoadAppIcon()
        {
            try
            {
                var sri = System.Windows.Application.GetResourceStream(
                    new Uri("pack://application:,,,/Assets/app-icon.ico"));
                if (sri?.Stream != null)
                    return new System.Drawing.Icon(sri.Stream);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadAppIcon from resource failed: {ex.Message}");
            }
            try { return System.Drawing.Icon.ExtractAssociatedIcon(Path.Combine(AppContext.BaseDirectory, "Cloudict.exe")); }
            catch { return System.Drawing.SystemIcons.Application; }
        }

        private void SetupNotifyIcon()
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = LoadAppIcon(),
                Text = "Cloudict",
                Visible = true
            };

            _notifyIcon.DoubleClick += (s, e) =>
            {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.Activate();
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("نمایش", null, (s, e) =>
            {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.Activate();
            });

            contextMenu.Items.Add("شروع/توقف تبدیل گفتار به متن", null, (s, e) =>
            {
                btnMicrophone_Click(null, null);
            });

            contextMenu.Items.Add("خروج", null, (s, e) =>
            {
                CleanupAndClose();
                System.Windows.Application.Current.Shutdown();
            });

            _notifyIcon.ContextMenuStrip = contextMenu;

            // Share this single NotifyIcon with NotificationManager so balloon
            // tips don't create a second tray entry (which previously appeared
            // as the blue "i" info icon).
            NotificationManager.Register(_notifyIcon);
        }

        #region Browser Setup and Management

        private void SetupBrowserDriver(bool preload)
        {
            try
            {
                statusText.Text = Loc.Get("Main_St_PreparingBrowser");

                Task.Run(async () => {
                    try
                    {
                        // Setup Chrome driver
                        new DriverManager().SetUpDriver(new ChromeConfig(), VersionResolveStrategy.MatchingBrowser);

                        if (preload)
                        {
                            // Preload browser
                            Dispatcher.Invoke(() => {
                                statusText.Text = Loc.Get("Main_St_PreInitGT");
                            });

                            try
                            {
                                _preloadedDriver = InitializeBrowserInstance();

                                // Open Google Translate
                                _preloadedDriver.Navigate().GoToUrl(BuildGoogleTranslateUrl());

                                // Allow page to load
                                await Task.Delay(5000);

                                Dispatcher.Invoke(() => {
                                    statusText.Text = Loc.Get("Main_St_GTReady");
                                    btnMicrophone.IsEnabled = true;
                                    btnStartMic.IsEnabled = true;
                                    btnStopMic.IsEnabled = true;
                                    _browserReady = true;
                                });
                            }
                            catch (Exception ex)
                            {
                                Dispatcher.Invoke(() => {
                                    statusText.Text = Loc.Get("Main_St_PreInitIncompletePrefix") + ex.Message;
                                    btnMicrophone.IsEnabled = true; // Let user try again
                                });
                                if (_preloadedDriver != null)
                                {
                                    _preloadedDriver.Quit();
                                    _preloadedDriver = null;
                                }
                            }
                        }
                        else
                        {
                            Dispatcher.Invoke(() => {
                                statusText.Text = Loc.Get("Main_St_BrowserReady");
                                btnMicrophone.IsEnabled = true;
                                btnStartMic.IsEnabled = true;
                                btnStopMic.IsEnabled = true;
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => {
                            statusText.Text = Loc.Get("Main_St_BrowserPrepErrorPrefix") + ex.Message;
                            btnMicrophone.IsEnabled = true;
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                statusText.Text = Loc.Get("Main_St_ErrorPrefix") + ex.Message;
                btnMicrophone.IsEnabled = true;
            }
        }

        private IWebDriver InitializeBrowserInstance()
        {
            var options = new ChromeOptions();
            options.AddArgument("--disable-gpu");
            options.AddArgument("--window-size=450,540");
            options.AddArgument("--disable-extensions");
            options.AddArgument("--disable-popup-blocking");
            options.AddArgument("--disable-default-apps");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--use-fake-ui-for-media-stream"); // Auto-allow microphone
            options.AddArgument("--lang=fa");

            // Allow microphone access
            options.AddUserProfilePreference("profile.default_content_setting_values.media_stream_mic", 1);

            // Set Persian language
            options.AddUserProfilePreference("intl.accept_languages", "fa-IR,fa");

            // Hide ChromeDriver console window
            var driverService = ChromeDriverService.CreateDefaultService();
            driverService.HideCommandPromptWindow = true;

            return new ChromeDriver(driverService, options);
        }

        private bool InitializeBrowser()
        {
            try
            {
                if (_driver != null)
                {
                    // Just reset instead of closing
                    try
                    {
                        // Reset any existing text in the Google Translate box but not in our UI
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => {
                            statusText.Text = Loc.Get("Main_St_ResetErrorPrefix") + ex.Message + Loc.Get("Main_St_ReopeningBrowserSuffix");
                        });

                        try
                        {
                            _driver.Quit();
                        }
                        catch (Exception quitEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"خطا در Quit مرورگر قبل از reset: {quitEx.Message}");
                        }
                        _driver = null;
                    }
                }

                // Use preloaded driver if available
                if (_preloadedDriver != null)
                {
                    _driver = _preloadedDriver;
                    _preloadedDriver = null;

                    try
                    {
                        // Reset window size
                        _driver.Manage().Window.Size = new System.Drawing.Size(450, 540);

                        // Check status and reset page if needed
                        if (!_driver.Url.Contains("translate.google.com"))
                        {
                            Dispatcher.Invoke(() => {
                                statusText.Text = Loc.Get("Main_St_ResettingGTPage");
                            });

                            _driver.Navigate().GoToUrl(BuildGoogleTranslateUrl());
                            Thread.Sleep(2000);
                        }
                    }
                    catch
                    {
                        // Relaunch browser on error
                        _driver.Quit();
                        _driver = null;
                        _driver = InitializeBrowserInstance();
                        _driver.Navigate().GoToUrl(BuildGoogleTranslateUrl());
                    }
                }
                else
                {
                    // Launch new browser
                    _driver = InitializeBrowserInstance();

                    Dispatcher.Invoke(() => {
                        statusText.Text = Loc.Get("Main_St_OpeningGT");
                    });

                    // Google Translate URL with appropriate parameters
                    _driver.Navigate().GoToUrl(BuildGoogleTranslateUrl());

                    Dispatcher.Invoke(() => {
                        statusText.Text = Loc.Get("Main_St_WaitingPageLoad");
                    });
                }

                // Wait for page to load
                new WebDriverWait(_driver, TimeSpan.FromSeconds(30)).Until(
                    d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").Equals("complete"));

                // Add delay to ensure all page elements are fully loaded
                Thread.Sleep(1000);

                Dispatcher.Invoke(() => {
                    statusText.Text = Loc.Get("Main_St_BrowserReadyPressGreen");
                });



                return true;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => {
                    statusText.Text = Loc.Get("Main_St_BrowserStartErrorPrefix") + ex.Message;
                    if (_driver != null)
                    {
                        try
                        {
                            // Save debug info
                            string pageSource = _driver.PageSource;
                            File.WriteAllText("page_debug.html", pageSource);
                            statusText.Text += " | فایل دیباگ در مسیر برنامه ذخیره شد";

                            var screenshot = ((ITakesScreenshot)_driver).GetScreenshot();
                            screenshot.SaveAsFile("error_screenshot.png");
                        }
                        catch { }
                    }
                });
                return false;
            }
        }

        #endregion

        #region Microphone Control

        /// <summary>
        /// The microphone-button selectors to try, in order. The user-configured selector
        /// (Settings → Google Translate) is tried first so the mic can be fixed for any
        /// language or page change without recompiling; the built-in structural XPaths follow
        /// as fallbacks.
        /// </summary>
        private IEnumerable<string> GetMicButtonXPaths()
        {
            if (!string.IsNullOrWhiteSpace(_settings?.MicButtonXPath))
                yield return _settings.MicButtonXPath.Trim();
            foreach (var x in MIC_BUTTON_XPATHS)
                yield return x;
        }

        private bool ActivateMicrophone()
        {
            try
            {
                Dispatcher.Invoke(() => {
                    statusText.Text = Loc.Get("Main_St_ActivatingMic");
                });

                // Add page readiness check before microphone operations
                try
                {
                    // Wait for page elements to be fully interactive with reduced timeout
                    var pageWait = new WebDriverWait(_driver, TimeSpan.FromSeconds(1));
                    pageWait.Until(driver => {
                        try {
                            return ((IJavaScriptExecutor)driver).ExecuteScript(
                                "return document.readyState === 'complete' && document.querySelectorAll('button').length > 0"
                            ).Equals(true);
                        } catch {
                            return false;
                        }
                    });
                    
                    Thread.Sleep(300); // Reduced delay for faster response
                }
                catch (Exception)
                {
                    Dispatcher.Invoke(() => {
                        statusText.Text = Loc.Get("Main_St_PageNotReadyContinue");
                    });
                }

                // Retry mechanism for microphone activation
                bool clicked = false;
                int maxRetries = 3;
                
                for (int retry = 0; retry < maxRetries && !clicked; retry++)
                {
                    try
                    {
                        // Reduced timeout for faster response
                        var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(1));
                        
                        if (retry > 0)
                        {
                            Dispatcher.Invoke(() => {
                                statusText.Text = Loc.Get("Main_St_MicAttempt", retry + 1);
                            });
                            Thread.Sleep(300); // Reduced wait between retries
                        }

                        // Primary, language-agnostic path: the Google Translate voice button is a
                        // single toggle with a stable jsname; while listening it has the 'XiUwde'
                        // class. Click it only when it isn't already listening (so we never toggle
                        // an already-active mic off).
                        if (!clicked)
                        {
                            try
                            {
                                bool ok = (bool)((IJavaScriptExecutor)_driver).ExecuteScript(@"
                                    var b = document.querySelector('button[jsname=""Sz6qce""]');
                                    if(!b) return false;
                                    if(!b.classList.contains('XiUwde')) b.click();
                                    return true;");
                                if (ok)
                                {
                                    clicked = true;
                                    Dispatcher.Invoke(() => {
                                        statusText.Text = Loc.Get("Main_St_MicActivatedXPath", "jsname=Sz6qce");
                                    });
                                }
                            }
                            catch { }
                        }

                        // Fallback: configured selector + built-in structural XPaths.
                        if (!clicked)
                        foreach (string xpath in GetMicButtonXPaths())
                        {
                            try
                            {
                                var micButton = wait.Until(driver => {
                                    try {
                                        var element = driver.FindElement(By.XPath(xpath));
                                        return element.Displayed && element.Enabled ? element : null;
                                    } catch {
                                        return null;
                                    }
                                });
                                
                                if (micButton != null)
                                {
                                    micButton.Click();
                                    clicked = true;
                                    Dispatcher.Invoke(() => {
                                        statusText.Text = Loc.Get("Main_St_MicActivatedXPath", xpath.Substring(Math.Max(0, xpath.Length - 25)));
                                    });
                                    break;
                                }
                            }
                            catch (Exception)
                            {
                                // Continue to next XPath
                                continue;
                            }
                        }
                        
                        if (!clicked && retry == 0)
                        {
                            Dispatcher.Invoke(() => {
                                statusText.Text = Loc.Get("Main_St_MicXPathNotFoundTryOther");
                            });
                        }

                        // If XPath failed, try with JavaScript
                        if (!clicked)
                        {
                            var js = (IJavaScriptExecutor)_driver;
                            clicked = (bool)js.ExecuteScript(@"
                                try {
                                    // Try to find by aria-label (backup method)
                                    var micBtns = document.querySelectorAll('button[aria-label*=""ترجمه گفتار""], button[aria-label*=""Translate by voice""]');
                                    if(micBtns.length > 0) {
                                        micBtns[0].click();
                                        return true;
                                    }
                                    
                                    // Try with general mic-related class or attributes
                                    var anyMicBtns = document.querySelectorAll('button.ita-kd-icon-button, button[data-mic=""true""], button[jsname=""W5Dscf""]');
                                    if(anyMicBtns.length > 0) {
                                        anyMicBtns[0].click();
                                        return true;
                                    }
                                    
                                    return false;
                                } catch(e) {
                                    console.error(e);
                                    return false;
                                }
                            ");
                        }

                        // If still not clicked, try another approach with more general selectors
                        if (!clicked)
                        {
                            try
                            {
                                // Try finding any button that looks like a microphone button
                                var buttons = _driver.FindElements(By.TagName("button"));

                                foreach (var button in buttons)
                                {
                                    try
                                    {
                                        string ariaLabel = button.GetAttribute("aria-label") ?? "";
                                        string className = button.GetAttribute("class") ?? "";

                                        if (ariaLabel.Contains("گفتار") || ariaLabel.Contains("میکروفون") ||
                                            ariaLabel.Contains("voice") || ariaLabel.Contains("Microphone") ||
                                            className.Contains("mic") || className.Contains("voice"))
                                        {
                                            button.Click();
                                            clicked = true;
                                            break;
                                        }
                                    }
                                    catch { continue; }
                                }
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (retry == maxRetries - 1)
                        {
                            Dispatcher.Invoke(() => {
                                statusText.Text = Loc.Get("Main_St_MicActivateErrorPrefix") + ex.Message;
                            });
                        }
                    }
                }

                if (clicked)
                {
                    Dispatcher.Invoke(() => {
                        statusText.Text = Loc.Get("Main_St_MicActivated");
                    });
                    return true;
                }
                else
                {
                    Dispatcher.Invoke(() => {
                        statusText.Text = Loc.Get("Main_St_MicButtonNotFound");
                    });
                    return false;
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => {
                    statusText.Text = Loc.Get("Main_St_MicEnableErrorPrefix") + ex.Message;
                });
                return false;
            }
        }

        private bool DeactivateMicrophone()
        {
            try
            {
                bool clicked = false;

                // Try JavaScript first for more reliability
                var js = (IJavaScriptExecutor)_driver;
                clicked = (bool)js.ExecuteScript(@"
                    try {
                        // Primary: the voice button is a toggle with a stable jsname; the 'XiUwde'
                        // class means it is currently listening. Click it only if it is on.
                        var toggle = document.querySelector('button[jsname=""Sz6qce""]');
                        if(toggle) {
                            if(toggle.classList.contains('XiUwde')) toggle.click();
                            return true;
                        }

                        // Legacy fallbacks (older page layouts)
                        var micBtns = document.querySelectorAll('button[jsname=""W5Dscf""], button.goxjub, div.goxjub');
                        if(micBtns.length > 0) {
                            micBtns[0].click();
                            return true;
                        }
                        
                        // Try with specific aria-label
                        var stopButtons = document.querySelectorAll('button[aria-label*=""متوقف کردن""], button[aria-label*=""توقف""], div[role=""button""][aria-label*=""توقف""]');
                        if(stopButtons.length > 0) {
                            stopButtons[0].click();
                            return true;
                        }
                        
                        // Look for any div that might be a stop button
                        var divs = document.querySelectorAll('div[role=""button""]');
                        for(var i=0; i < divs.length; i++) {
                            if(divs[i].classList.contains('goxjub') || 
                              divs[i].getAttribute('aria-label')?.includes('توقف')) {
                                divs[i].click();
                                return true;
                            }
                        }
                        
                        return false;
                    } catch(e) {
                        console.error(e);
                        return false;
                    }
                ");

                if (clicked)
                {
                    Dispatcher.Invoke(() => {
                        statusText.Text = Loc.Get("Main_St_StopClickedJS");
                    });
                    return true;
                }

                // Try with the exact XPath if JavaScript failed
                try
                {
                    var stopButton = _driver.FindElement(By.XPath(STOP_BUTTON_XPATH));
                    stopButton.Click();
                    clicked = true;

                    Dispatcher.Invoke(() => {
                        statusText.Text = Loc.Get("Main_St_StopClickedXPath");
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => {
                        statusText.Text = Loc.Get("Main_St_StopXPathErrorPrefix") + ex.Message;
                    });
                }

                // Try with different XPath patterns
                if (!clicked)
                {
                    string[] xpaths = {
                        "//div[@role='button' and contains(@aria-label, 'توقف')]",
                        "//div[@jsname='W5Dscf']",
                        "//button[contains(@class, 'goxjub')]",
                        "//div[contains(@class, 'goxjub')]"
                    };

                    foreach (string xpath in xpaths)
                    {
                        try
                        {
                            var element = _driver.FindElement(By.XPath(xpath));
                            element.Click();
                            clicked = true;

                            Dispatcher.Invoke(() => {
                                statusText.Text = Loc.Get("Main_St_StopClickedAltXPath");
                            });

                            break;
                        }
                        catch (Exception stopEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"کلیک xpath جایگزین دکمه توقف ناموفق: {stopEx.Message}");
                        }
                    }
                }

                // Try with Escape key as last resort
                if (!clicked)
                {
                    try
                    {
                        OpenQA.Selenium.Interactions.Actions action = new OpenQA.Selenium.Interactions.Actions(_driver);
                        action.SendKeys(OpenQA.Selenium.Keys.Escape).Perform();
                        clicked = true;

                        Dispatcher.Invoke(() => {
                            statusText.Text = Loc.Get("Main_St_StopViaEsc");
                        });
                    }
                    catch (Exception escEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"ESC fallback برای توقف میکروفون ناموفق: {escEx.Message}");
                    }
                }

                return clicked;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => {
                    statusText.Text = Loc.Get("Main_St_MicDisableErrorPrefix") + ex.Message;
                });
                return false;
            }
        }

        // Method for direct microphone start button
        private async void btnStartMic_Click(object sender, RoutedEventArgs e)
        {
            if (_driver == null)
            {
                statusText.Text = Loc.Get("Main_St_OpenChromeFirstRed");
                return;
            }
            
            btnStartMic.IsEnabled = false;
            try
            {
                // Always allow start click - remove _isListening check
                statusText.Text = Loc.Get("Main_St_StartingRecognition");

                // پاک‌سازی کامل state قبل از شروع (بدون پاک کردن متن نهایی)
                // این کار جلوی reload شدن متن قدیمی از textarea گوگل ترنسلیت را می‌گیرد —
                // مخصوصاً وقتی پنجره مرورگر کرومیوم قبلاً مینیمایز شده باشد و متن قبلی
                // هنوز در صفحه باقی مانده است.
                ClearAllRecognitionState(clearFinalText: false);

                // Activate microphone
                bool micActivated = await Task.Run(() => ActivateMicrophone());
                
                if (micActivated)
                {
                    _isListening = true;
                    _cancellationTokenSource?.Cancel();
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = new CancellationTokenSource();
                    
                    // Reset text variables only after microphone is actually activated
                    _currentRecognizedText = "";
                    _lastProcessedText = "";
                    
                    // راه‌اندازی و نمایش چراغ وضعیت همیشه
                    if (_statusIndicator == null)
                    {
                        InitializeStatusIndicator();
                    }
                    _statusIndicator?.Show();
                    
                    // به‌روزرسانی چراغ وضعیت
                    UpdateStatusIndicatorBasedOnMicrophone();
                    
                    // نمایش نوتیفیکیشن فعال شدن میکروفون
                    NotificationManager.ShowBalloonTip("میکروفون", "میکروفون فعال شد", System.Windows.Forms.ToolTipIcon.Info);
                    
                    statusText.Text = Loc.Get("Main_WaitingForSpeech");
                    
                    // Add delay before starting text monitoring to prevent reading old text
                    _ = Task.Run(async () => {
                        await Task.Delay(2000); // Wait 2 seconds for microphone to fully activate
                        if (!_cancellationTokenSource.Token.IsCancellationRequested)
                        {
                            Dispatcher.Invoke(() => {
                                statusText.Text = Loc.Get("Main_St_Listening");
                            });
                            await StartListeningAsync(_cancellationTokenSource.Token);
                        }
                    });
                }
                else
                {
                    statusText.Text = Loc.Get("Main_St_MicEnableError");
                    // به‌روزرسانی چراغ وضعیت در صورت خطا
                    UpdateStatusIndicatorBasedOnMicrophone();
                }
            }
            catch (Exception ex)
            {
                statusText.Text = Loc.Get("Main_St_ErrorPrefix") + ex.Message;
            }
            finally
            {
                btnStartMic.IsEnabled = true;
            }
        }

        // Internal method for start button click without UI interaction
        private void btnStartMic_Click_Internal()
        {
            try
            {
                bool clicked = false;
                try
                {
                    // ابتدا تلاش با XPath دقیق
                    try
                    {
                        // استفاده از XPath اصلی جدید کاربر
                        var micButton = _driver.FindElement(By.XPath(MIC_BUTTON_XPATH));
                        micButton.Click();
                        clicked = true;
                    }
                    catch
                    {
                        // Try all alternative XPaths for different button states
                        foreach (string xpath in GetMicButtonXPaths())
                        {
                            try
                            {
                                var micButton = _driver.FindElement(By.XPath(xpath));
                                if (micButton != null && micButton.Displayed && micButton.Enabled)
                                {
                                    micButton.Click();
                                    clicked = true;
                                    Dispatcher.Invoke(() => {
                                        statusText.Text = Loc.Get("Main_St_MicClickedAltXPath", xpath.Substring(Math.Max(0, xpath.Length - 25)));
                                    });
                                    break;
                                }
                            }
                            catch
                            {
                                continue;
                            }
                        }
                        
                        // If still not clicked, try JavaScript with aria-label
                        if (!clicked)
                        {
                            var js = (IJavaScriptExecutor)_driver;
                            clicked = (bool)js.ExecuteScript(@"
                            try {
                                var micBtns = document.querySelectorAll('button[aria-label=""ترجمه گفتار""]');
                                if(micBtns.length > 0) {
                                    micBtns[0].click();
                                    return true;
                                }
                                return false;
                            } catch(e) {
                                return false;
                            }
                        ");
                        }
                    }
                    Dispatcher.Invoke(() =>
                    {
                        if (clicked)
                            statusText.Text = Loc.Get("Main_St_MicClickedOk");
                        else
                            statusText.Text = Loc.Get("Main_St_MicNotFound2");
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        statusText.Text = Loc.Get("Main_St_MicClickErrorPrefix") + ex.Message;
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => {
                    statusText.Text = Loc.Get("Main_St_StartClickErrorPrefix") + ex.Message;
                });
            }
        }

        // Method for direct microphone stop button
        private async void btnStopMic_Click(object sender, RoutedEventArgs e)
        {
            if (_driver == null)
            {
                statusText.Text = Loc.Get("Main_St_BrowserNotActive");
                return;
            }

            btnStopMic.IsEnabled = false;

            try
            {
                // Always allow stop click - remove _isListening check
                statusText.Text = Loc.Get("Main_St_StoppingRecognition");

                // متوقف کردن تایمرها قبل از deactivation تا کلمات قدیمی در صف اجرا نشوند
                _processingTimer?.Stop();
                _inactivityTimer?.Stop();

                // Cancel listening task
                _cancellationTokenSource?.Cancel();

                // Deactivate microphone
                bool micDeactivated = await Task.Run(() => DeactivateMicrophone());

                _isListening = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                // پاک‌سازی کامل state تشخیص: متن باکس‌ها، بافر کلمات، textarea گوگل ترنسلیت
                // این کار جلوی بازتولید متن قبلی پس از مینیمایز شدن مرورگر کرومیوم را می‌گیرد
                // و مطابق درخواست کاربر، باکس متن نهایی نیز پاک می‌شود.
                ClearAllRecognitionState(clearFinalText: true);

                // به‌روزرسانی چراغ وضعیت
                UpdateStatusIndicatorBasedOnMicrophone();

                // نمایش نوتیفیکیشن متوقف شدن میکروفون
                NotificationManager.ShowBalloonTip("میکروفون", Loc.Get("Main_St_MicStopped"), System.Windows.Forms.ToolTipIcon.Info);

                statusText.Text = Loc.Get("Main_St_RecognitionStopped");
            }
            catch (Exception ex)
            {
                statusText.Text = Loc.Get("Main_St_ErrorPrefix") + ex.Message;

                // Reset state on error
                _isListening = false;
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                // در صورت خطا هم سعی می‌کنیم state تشخیص را تمیز نگه داریم
                try { ClearAllRecognitionState(clearFinalText: true); }
                catch (Exception clearEx) { System.Diagnostics.Debug.WriteLine($"Cleanup after stop error failed: {clearEx.Message}"); }

                // به‌روزرسانی چراغ وضعیت در صورت خطا
                UpdateStatusIndicatorBasedOnMicrophone();
            }
            finally
            {
                btnStopMic.IsEnabled = true;
            }
        }

        // Internal method for stop button click without UI interaction
        private void btnStopMic_Click_Internal()
        {
            try
            {
                bool clicked = false;

                // Try finding the exact element using the provided XPath
                try
                {
                    var button = _driver.FindElement(By.XPath(STOP_SPEECH_TRANSLATION_BUTTON_XPATH));
                    button.Click();
                    clicked = true;

                    Dispatcher.Invoke(() => {
                        statusText.Text = Loc.Get("Main_St_StopClickedDirect");
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => {
                        statusText.Text = Loc.Get("Main_St_StopDirectErrorPrefix") + ex.Message;
                    });
                }

                // If direct XPath failed, try using JavaScript
                if (!clicked)
                {
                    var js = (IJavaScriptExecutor)_driver;
                    clicked = (bool)js.ExecuteScript(@"
                        try {
                            // Try to find by aria-label
                            var btn = document.querySelector('button[aria-label=""متوقف کردن ترجمه گفتار""]');
                            if(btn) {
                                btn.click();
                                return true;
                            }

                            // Try with any stop button
                            var stopBtns = document.querySelectorAll('button[aria-label*=""متوقف کردن""], button[aria-label*=""توقف""]');
                            if(stopBtns.length > 0) {
                                stopBtns[0].click();
                                return true;
                            }
                            
                            return false;
                        } catch(e) {
                            console.error(e);
                            return false;
                        }
                    ");

                    if (clicked)
                    {
                        Dispatcher.Invoke(() => {
                            statusText.Text = Loc.Get("Main_St_StopClickedJS2");
                        });
                    }
                }

                // If still not clicked, use the general deactivation method
                if (!clicked)
                {
                    if (DeactivateMicrophone())
                    {
                        clicked = true;
                        Dispatcher.Invoke(() => {
                            statusText.Text = Loc.Get("Main_St_MicStoppedGeneric");
                        });
                    }
                }

                if (!clicked)
                {
                    Dispatcher.Invoke(() => {
                        statusText.Text = Loc.Get("Main_St_StopAllFailed");
                    });
                }
                else
                {
                    Dispatcher.Invoke(() => {
                        statusText.Text = Loc.Get("Main_St_MicStopped");
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => {
                    statusText.Text = Loc.Get("Main_St_MicStopErrorPrefix") + ex.Message;
                });
            }
        }

        #endregion

        #region Speech Recognition

        private async void btnMicrophone_Click(object sender, RoutedEventArgs e)
        {
            // Disable button during processing
            if (sender != null) btnMicrophone.IsEnabled = false;

            try
            {
                // Toggle Chrome browser state
                if (_driver == null)
                {
                    // Open Chrome with Google Translate
                    statusText.Text = Loc.Get("Main_St_OpeningChrome");
                    
                    await Task.Run(() =>
                    {
                        try
                        {
                            if (InitializeBrowser())
                            {
                                Dispatcher.Invoke(() => {
                                    statusText.Text = Loc.Get("Main_St_ChromeGTReady");
                                    btnMicrophone.Content = "❌"; // Close icon
                                    if (txtHelperBrowserLabel != null) txtHelperBrowserLabel.Text = Loc.Get("Main_CloseHelperBrowser");
                                });
                            }
                            else
                            {
                                throw new Exception("نتوانستیم مرورگر را آماده کنیم");
                            }
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                statusText.Text = Loc.Get("Main_St_OpenBrowserErrorPrefix") + ex.Message;
                            });
                        }
                    });
                }
                else
                {
                    // Close Chrome browser
                    statusText.Text = Loc.Get("Main_St_ClosingChrome");
                    
                    await Task.Run(() =>
                    {
                        try
                        {
                            _driver?.Quit();
                            _driver = null;
                            
                            Dispatcher.Invoke(() => {
                                statusText.Text = Loc.Get("Main_St_ChromeClosed");
                                btnMicrophone.Content = "🌐"; // Globe icon for opening browser
                                if (txtHelperBrowserLabel != null) txtHelperBrowserLabel.Text = Loc.Get("Main_OpenHelperBrowser");
                            });
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                statusText.Text = Loc.Get("Main_St_CloseBrowserErrorPrefix") + ex.Message;
                            });
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                statusText.Text = Loc.Get("Main_St_ErrorPrefix") + ex.Message;
            }
            finally
            {
                if (sender != null) btnMicrophone.IsEnabled = true;
            }
        }

        // New method for speech recognition control
        private async void btnStartListening_Click(object sender, RoutedEventArgs e)
        {
            // Disable button during processing
            if (sender != null) ((System.Windows.Controls.Button)sender).IsEnabled = false;

            try
            {
                // Check if browser is available
                if (_driver == null)
                {
                    statusText.Text = Loc.Get("Main_St_OpenChromeFirst");
                    return;
                }

                // Toggle listening state
                if (!_isListening)
                {
                    // Start listening
                    statusText.Text = Loc.Get("Main_St_StartingRecognition");
                    
                    // Clear recognized text completely
                    lblRecognizedText.Text = "";
                    
                    // Activate microphone
                    bool micActivated = await Task.Run(() => ActivateMicrophone());
                    
                    if (micActivated)
                    {
                        _isListening = true;
                        _cancellationTokenSource = new CancellationTokenSource();
                        
                        // Reset text variables only after microphone is actually activated
                        _currentRecognizedText = "";
                        _lastProcessedText = "";
                        
                        // Reset voice command history at start of new session
                        ResetVoiceCommandHistory();
                        
                        // Update UI
                        if (sender != null) ((System.Windows.Controls.Button)sender).Content = "🛑"; // Stop icon
                        statusText.Text = Loc.Get("Main_WaitingForSpeech");
                        
                        // Add delay before starting text monitoring to prevent reading old text
                        _ = Task.Run(async () => {
                            await Task.Delay(2000); // Wait 2 seconds for microphone to fully activate
                            if (!_cancellationTokenSource.Token.IsCancellationRequested)
                            {
                                Dispatcher.Invoke(() => {
                                    statusText.Text = Loc.Get("Main_St_Listening");
                                });
                                await StartListeningAsync(_cancellationTokenSource.Token);
                            }
                        });
                    }
                    else
                    {
                        statusText.Text = Loc.Get("Main_St_MicEnableError");
                    }
                }
                else
                {
                    // Stop listening
                    statusText.Text = Loc.Get("Main_St_StoppingRecognition");
                    
                    // Cancel listening task
                    _cancellationTokenSource?.Cancel();
                    
                    // Deactivate microphone
                    bool micDeactivated = await Task.Run(() => DeactivateMicrophone());
                    
                    _isListening = false;
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = null;
                    
                    // Update UI
                    if (sender != null) ((System.Windows.Controls.Button)sender).Content = "🎤"; // Microphone icon
                    statusText.Text = Loc.Get("Main_St_RecognitionStopped");
                }
            }
            catch (Exception ex)
            {
                statusText.Text = Loc.Get("Main_St_ErrorPrefix") + ex.Message;
                
                // Reset state on error
                _isListening = false;
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                if (sender != null) ((System.Windows.Controls.Button)sender).Content = "🎤";
            }
            finally
            {
                if (sender != null) ((System.Windows.Controls.Button)sender).IsEnabled = true;
            }
        }

        private async Task StartListeningAsync(CancellationToken cancellationToken)
        {
            string lastRecognizedText = "";

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Get text from source textarea
                    string recognizedText = await GetRecognizedTextAsync();

                    // Only update UI if text has changed and is not empty
                    if (recognizedText != lastRecognizedText && !string.IsNullOrEmpty(recognizedText))
                    {
                        // Update UI with the full recognized text
                        Dispatcher.Invoke(() => {
                            lblRecognizedText.Text = recognizedText;
                        });



                        lastRecognizedText = recognizedText;
                    }

                    await Task.Delay(250, cancellationToken);
                }
                catch (Exception ex)
                {
                    // Log error and continue
                    Dispatcher.Invoke(() => {
                        statusText.Text = Loc.Get("Main_St_RecognitionErrorPrefix") + ex.Message;
                    });

                    await Task.Delay(1000, cancellationToken);
                }
            }
        }

        private async Task<string> GetRecognizedTextAsync()
        {
            try
            {
                // Execute JavaScript to get text from the textarea with improved selectors
                var jsResult = await Task.Run(() => {
                    try
                    {
                        return ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                            try {
                                // Method 1: Try the main source textarea with multiple possible aria-labels
                                var ariaLabels = [
                                    'متن منبع',
                                    'Source text',
                                    'متن برای ترجمه',
                                    'Text to translate'
                                ];
                                
                                for(var j=0; j < ariaLabels.length; j++) {
                                    var textArea = document.querySelector('textarea[aria-label=""' + ariaLabels[j] + '""]');
                                    if(textArea && textArea.value && textArea.value.trim()) {
                                        return textArea.value.trim();
                                    }
                                }
                                
                                // Method 2: Find textarea by class names commonly used in Google Translate
                                var classSelectors = [
                                    'textarea.er8xn',
                                    'textarea[jsname]',
                                    'textarea.gLFyf',
                                    'textarea[data-initial-value]'
                                ];
                                
                                for(var k=0; k < classSelectors.length; k++) {
                                    var elements = document.querySelectorAll(classSelectors[k]);
                                    for(var l=0; l < elements.length; l++) {
                                        if(elements[l].value && elements[l].value.trim()) {
                                            return elements[l].value.trim();
                                        }
                                    }
                                }
                                
                                // Method 3: Look for any textarea that contains text
                                var allTextareas = document.querySelectorAll('textarea');
                                for(var m=0; m < allTextareas.length; m++) {
                                    if(allTextareas[m].value && allTextareas[m].value.trim() && 
                                       allTextareas[m].offsetParent !== null) { // visible element
                                        return allTextareas[m].value.trim();
                                    }
                                }
                                
                                // Method 4: Check for Persian text in spans
                                var persianElements = document.querySelectorAll('span[lang=""fa""], [data-language-to-translate=""fa""] span, .source-text span');
                                for(var i=0; i < persianElements.length; i++) {
                                    if(persianElements[i].textContent && persianElements[i].textContent.trim() && 
                                       persianElements[i].offsetParent !== null) {
                                        return persianElements[i].textContent.trim();
                                    }
                                }
                                
                                return '';
                            } catch(e) {
                                console.error('Error in GetRecognizedTextAsync:', e);
                                return '';
                            }
                        ");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"JavaScript execution failed: {ex.Message}");
                        return null;
                    }
                });

                if (jsResult != null && !string.IsNullOrEmpty(jsResult.ToString()))
                    return jsResult.ToString();

                // Enhanced fallback to Selenium methods if JavaScript fails
                return await Task.Run(() => {
                    try
                    {
                        // Try multiple aria-label variations
                        string[] ariaLabels = { "متن منبع", "Source text", "متن برای ترجمه", "Text to translate" };
                        foreach (var label in ariaLabels)
                        {
                            try
                            {
                                var textElement = _driver.FindElement(By.CssSelector($"textarea[aria-label='{label}']"));
                                var value = textElement.GetAttribute("value");
                                if (!string.IsNullOrEmpty(value))
                                    return value;
                            }
                            catch { }
                        }
                        
                        // Try class-based selectors
                        string[] classSelectors = { "textarea.er8xn", "textarea[jsname]", "textarea.gLFyf" };
                        foreach (var selector in classSelectors)
                        {
                            try
                            {
                                var elements = _driver.FindElements(By.CssSelector(selector));
                                foreach (var element in elements)
                                {
                                    var value = element.GetAttribute("value");
                                    if (!string.IsNullOrEmpty(value))
                                        return value;
                                }
                            }
                            catch { }
                        }
                        
                        // Try Persian text spans
                        try
                        {
                            var persianElements = _driver.FindElements(By.CssSelector("span[lang='fa'], [data-language-to-translate='fa'] span"));
                            foreach (var element in persianElements)
                            {
                                var text = element.Text;
                                if (!string.IsNullOrEmpty(text))
                                    return text;
                            }
                        }
                        catch { }
                        
                        return string.Empty;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Selenium fallback failed: {ex.Message}");
                        return string.Empty;
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetRecognizedTextAsync failed: {ex.Message}");
                return string.Empty;
            }
        }

        #endregion

        #region UI Management

        // Dummy method to keep UI button working
        private void btnAddCommand_Click(object sender, RoutedEventArgs e)
        {
            // Dummy function to keep UI intact
            statusText.Text = Loc.Get("Main_St_VoiceCommandsDisabledVersion");
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settingsWindow = new SettingsWindow(_voiceCommandManager);
                if (settingsWindow.ShowDialog() == true)
                {
                    // Settings were saved, refresh our local copy
                    RefreshSettings();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("Main_OpenSettingsError", ex.Message), Loc.Get("Common_Error_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSelectEngine_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settingsWindow = new SettingsWindow(_voiceCommandManager);
                settingsWindow.SelectEngineTab();
                if (settingsWindow.ShowDialog() == true)
                    RefreshSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("Main_OpenSettingsError", ex.Message), Loc.Get("Common_Error_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Shows/hides the "helper Chromium browser" button depending on the selected engine.</summary>
        private void UpdateEngineUiState()
        {
            if (pnlHelperBrowser != null)
                pnlHelperBrowser.Visibility = (_settings?.SpeechEngine ?? "GoogleTranslate") == "GoogleTranslate"
                    ? System.Windows.Visibility.Visible
                    : System.Windows.Visibility.Collapsed;
        }

        /// <summary>Builds the Google Translate URL for the configured typing/dictation language.</summary>
        private string BuildGoogleTranslateUrl()
        {
            string lang = string.IsNullOrWhiteSpace(_settings?.TypingLanguage) ? "en" : _settings.TypingLanguage;
            string target = lang == "en" ? "fa" : "en";
            // hl follows the dictation language so the page UI (and its aria-labels) matches the
            // language being spoken — this keeps the microphone/stop selectors working (the mic
            // logic recognizes both the Persian and English labels).
            return $"https://translate.google.com/?hl={lang}&sl={lang}&tl={target}&op=translate";
        }

        private void RefreshSettings()
        {
            try
            {
                _settings = _settingsManager.LoadSettings();

                UpdateEngineUiState();

                // Update timer intervals with new settings
                _processingTimer.Interval = TimeSpan.FromMilliseconds(_settings.ProcessDelayMs);
                _inactivityTimer.Interval = TimeSpan.FromMilliseconds(_settings.InactivityDelayMs);
                
                // Re-register global shortcut with new settings
                if (_shortcutManager != null)
                {
                    _shortcutManager.UnregisterShortcuts();
                    _shortcutManager.Dispose();
                    _shortcutManager = new GlobalShortcutManager(this, _settings, OnGlobalShortcutPressed, OnStopShortcutPressed);
                    bool registered = _shortcutManager.RegisterShortcuts();
                    System.Diagnostics.Debug.WriteLine($"شورتکی سراسری مجدداً ثبت شد: {registered}");
                    System.Diagnostics.Debug.WriteLine($"تنظیمات جدید شورتکی: Ctrl={_settings.ShortcutCtrl}, Shift={_settings.ShortcutShift}, Key={_settings.ShortcutKey}");
                }
                
                // Update voice command system with new settings
                if (_voiceCommandManager != null)
                {
                    _voiceCommandManager.RefreshCommands();
                    if (_voiceCommandProcessor != null)
                    {
                        _voiceCommandProcessor = new VoiceCommandProcessor(_voiceCommandManager.ActiveCommands, _settings.CaseSensitiveCommands);
                        System.Diagnostics.Debug.WriteLine("سیستم دستورات صوتی با تنظیمات جدید به‌روزرسانی شد");
                    }
                }
                
                statusText.Text = Loc.Get("Main_St_SettingsUpdated");
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("Main_UpdateSettingsError", ex.Message), Loc.Get("Common_Error_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Context Menu Event Handlers

        private void CopyMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu)
                {
                    if (contextMenu.PlacementTarget is System.Windows.Controls.TextBox textBox && !string.IsNullOrEmpty(textBox.SelectedText))
                    {
                        System.Windows.Clipboard.SetText(textBox.SelectedText);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Copy failed: {ex.Message}");
            }
        }

        private void CutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu)
                {
                    if (contextMenu.PlacementTarget is System.Windows.Controls.TextBox textBox && !string.IsNullOrEmpty(textBox.SelectedText))
                    {
                        System.Windows.Clipboard.SetText(textBox.SelectedText);
                        textBox.SelectedText = string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cut failed: {ex.Message}");
            }
        }

        private void PasteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu)
                {
                    if (contextMenu.PlacementTarget is System.Windows.Controls.TextBox textBox && System.Windows.Clipboard.ContainsText())
                    {
                        string clipboardText = System.Windows.Clipboard.GetText();
                        int selectionStart = textBox.SelectionStart;
                        textBox.Text = textBox.Text.Remove(selectionStart, textBox.SelectionLength).Insert(selectionStart, clipboardText);
                        textBox.SelectionStart = selectionStart + clipboardText.Length;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Paste failed: {ex.Message}");
            }
        }

        private void SelectAllMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu)
                {
                    if (contextMenu.PlacementTarget is System.Windows.Controls.TextBox textBox)
                    {
                        textBox.SelectAll();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Select All failed: {ex.Message}");
            }
        }

        #endregion

        #region Global Shortcut and Status Indicator

        /// <summary>
        /// راه‌اندازی شورتکی سراسری
        /// </summary>
        private RoutedEventHandler _globalShortcutLoadedHandler;

        private void InitializeGlobalShortcut()
        {
            System.Diagnostics.Debug.WriteLine("InitializeGlobalShortcut فراخوانی شد");

            // Detach any previously-attached handler so repeated calls don't pile up duplicates
            if (_globalShortcutLoadedHandler != null)
            {
                this.Loaded -= _globalShortcutLoadedHandler;
            }

            _globalShortcutLoadedHandler = (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine("Loaded event فراخوانی شد");
                try
                {
                    _shortcutManager = new GlobalShortcutManager(this, _settings, OnGlobalShortcutPressed, OnStopShortcutPressed);
                    bool registered = _shortcutManager.RegisterShortcuts();
                    System.Diagnostics.Debug.WriteLine($"شورتکی‌های سراسری ثبت شدند: {registered}");
                    System.Diagnostics.Debug.WriteLine($"شورتکی‌های فعال: Ctrl+Alt+A (toggle), Ctrl+Alt+S (stop)");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"خطا در راه‌اندازی شورتکی سراسری: {ex.Message}");
                }
            };

            this.Loaded += _globalShortcutLoadedHandler;
        }

        /// <summary>
        /// راه‌اندازی چراغ وضعیت دسکتاپ
        /// </summary>
        private void InitializeStatusIndicator()
        {
            try
            {
                _statusIndicator = new DesktopStatusIndicator();
                _statusIndicator.Show();
                System.Diagnostics.Debug.WriteLine("چراغ وضعیت راه‌اندازی و نمایش داده شد");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"خطا در راه‌اندازی چراغ وضعیت: {ex.Message}");
            }
        }

        /// <summary>
        /// پردازش فشردن شورتکی سراسری Ctrl+Alt+A (toggle)
        /// </summary>
        private void OnGlobalShortcutPressed()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    System.Diagnostics.Debug.WriteLine($"شورتکی Ctrl+Alt+A فشرده شد - وضعیت مرورگر: {(_driver != null && _browserReady ? "باز" : "بسته")}, وضعیت میکروفون: {(_isListening ? "فعال" : "غیرفعال")}");
                    
                    // بررسی وضعیت مرورگر
                    if (_driver == null || !_browserReady)
                    {
                        // مرورگر بسته است، آن را باز کن و گوگل ترنسلیت را فعال کن
                        statusText.Text = Loc.Get("Main_St_OpeningAndActivatingGT");
                        btnStartMic_Click(btnStartMic, new RoutedEventArgs());
                        System.Diagnostics.Debug.WriteLine("مرورگر بسته بود، در حال باز کردن...");
                    }
                    else
                    {
                        // مرورگر باز است، بررسی وضعیت میکروفون
                        if (_isListening)
                        {
                            // میکروفون فعال است، آن را غیرفعال کن
                            statusText.Text = Loc.Get("Main_St_StoppingMic");
                            btnStopMic_Click(btnStopMic, new RoutedEventArgs());
                            System.Diagnostics.Debug.WriteLine("میکروفون فعال بود، در حال توقف...");
                        }
                        else
                        {
                            // میکروفون غیرفعال است، آن را فعال کن
                            statusText.Text = Loc.Get("Main_St_ActivatingMic2");
                            btnStartMic_Click(btnStartMic, new RoutedEventArgs());
                            System.Diagnostics.Debug.WriteLine("میکروفون غیرفعال بود، در حال فعال‌سازی...");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"خطا در پردازش شورتکی Ctrl+Alt+A: {ex.Message}");
            }
        }

        /// <summary>
        /// پردازش فشردن شورتکی سراسری Ctrl+Alt+S (stop)
        /// </summary>
        private void OnStopShortcutPressed()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    System.Diagnostics.Debug.WriteLine($"شورتکی Ctrl+Alt+S فشرده شد - وضعیت مرورگر: {(_driver != null && _browserReady ? "باز" : "بسته")}, وضعیت میکروفون: {(_isListening ? "فعال" : "غیرفعال")}");
                    
                    // اگر مرورگر باز است، دکمه Stop را فراخوانی کن (مشابه کلیک روی دکمه)
                    if (_driver != null)
                    {
                        btnStopMic_Click(btnStopMic, new RoutedEventArgs());
                        System.Diagnostics.Debug.WriteLine("دکمه Stop میکروفون از طریق شورتکی فراخوانی شد");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("مرورگر بسته است - نمی‌توان میکروفون را متوقف کرد");
                        statusText.Text = Loc.Get("Main_St_BrowserNotActive");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"خطا در پردازش شورتکی Ctrl+Alt+S: {ex.Message}");
            }
        }

        /// <summary>
        /// فعال‌سازی ویژگی‌های شورتکی سراسری
        /// </summary>
        private void ActivateGlobalShortcutFeatures()
        {
            try
            {
                // راه‌اندازی چراغ وضعیت در صورت عدم وجود
                if (_statusIndicator == null)
                {
                    InitializeStatusIndicator();
                }

                // فعال کردن انتقال لایو
                if (!_isLiveTransferActive)
                {
                    btnLiveTransfer.IsChecked = true;
                    BtnLiveTransfer_Click(btnLiveTransfer, new RoutedEventArgs());
                }

                // نمایش چراغ وضعیت
                _statusIndicator?.Show();

                // فعال کردن میکروفون
                if (!_isListening)
                {
                    btnStartMic_Click_Internal();
                }

                // به‌روزرسانی وضعیت چراغ بر اساس وضعیت واقعی میکروفون
                UpdateStatusIndicatorBasedOnMicrophone();

                statusText.Text = Loc.Get("Main_St_GlobalShortcutOnLive");
            }
            catch (Exception ex)
            {
                statusText.Text = Loc.Get("Main_St_ShortcutEnableError", ex.Message);
            }
        }

        /// <summary>
        /// غیرفعال‌سازی ویژگی‌های شورتکی سراسری
        /// </summary>
        private void DeactivateGlobalShortcutFeatures()
        {
            try
            {
                // غیرفعال کردن میکروفون
                if (_isListening)
                {
                    btnStopMic_Click(btnStopMic, new RoutedEventArgs());
                }

                // غیرفعال کردن انتقال لایو
                if (_isLiveTransferActive)
                {
                    btnLiveTransfer.IsChecked = false;
                    BtnLiveTransfer_Click(btnLiveTransfer, new RoutedEventArgs());
                }

                // مخفی کردن چراغ وضعیت
                _statusIndicator?.Hide();

                statusText.Text = Loc.Get("Main_St_GlobalShortcutOff");
            }
            catch (Exception ex)
            {
                statusText.Text = Loc.Get("Main_St_ShortcutDisableError", ex.Message);
            }
        }

        /// <summary>
        /// به‌روزرسانی وضعیت چراغ بر اساس حالت میکروفون
        /// </summary>
        private void UpdateStatusIndicatorBasedOnMicrophone()
        {
            if (_statusIndicator != null)
            {
                // استفاده از وضعیت واقعی میکروفون ویندوز به جای _isListening
                bool isMicActive = DesktopStatusIndicator.IsMicrophoneActiveInSystem();
                _statusIndicator.SetMicrophoneStatus(isMicActive);
            }
        }

        #endregion

        #region Voice Command System

        /// <summary>
        /// Initialize the voice command system
        /// </summary>
        private void InitializeVoiceCommandSystem()
        {
            try
            {
                _voiceCommandManager = new VoiceCommandManager(_settings);
                _systemCommandExecutor = new SystemCommandExecutor();
                _voiceCommandProcessor = new VoiceCommandProcessor(_voiceCommandManager.ActiveCommands ?? new List<VoiceCommand>(), false);
                
                // Subscribe to commands changed event to refresh processor
                _voiceCommandManager.CommandsChanged += OnVoiceCommandsChanged;
                
                statusText.Text = Loc.Get("Main_St_VoiceSysReady");
            }
            catch (Exception ex)
            {
                statusText.Text = Loc.Get("Main_St_VoiceSysInitError", ex.Message);
            }
        }

        /// <summary>
        /// Handle voice commands changed event
        /// </summary>
        private void OnVoiceCommandsChanged(object sender, EventArgs e)
        {
            try
            {
                // Refresh the voice command processor with updated commands
                _voiceCommandProcessor = new VoiceCommandProcessor(_voiceCommandManager.ActiveCommands ?? new List<VoiceCommand>(), false);
                statusText.Text = Loc.Get("Main_St_VoiceUpdated");
            }
            catch (Exception ex)
            {
                statusText.Text = Loc.Get("Main_St_VoiceUpdateError", ex.Message);
            }
        }

        /// <summary>
        /// Process voice commands from recognized text - DISABLED
        /// متد غیرفعال شده برای جلوگیری از اجرای چندگانه دستورات
        /// </summary>
        /// <param name="recognizedText">The recognized text to process</param>
        /// <returns>Text with commands removed</returns>
        private string ProcessVoiceCommands(string recognizedText)
        {
            // این متد غیرفعال شده - تمام پردازش دستورات در ProcessingTimer_Tick انجام می‌شود
            return recognizedText;
        }
        
        /// <summary>
        /// ریست کردن تاریخچه دستورات صوتی
        /// </summary>
        private void ResetVoiceCommandHistory()
        {
            try
            {
                _voiceCommandProcessor?.ResetWordHistory();
                statusText.Text = Loc.Get("Main_St_VoiceHistoryReset");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"خطا در ریست تاریخچه دستورات: {ex.Message}");
            }
        }

        /// <summary>
        /// متد کمکی برای ریست میکروفون بعد از تشخیص دستور
        /// </summary>
        private async Task ResetMicrophoneAfterCommand()
        {
            // ذخیره وضعیت فعلی انتقال متن قبل از ریست
            bool wasLiveTransferActive = false;
            bool wasProcessingTimerEnabled = false;
            bool wasInactivityTimerEnabled = false;
            
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    // ذخیره وضعیت فعلی
                    wasLiveTransferActive = _isLiveTransferActive;
                    wasProcessingTimerEnabled = _processingTimer.IsEnabled;
                    wasInactivityTimerEnabled = _inactivityTimer.IsEnabled;
                    
                    statusText.Text = Loc.Get("Main_St_StopTransferBeforeReset");
                });

                // متوقف کردن تایمرها و انتقال متن
                _processingTimer.Stop();
                _inactivityTimer.Stop();
                
                // اگر انتقال لایو فعال بود، موقتاً آن را غیرفعال کن
                if (wasLiveTransferActive)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _isLiveTransferActive = false;
                        btnLiveTransfer.IsChecked = false;
                    });
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    statusText.Text = Loc.Get("Main_St_MicResetAfterCmd");
                });

                // استفاده از همان روش InactivityTimer_Tick
                // Click the Stop button
                await Task.Run(() => btnStopMic_Click_Internal());

                // Wait for Google Translate to settle back to idle before restarting
                await Task.Delay(700);

                // Click the Start button
                await Task.Run(() => btnStartMic_Click_Internal());

                await Dispatcher.InvokeAsync(() =>
                {
                    statusText.Text = Loc.Get("Main_St_RestoringTransferState");
                });
                
                // بازگردانی وضعیت قبلی انتقال متن
                if (wasLiveTransferActive)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _isLiveTransferActive = true;
                        btnLiveTransfer.IsChecked = true;
                        
                        // بازگردانی تنظیمات انتقال لایو
                        lblFinalText.IsEnabled = false;
                        lblFinalText.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
                        
                        // به‌روزرسانی چراغ وضعیت
                        UpdateStatusIndicatorBasedOnMicrophone();
                    });
                }
                
                // راه‌اندازی مجدد تایمرها اگر قبلاً فعال بودند
                if (wasProcessingTimerEnabled)
                {
                    _processingTimer.Start();
                }
                if (wasInactivityTimerEnabled)
                {
                    _inactivityTimer.Start();
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    statusText.Text = Loc.Get("Main_St_MicResetAndRestored");
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    statusText.Text = Loc.Get("Main_St_MicResetErrorPrefix") + ex.Message;
                    
                    // در صورت خطا، سعی کن وضعیت قبلی را بازگردان
                    try
                    {
                        if (wasLiveTransferActive)
                        {
                            _isLiveTransferActive = true;
                            btnLiveTransfer.IsChecked = true;
                            lblFinalText.IsEnabled = false;
                            lblFinalText.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
                        }
                        
                        if (wasProcessingTimerEnabled)
                            _processingTimer.Start();
                        if (wasInactivityTimerEnabled)
                            _inactivityTimer.Start();
                    }
                    catch { }
                });
            }
        }

        /// <summary>
        /// متد کمکی برای اجرای دستور در انتظار
        /// </summary>
        private async Task ExecutePendingCommand()
        {
            try
            {
                if (_isCommandPending && _pendingCommand != null)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        statusText.Text = Loc.Get("Main_St_RunningCommandPrefix") + _pendingCommand.Phrase;
                    });

                    // اجرای دستور بر اساس نوع آن
                    await Task.Run(() =>
                    {
                        if (_pendingCommand.ActionType == CommandActionType.TypeText)
                        {
                            var replacedText = _voiceCommandProcessor?.ReplaceSingleWordCommand(_pendingCommand.Phrase);
                            // در اینجا متن جایگزین شده در متن اصلی قرار می‌گیرد
                        }
                        else if (_pendingCommand.ActionType == CommandActionType.SendKeys)
                        {
                            // اجرای دستور کلیدی
                            _systemCommandExecutor?.ExecuteKeyCommand(_pendingCommand.ActionValue);
                        }

                    });

                    // پاک کردن دستور از حافظه
                    _pendingCommand = null;
                    _isCommandPending = false;
                    _textBeforeCommand = string.Empty;

                    await Dispatcher.InvokeAsync(() =>
                    {
                        statusText.Text = Loc.Get("Main_St_CommandDoneReady");
                    });
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    statusText.Text = Loc.Get("Main_St_CommandErrorPrefix") + ex.Message;
                });
                
                // پاک کردن دستور در صورت خطا
                _pendingCommand = null;
                _isCommandPending = false;
                _textBeforeCommand = string.Empty;
            }
        }

        #endregion

        #region Cleanup

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (MessageBox.Show(Loc.Get("Main_ConfirmExit_Msg"), Loc.Get("Common_ConfirmExit_Title"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No)
            {
                e.Cancel = true;
                return;
            }

            CleanupAndClose();
        }

        private void CleanupAndClose()
        {
            try
            {
                // Stop the processing timer and detach handlers so queued Tick callbacks
                // can't fire on a half-disposed window.
                if (_processingTimer != null)
                {
                    _processingTimer.Stop();
                    _processingTimer.Tick -= ProcessingTimer_Tick;
                }
                if (_inactivityTimer != null)
                {
                    _inactivityTimer.Stop();
                    _inactivityTimer.Tick -= InactivityTimer_Tick;
                }

                // Cancel running tasks
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                // Close browser
                if (_driver != null)
                {
                    try
                    {
                        _driver.Quit();
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"خطا در Quit مرورگر: {ex.Message}"); }
                    _driver = null;
                }

                if (_preloadedDriver != null)
                {
                    try
                    {
                        _preloadedDriver.Quit();
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"خطا در Quit مرورگر پیش‌بارگذاری: {ex.Message}"); }
                    _preloadedDriver = null;
                }

                // Kill ChromeDriver processes and dispose their handles
                try
                {
                    foreach (var process in Process.GetProcessesByName("chromedriver"))
                    {
                        try
                        {
                            process.Kill();
                            process.WaitForExit(2000);
                        }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"خطا در Kill chromedriver: {ex.Message}"); }
                        finally { process.Dispose(); }
                    }

                    // Close Chrome instances opened by Selenium
                    foreach (var process in Process.GetProcessesByName("chrome"))
                    {
                        try
                        {
                            if (process.MainWindowTitle.Contains("data:,"))
                            {
                                try
                                {
                                    process.Kill();
                                    process.WaitForExit(2000);
                                }
                                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"خطا در Kill chrome: {ex.Message}"); }
                            }
                        }
                        finally { process.Dispose(); }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"خطا در پاک‌سازی پروسه‌های Chrome: {ex.Message}");
                }

                // پاک‌سازی شورتکی سراسری
                try
                {
                    _shortcutManager?.UnregisterShortcuts();
                    _shortcutManager?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"خطا در پاک‌سازی شورتکی سراسری: {ex.Message}");
                }

                // پاک‌سازی چراغ وضعیت
                try
                {
                    _statusIndicator?.Hide();
                    _statusIndicator?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"خطا در پاک‌سازی چراغ وضعیت: {ex.Message}");
                }

                // Clean up notify icon
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }

                // Force application shutdown
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"خطا در CleanupAndClose: {ex.Message}");
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        protected override void OnStateChanged(EventArgs e)
        {
            // Minimize to tray functionality based on settings
            if (WindowState == WindowState.Minimized && _settings.MinimizeToTray)
            {
                this.Hide();
                
                // Setup notify icon if not already done
                if (_notifyIcon == null)
                {
                    SetupNotifyIcon();
                }
            }

            base.OnStateChanged(e);
        }

        #endregion

        #region Hyperlink Event Handler

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
                {
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("Main_OpenLinkError", ex.Message), Loc.Get("Common_Error_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Opens the URL stored in a link-button's Tag (GitHub / website / author profile).</summary>
        private void OpenUrlButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement fe && fe.Tag is string url && !string.IsNullOrWhiteSpace(url))
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("Main_OpenLinkError", ex.Message), Loc.Get("Common_Error_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion
    }
}
