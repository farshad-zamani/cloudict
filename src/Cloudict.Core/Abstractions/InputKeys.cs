using System;

namespace Cloudict.Abstractions
{
    /// <summary>
    /// The keys Cloudict can press on the user's behalf, named independently of any operating
    /// system. Windows virtual-key codes, X11 keysyms and macOS virtual keycodes are all different
    /// numbering schemes, so the platform layer translates this enum into whatever the host uses.
    ///
    /// <para>This deliberately covers only what voice commands and text transfer actually need —
    /// it is not meant to describe every key on a keyboard.</para>
    /// </summary>
    public enum InjectedKey
    {
        None = 0,

        Enter, Tab, Space, Backspace, Delete, Escape, Insert,
        Home, End, PageUp, PageDown,
        Up, Down, Left, Right,

        F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,

        A, B, C, D, E, F, G, H, I, J, K, L, M,
        N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

        D0, D1, D2, D3, D4, D5, D6, D7, D8, D9
    }

    /// <summary>Modifier keys held while another key is pressed.</summary>
    [Flags]
    public enum KeyModifiers
    {
        None = 0,
        Control = 1,
        Alt = 2,
        Shift = 4,

        /// <summary>Windows key / Linux Super / macOS Command.</summary>
        Meta = 8
    }

    /// <summary>A global shortcut: one key plus the modifiers that must be held with it.</summary>
    public sealed class HotkeyBinding
    {
        public HotkeyBinding(InjectedKey key, KeyModifiers modifiers)
        {
            Key = key;
            Modifiers = modifiers;
        }

        public InjectedKey Key { get; }
        public KeyModifiers Modifiers { get; }

        /// <summary>True when this binding could actually be registered as a global shortcut.</summary>
        public bool IsValid => Key != InjectedKey.None && Modifiers != KeyModifiers.None;

        public override string ToString()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (Modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Meta");
            parts.Add(KeyNames.ToDisplayName(Key));
            return string.Join("+", parts);
        }
    }

    /// <summary>
    /// Converts between <see cref="InjectedKey"/> and the short text names used in settings files
    /// and voice-command definitions ("Enter", "F5", "A"). Settings written by older Windows-only
    /// builds use exactly these names, so parsing must stay compatible.
    /// </summary>
    public static class KeyNames
    {
        /// <summary>Parses a stored key name. Returns <see cref="InjectedKey.None"/> when unrecognized.</summary>
        public static InjectedKey Parse(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return InjectedKey.None;

            switch (name.Trim().ToUpperInvariant())
            {
                case "ENTER":
                case "RETURN": return InjectedKey.Enter;
                case "TAB": return InjectedKey.Tab;
                case "SPACE":
                case "SPACEBAR": return InjectedKey.Space;
                case "BACKSPACE":
                case "BACK": return InjectedKey.Backspace;
                case "DELETE":
                case "DEL": return InjectedKey.Delete;
                case "ESC":
                case "ESCAPE": return InjectedKey.Escape;
                case "INSERT": return InjectedKey.Insert;
                case "HOME": return InjectedKey.Home;
                case "END": return InjectedKey.End;
                case "PAGEUP": return InjectedKey.PageUp;
                case "PAGEDOWN": return InjectedKey.PageDown;
                case "UP": return InjectedKey.Up;
                case "DOWN": return InjectedKey.Down;
                case "LEFT": return InjectedKey.Left;
                case "RIGHT": return InjectedKey.Right;
            }

            var upper = name.Trim().ToUpperInvariant();

            if (upper.Length == 1 && upper[0] >= 'A' && upper[0] <= 'Z')
                return InjectedKey.A + (upper[0] - 'A');

            if (upper.Length == 1 && upper[0] >= '0' && upper[0] <= '9')
                return InjectedKey.D0 + (upper[0] - '0');

            if (upper.Length > 1 && upper[0] == 'F' &&
                int.TryParse(upper.Substring(1), out int fn) && fn >= 1 && fn <= 12)
                return InjectedKey.F1 + (fn - 1);

            return InjectedKey.None;
        }

        /// <summary>The name used when writing a key back to settings, and when showing it to the user.</summary>
        public static string ToDisplayName(InjectedKey key)
        {
            if (key >= InjectedKey.A && key <= InjectedKey.Z)
                return ((char)('A' + (key - InjectedKey.A))).ToString();

            if (key >= InjectedKey.D0 && key <= InjectedKey.D9)
                return ((char)('0' + (key - InjectedKey.D0))).ToString();

            if (key >= InjectedKey.F1 && key <= InjectedKey.F12)
                return "F" + (key - InjectedKey.F1 + 1);

            return key.ToString();
        }
    }
}
