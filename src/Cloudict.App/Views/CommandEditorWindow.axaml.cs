using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Cloudict.Abstractions;
using Cloudict.App.Services;
using Cloudict.Services;

namespace Cloudict.App.Views
{
    /// <summary>
    /// Creates and edits one voice command.
    ///
    /// <para>Its WPF predecessor did not survive the move to Avalonia, and the gap was not obvious
    /// from the settings window: the commands tab still listed commands and still deleted them, so
    /// only someone trying to <em>define</em> one found that "Add" produced a row named "new command"
    /// with no way to fill it in — the grid is read-only.</para>
    ///
    /// <para>The key for a "send key" command is captured by pressing it. The help text inherited
    /// from 2.x tells people to write <c>^c</c> or <c>%{F4}</c>, a SendKeys dialect
    /// <see cref="KeyCommandParser"/> does not read, so a command written that way would have been
    /// accepted and then silently done nothing.</para>
    /// </summary>
    public partial class CommandEditorWindow : Window
    {
        private static readonly CommandActionType[] ActionOrder =
        {
            CommandActionType.TypeText,
            CommandActionType.SendKeys,
            CommandActionType.ChangeToFarsi,
            CommandActionType.ChangeToEnglish
        };

        private readonly VoiceCommand _original;
        private string _keyValue = string.Empty;
        private bool _capturing;

        /// <summary>The command as edited, or null when the user cancelled.</summary>
        public VoiceCommand Result { get; private set; }

        /// <param name="existing">Null to create a new command, otherwise the one being edited.</param>
        public CommandEditorWindow(VoiceCommand existing = null)
        {
            InitializeComponent();

            WindowSizing.FitToWorkArea(this, 720, 620);

            _original = existing;

            Title = Loc.Get(existing == null ? "AddCmd_Title" : "AddCmd_EditTitle");
            TxtHeader.Text = Loc.Get(existing == null ? "AddCmd_Header" : "AddCmd_EditHeader");

            CmbActionType.ItemsSource = ActionOrder.Select(DescribeAction).ToList();

            // Tunnelling, because a captured key must be seen before the focused control acts on it:
            // Space would press the focused button, Tab would move focus, Escape would close the
            // window, and none of those could then be assigned to a command.
            AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

            if (existing != null) LoadFrom(existing);
            else
            {
                CmbActionType.SelectedIndex = 0;
                ShowValueEditorFor(CommandActionType.TypeText);
            }
        }

        private static string DescribeAction(CommandActionType type) =>
            Converters.ActionNameConverter.Describe(type);

        private void LoadFrom(VoiceCommand command)
        {
            TxtPhrase.Text = command.Phrase;

            var index = Array.IndexOf(ActionOrder, command.ActionType);
            CmbActionType.SelectedIndex = index >= 0 ? index : 0;

            if (command.ActionType == CommandActionType.SendKeys)
            {
                // Show what is stored even if it is one of the older spellings, so an existing
                // command is never silently rewritten just by being opened.
                _keyValue = command.ActionValue ?? string.Empty;
                TxtKeyValue.Text = _keyValue;
            }
            else
            {
                TxtValue.Text = command.ActionValue;
            }

            ShowValueEditorFor(command.ActionType);
        }

        private CommandActionType SelectedAction =>
            CmbActionType.SelectedIndex >= 0 && CmbActionType.SelectedIndex < ActionOrder.Length
                ? ActionOrder[CmbActionType.SelectedIndex]
                : CommandActionType.TypeText;

        private void OnActionTypeChanged(object sender, SelectionChangedEventArgs e) =>
            ShowValueEditorFor(SelectedAction);

        /// <summary>Only the two actions that carry a value show one; the layout switches lie about the rest.</summary>
        private void ShowValueEditorFor(CommandActionType type)
        {
            if (PanelText == null || PanelKey == null) return;

            PanelText.IsVisible = type == CommandActionType.TypeText;
            PanelKey.IsVisible = type == CommandActionType.SendKeys;

            if (type == CommandActionType.SendKeys && string.IsNullOrEmpty(TxtKeyValue.Text))
                TxtKeyValue.Text = Loc.Get("AddCmd_NoKeyChosen");

            HideError();
        }

        #region Key capture

        private void OnCaptureKeyClick(object sender, RoutedEventArgs e)
        {
            _capturing = true;
            TxtKeyValue.Text = Loc.Get("KeySel_Prompt");
            HideError();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_capturing) return;

