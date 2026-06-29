using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace Cloudict
{
    public partial class KeySelectionWindow : GlassWindow
    {
        private List<Key> _pressedKeys = new List<Key>();
        
        public string SelectedKeys { get; private set; } = "";
        public new bool DialogResult { get; private set; } = false;
        
        public KeySelectionWindow()
        {
            InitializeComponent();
            
            // Focus the window so it can receive key events
            this.Loaded += (s, e) => {
                this.Focus();
                this.Activate();
            };
            
            // Ensure the window can receive keyboard input
            this.Focusable = true;
        }
        
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Handle special system keys
            if (e.Key == Key.Escape)
            {
                btnCancel_Click(null, null);
                return;
            }
            
            // Explicitly handle Enter key to prevent default behavior
            if (e.Key == Key.Enter)
            {
                // Add Enter key to the list like any other key
                if (!_pressedKeys.Contains(Key.Enter))
                {
                    _pressedKeys.Add(Key.Enter);
                }
                UpdateDisplayText();
                e.Handled = true;
                return;
            }
            
            // Handle system keys that might not be detected properly
            Key actualKey = e.Key;
            if (e.Key == Key.System)
            {
                // When Key.System is detected, check SystemKey for the actual key
                actualKey = e.SystemKey;
            }
            
            // Add the key if it's not already in the list
            if (!_pressedKeys.Contains(actualKey))
            {
                _pressedKeys.Add(actualKey);
            }
            
            UpdateDisplayText();
            e.Handled = true;
        }
        
        private void UpdateDisplayText()
        {
            if (_pressedKeys.Count == 0)
            {
                txtSelectedKeys.Text = "";
                return;
            }
            
            if (_pressedKeys.Count == 1)
            {
                // For single keys, show the key name
                txtSelectedKeys.Text = GetKeyDisplayName(_pressedKeys[0]);
            }
            else
            {
                // For combination keys, show them with + separator
                var keyNames = _pressedKeys.Select(GetKeyDisplayName).ToList();
                txtSelectedKeys.Text = string.Join("+", keyNames);
            }
        }
        
        private string GetKeyDisplayName(Key key)
        {
            switch (key)
            {
                // Modifier keys
                case Key.LeftCtrl:
                case Key.RightCtrl:
                    return "Ctrl";
                case Key.LeftAlt:
                case Key.RightAlt:
                    return "Alt";
                case Key.LeftShift:
                case Key.RightShift:
                    return "Shift";
                case Key.LWin:
                case Key.RWin:
                    return "Win";
                    
                // Special keys
                case Key.Space:
                    return "Space";
                case Key.Tab:
                    return "Tab";
                case Key.Enter:
                    return "Enter";
                case Key.Back:
                    return "Backspace";
                case Key.Delete:
                    return "Delete";
                case Key.Escape:
                    return "Escape";
                case Key.Insert:
                    return "Insert";
                    
                // Navigation keys
                case Key.Home:
                    return "Home";
                case Key.End:
                    return "End";
                case Key.PageUp:
                    return "PageUp";
                case Key.PageDown:
                    return "PageDown";
                case Key.Up:
                    return "Up";
                case Key.Down:
                    return "Down";
                case Key.Left:
                    return "Left";
                case Key.Right:
                    return "Right";
                    
                // Function keys
                case Key.F1:
                case Key.F2:
                case Key.F3:
                case Key.F4:
                case Key.F5:
                case Key.F6:
                case Key.F7:
                case Key.F8:
                case Key.F9:
                case Key.F10:
                case Key.F11:
                case Key.F12:
                    return key.ToString();
                    
                // Number pad keys
                case Key.NumPad0:
                case Key.NumPad1:
                case Key.NumPad2:
                case Key.NumPad3:
                case Key.NumPad4:
                case Key.NumPad5:
                case Key.NumPad6:
                case Key.NumPad7:
                case Key.NumPad8:
                case Key.NumPad9:
                    return "NumPad" + key.ToString().Substring(6);
                case Key.Multiply:
                    return "NumPad*";
                case Key.Add:
                    return "NumPad+";
                case Key.Subtract:
                    return "NumPad-";
                case Key.Divide:
                    return "NumPad/";
                case Key.Decimal:
                    return "NumPad.";
                    
                // Other special keys
                case Key.CapsLock:
                    return "CapsLock";
                case Key.NumLock:
                    return "NumLock";
                    
                default:
                    return key.ToString();
            }
        }
        
        private void btnClear_Click(object sender, RoutedEventArgs e)
        {
            _pressedKeys.Clear();
            txtSelectedKeys.Text = "";
        }
        
        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            if (_pressedKeys.Count == 0)
            {
                MessageBox.Show(Loc.Get("KeySel_SelectAtLeastOne"), Loc.Get("Common_Error_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            if (_pressedKeys.Count == 1)
            {
                // For single keys, use the format expected by SendKeys
                SelectedKeys = FormatForSendKeys(_pressedKeys[0]);
            }
            else
            {
                // For combination keys, format as Ctrl+Alt+Key
                var keyNames = _pressedKeys.Select(GetKeyDisplayName).ToList();
                SelectedKeys = string.Join("+", keyNames);
            }
            
            DialogResult = true;
            this.Close();
        }
        
        private string FormatForSendKeys(Key key)
        {
            switch (key)
            {
                // Special keys that need specific formatting for SendKeys
                case Key.Enter:
                    return "Enter";
                case Key.Tab:
                    return "Tab";
                case Key.Space:
                    return "Space";
                case Key.Back:
                    return "Backspace";
                case Key.Delete:
                    return "Delete";
                case Key.Escape:
                    return "Escape";
                case Key.Insert:
                    return "Insert";
                    
                // Navigation keys
                case Key.Home:
                    return "Home";
                case Key.End:
                    return "End";
                case Key.PageUp:
                    return "PageUp";
                case Key.PageDown:
                    return "PageDown";
                case Key.Up:
                    return "Up";
                case Key.Down:
                    return "Down";
                case Key.Left:
                    return "Left";
                case Key.Right:
                    return "Right";
                    
                // Function keys
                case Key.F1:
                case Key.F2:
                case Key.F3:
                case Key.F4:
                case Key.F5:
                case Key.F6:
                case Key.F7:
                case Key.F8:
                case Key.F9:
                case Key.F10:
                case Key.F11:
                case Key.F12:
                    return key.ToString();
                    
                // Modifier keys (for combination support)
                case Key.LeftCtrl:
                case Key.RightCtrl:
                    return "Ctrl";
                case Key.LeftAlt:
                case Key.RightAlt:
                    return "Alt";
                case Key.LeftShift:
                case Key.RightShift:
                    return "Shift";
                case Key.LWin:
                case Key.RWin:
                    return "Win";
                    
                // Number pad keys
                case Key.NumPad0:
                case Key.NumPad1:
                case Key.NumPad2:
                case Key.NumPad3:
                case Key.NumPad4:
                case Key.NumPad5:
                case Key.NumPad6:
                case Key.NumPad7:
                case Key.NumPad8:
                case Key.NumPad9:
                    return "NumPad" + key.ToString().Substring(6);
                case Key.Multiply:
                    return "NumPad*";
                case Key.Add:
                    return "NumPad+";
                case Key.Subtract:
                    return "NumPad-";
                case Key.Divide:
                    return "NumPad/";
                case Key.Decimal:
                    return "NumPad.";
                    
                // Other special keys
                case Key.CapsLock:
                    return "CapsLock";
                case Key.NumLock:
                    return "NumLock";
                    
                default:
                    // For regular keys, return as-is
                    return key.ToString();
            }
        }
        
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            this.Close();
        }
        
        private void txtSelectedKeys_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Prevent the TextBox from getting focus and keep focus on the Window
            // so that key events continue to be captured by Window_KeyDown
            e.Handled = true;
            this.Focus();
        }
    }
}