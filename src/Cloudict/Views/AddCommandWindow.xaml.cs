using System;
using System.Windows;
using System.Windows.Controls;

namespace Cloudict
{
    public partial class AddCommandWindow : GlassWindow
    {
        public VoiceCommand Command { get; private set; }
        private VoiceCommand _editingCommand;

        public AddCommandWindow()
        {
            InitializeComponent();
            cmbActionType.SelectedIndex = 0;
            cmbActionType.SelectionChanged += CmbActionType_SelectionChanged;
            UpdateHelpText();
        }

        public AddCommandWindow(VoiceCommand commandToEdit) : this()
        {
            _editingCommand = commandToEdit;
            if (commandToEdit != null)
            {
                txtCommandPhrase.Text = commandToEdit.Phrase;
                txtActionValue.Text = commandToEdit.ActionValue;
                
                // تنظیم نوع عملیات
                switch (commandToEdit.ActionType)
                {
                    case CommandActionType.TypeText:
                        cmbActionType.SelectedIndex = 0;
                        break;
                    case CommandActionType.SendKeys:
                        cmbActionType.SelectedIndex = 1;
                        break;

                    case CommandActionType.ChangeToFarsi:
                        cmbActionType.SelectedIndex = 3;
                        break;
                    case CommandActionType.ChangeToEnglish:
                        cmbActionType.SelectedIndex = 4;
                        break;

                }
            }
        }

        private void CmbActionType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateHelpText();
        }

        private void UpdateHelpText()
        {
            if (cmbActionType.SelectedIndex == 1) // ارسال کلیدهای میانبر
            {
                // اضافه کردن دکمه‌های کلیدهای رایج
                AddCommonKeyButtons();
            }
        }

        private void AddCommonKeyButtons()
        {
            // نمایش دکمه‌های کلیدهای رایج
            if (pnlCommonKeys != null)
                pnlCommonKeys.Visibility = Visibility.Visible;
            if (pnlCommonKeys2 != null)
                pnlCommonKeys2.Visibility = Visibility.Visible;
        }

        private void CommonKey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag != null)
            {
                string keyValue = button.Tag.ToString();
                // Convert simple key names to proper format
                switch (keyValue)
                {
                    case "ENTER":
                        txtActionValue.Text = "{ENTER}";
                        break;
                    case "TAB":
                        txtActionValue.Text = "{TAB}";
                        break;
                    case "ESC":
                        txtActionValue.Text = "{ESC}";
                        break;
                    case "BACKSPACE":
                        txtActionValue.Text = "{BACKSPACE}";
                        break;
                    case "DELETE":
                        txtActionValue.Text = "{DELETE}";
                        break;
                    case "F1":
                        txtActionValue.Text = "{F1}";
                        break;
                    case "F5":
                        txtActionValue.Text = "{F5}";
                        break;
                    default:
                        txtActionValue.Text = keyValue;
                        break;
                }
            }
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            // اعتبارسنجی ورودی‌ها
            if (string.IsNullOrWhiteSpace(txtCommandPhrase.Text))
            {
                MessageBox.Show(Loc.Get("AddCmd_EnterPhrase"), Loc.Get("Common_Error_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(txtActionValue.Text))
            {
                MessageBox.Show(Loc.Get("AddCmd_EnterValue"), Loc.Get("Common_Error_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // تعیین نوع عملیات بر اساس انتخاب کاربر
            CommandActionType actionType;
            switch (cmbActionType.SelectedIndex)
            {
                case 0:
                    actionType = CommandActionType.TypeText;
                    break;
                case 1:
                    actionType = CommandActionType.SendKeys;
                    break;
                case 2:
                    actionType = CommandActionType.SendKeys;
                    break;
                case 3:
                    actionType = CommandActionType.ChangeToFarsi;
                    break;
                case 4:
                    actionType = CommandActionType.ChangeToEnglish;
                    break;

                default:
                    actionType = CommandActionType.TypeText;
                    break;
            }

            // ایجاد دستور جدید
            Command = new VoiceCommand(txtCommandPhrase.Text.Trim(), actionType, txtActionValue.Text.Trim());
            DialogResult = true;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}