            // A modifier on its own is the user still assembling the combination.
            if (IsModifier(e.Key)) return;

            e.Handled = true;

            var key = ToInjectedKey(e.Key);

            DiagnosticLog.Write("CommandEditor",
                $"captured {e.Key} (+{e.KeyModifiers}) -> {key}");

            if (key == InjectedKey.None)
            {
                TxtKeyValue.Text = _keyValue.Length > 0 ? _keyValue : Loc.Get("AddCmd_NoKeyChosen");
                ShowError("AddCmd_KeyNotRecognized");
                _capturing = false;
                return;
            }

            var modifiers = Cloudict.Abstractions.KeyModifiers.None;
            if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control)) modifiers |= Cloudict.Abstractions.KeyModifiers.Control;
            if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt)) modifiers |= Cloudict.Abstractions.KeyModifiers.Alt;
            if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift)) modifiers |= Cloudict.Abstractions.KeyModifiers.Shift;
            if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta)) modifiers |= Cloudict.Abstractions.KeyModifiers.Meta;

            _keyValue = new HotkeyBinding(key, modifiers).ToString();
            TxtKeyValue.Text = _keyValue;
            _capturing = false;

            HideError();
        }

        private static bool IsModifier(Key key) => key is
            Key.LeftCtrl or Key.RightCtrl or
            Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or
            Key.LWin or Key.RWin;

        /// <summary>
        /// Maps a pressed key onto the set Cloudict can actually inject. Written out rather than
        /// matched by name: Avalonia calls backspace <c>Back</c> and enter <c>Return</c>, so name
        /// matching would quietly drop two of the most useful keys a command can send.
        /// </summary>
        private static InjectedKey ToInjectedKey(Key key)
        {
            switch (key)
            {
                case Key.Return or Key.Enter: return InjectedKey.Enter;
                case Key.Tab: return InjectedKey.Tab;
                case Key.Space: return InjectedKey.Space;
                case Key.Back: return InjectedKey.Backspace;
                case Key.Delete: return InjectedKey.Delete;
                case Key.Escape: return InjectedKey.Escape;
                case Key.Insert: return InjectedKey.Insert;
                case Key.Home: return InjectedKey.Home;
                case Key.End: return InjectedKey.End;
                case Key.PageUp: return InjectedKey.PageUp;
                case Key.PageDown: return InjectedKey.PageDown;
                case Key.Up: return InjectedKey.Up;
                case Key.Down: return InjectedKey.Down;
                case Key.Left: return InjectedKey.Left;
                case Key.Right: return InjectedKey.Right;
            }

            if (key >= Key.A && key <= Key.Z) return InjectedKey.A + (key - Key.A);
            if (key >= Key.D0 && key <= Key.D9) return InjectedKey.D0 + (key - Key.D0);
            if (key >= Key.NumPad0 && key <= Key.NumPad9) return InjectedKey.D0 + (key - Key.NumPad0);
            if (key >= Key.F1 && key <= Key.F12) return InjectedKey.F1 + (key - Key.F1);

            return InjectedKey.None;
        }

        #endregion

        #region Save

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            var phrase = TxtPhrase.Text?.Trim();
            if (string.IsNullOrWhiteSpace(phrase))
            {
                ShowError("AddCmd_EnterPhrase");
                TxtPhrase.Focus();
                return;
            }

            var action = SelectedAction;
            var value = string.Empty;

            if (action == CommandActionType.TypeText)
            {
                // Not trimmed: a command whose whole job is to type a space, or a comma followed by
                // one, must keep exactly what was entered.
                value = TxtValue.Text ?? string.Empty;
                if (value.Length == 0)
                {
                    ShowError("AddCmd_EnterValue");
                    TxtValue.Focus();
                    return;
                }
            }
            else if (action == CommandActionType.SendKeys)
            {
                value = _keyValue?.Trim() ?? string.Empty;
                if (value.Length == 0 || !KeyCommandParser.TryParse(value, out _))
                {
                    ShowError("AddCmd_EnterValue");
                    return;
                }
            }

            var result = _original ?? new VoiceCommand();
            result.Phrase = phrase;
            result.ActionType = action;
            result.ActionValue = value;
            result.UpdatedAt = DateTime.Now;

            Result = result;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

        private void ShowError(string key)
        {
            TxtError.Text = Loc.Get(key);
            TxtError.IsVisible = true;
        }

        private void HideError() => TxtError.IsVisible = false;

        #endregion
    }
}
