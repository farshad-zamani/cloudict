using System;
using System.Collections.Generic;
using Cloudict.Abstractions;

namespace Cloudict.Services
{
    /// <summary>A key press requested by a voice command, resolved to a key plus its modifiers.</summary>
    public readonly struct KeyCommand
    {
        public KeyCommand(InjectedKey key, KeyModifiers modifiers)
        {
            Key = key;
            Modifiers = modifiers;
        }

        public InjectedKey Key { get; }
        public KeyModifiers Modifiers { get; }

        public override string ToString() => new HotkeyBinding(Key, Modifiers).ToString();
    }

    /// <summary>
    /// Turns the text stored in a voice command's action value — <c>"Enter"</c>,
    /// <c>"{BACKSPACE}"</c>, <c>"Ctrl+Backspace"</c>, <c>"copy"</c>, <c>"اینتر"</c> — into a key and
    /// modifiers.
    ///
    /// <para>This parsing used to live inside the Windows-only <c>SystemCommandExecutor</c>, mixed
    /// in with the <c>user32</c> calls that performed the press. It is pure string handling with
    /// nothing platform-specific about it, so it belongs in Core where all three platforms share it
    /// — and where it can be unit-tested without a keyboard.</para>
    ///
    /// <para>Existing users' settings files contain these exact strings, so every spelling the old
    /// implementation accepted, including the Persian aliases, must keep working.</para>
    /// </summary>
    public static class KeyCommandParser
    {
        /// <summary>Names that map straight to a single key, including the Persian aliases users already have saved.</summary>
        private static readonly Dictionary<string, InjectedKey> SingleKeys =
            new Dictionary<string, InjectedKey>(StringComparer.OrdinalIgnoreCase)
            {
                ["enter"] = InjectedKey.Enter,
                ["return"] = InjectedKey.Enter,
                ["انتر"] = InjectedKey.Enter,
                ["اینتر"] = InjectedKey.Enter,

                ["tab"] = InjectedKey.Tab,
                ["تب"] = InjectedKey.Tab,

                ["space"] = InjectedKey.Space,
                ["spacebar"] = InjectedKey.Space,
                ["فاصله"] = InjectedKey.Space,
                ["اسپیس"] = InjectedKey.Space,

                ["backspace"] = InjectedKey.Backspace,
                ["back"] = InjectedKey.Backspace,
                ["بک اسپیس"] = InjectedKey.Backspace,
                ["پاک کردن"] = InjectedKey.Backspace,

                ["delete"] = InjectedKey.Delete,
                ["del"] = InjectedKey.Delete,
                ["حذف"] = InjectedKey.Delete,
                ["دلیت"] = InjectedKey.Delete,

                ["escape"] = InjectedKey.Escape,
                ["esc"] = InjectedKey.Escape,
                ["اسکیپ"] = InjectedKey.Escape,
                ["خروج"] = InjectedKey.Escape,

                ["insert"] = InjectedKey.Insert,
                ["home"] = InjectedKey.Home,
                ["end"] = InjectedKey.End,

                ["pageup"] = InjectedKey.PageUp,
                ["pgup"] = InjectedKey.PageUp,
                ["pagedown"] = InjectedKey.PageDown,
                ["pgdn"] = InjectedKey.PageDown,

                ["up"] = InjectedKey.Up,
                ["uparrow"] = InjectedKey.Up,
                ["down"] = InjectedKey.Down,
                ["downarrow"] = InjectedKey.Down,
                ["left"] = InjectedKey.Left,
                ["leftarrow"] = InjectedKey.Left,
                ["right"] = InjectedKey.Right,
                ["rightarrow"] = InjectedKey.Right,
            };

        /// <summary>Editing shortcuts that have a name of their own as well as a key combination.</summary>
        private static readonly Dictionary<string, KeyCommand> NamedChords =
            new Dictionary<string, KeyCommand>(StringComparer.OrdinalIgnoreCase)
            {
                ["copy"] = new KeyCommand(InjectedKey.C, KeyModifiers.Control),
                ["paste"] = new KeyCommand(InjectedKey.V, KeyModifiers.Control),
                ["cut"] = new KeyCommand(InjectedKey.X, KeyModifiers.Control),
                ["undo"] = new KeyCommand(InjectedKey.Z, KeyModifiers.Control),
                ["redo"] = new KeyCommand(InjectedKey.Y, KeyModifiers.Control),
                ["selectall"] = new KeyCommand(InjectedKey.A, KeyModifiers.Control),
                ["save"] = new KeyCommand(InjectedKey.S, KeyModifiers.Control),
            };

        private static readonly Dictionary<string, KeyModifiers> Modifiers =
            new Dictionary<string, KeyModifiers>(StringComparer.OrdinalIgnoreCase)
            {
                ["ctrl"] = KeyModifiers.Control,
                ["control"] = KeyModifiers.Control,
                ["alt"] = KeyModifiers.Alt,
                ["shift"] = KeyModifiers.Shift,
                ["win"] = KeyModifiers.Meta,
                ["cmd"] = KeyModifiers.Meta,
                ["command"] = KeyModifiers.Meta,
                ["super"] = KeyModifiers.Meta,
                ["meta"] = KeyModifiers.Meta,
            };

        /// <summary>
        /// Parses a stored action value. Returns false when nothing recognizable is found, which the
        /// caller should treat as "do nothing" rather than an error — the value is user-editable.
        /// </summary>
        public static bool TryParse(string command, out KeyCommand result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(command)) return false;

            var normalized = command.Trim();

            // SendKeys-style braces, e.g. "{ENTER}".
            if (normalized.Length > 2 && normalized[0] == '{' && normalized[normalized.Length - 1] == '}')
                normalized = normalized.Substring(1, normalized.Length - 2).Trim();

            if (normalized.Length == 0) return false;

            if (NamedChords.TryGetValue(normalized, out var chord))
            {
                result = chord;
                return true;
            }

            if (SingleKeys.TryGetValue(normalized, out var single))
            {
                result = new KeyCommand(single, KeyModifiers.None);
                return true;
            }

            if (normalized.Contains('+'))
                return TryParseCombination(normalized, out result);

            var parsed = KeyNames.Parse(normalized);
            if (parsed != InjectedKey.None)
            {
                result = new KeyCommand(parsed, KeyModifiers.None);
                return true;
            }

            return false;
        }

        /// <summary>Handles "Ctrl+Backspace", "Ctrl+Shift+S", "Alt+F4" and similar.</summary>
        private static bool TryParseCombination(string command, out KeyCommand result)
        {
            result = default;

            var parts = command.Split('+');
            if (parts.Length < 2) return false;

            var modifiers = KeyModifiers.None;

            // Everything before the final segment must be a modifier; the last segment is the key.
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (!Modifiers.TryGetValue(parts[i].Trim(), out var m)) return false;
                modifiers |= m;
            }

            var keyText = parts[parts.Length - 1].Trim();

            InjectedKey key;
            if (SingleKeys.TryGetValue(keyText, out var named)) key = named;
            else key = KeyNames.Parse(keyText);

            if (key == InjectedKey.None) return false;

            result = new KeyCommand(key, modifiers);
            return true;
        }
    }
}
