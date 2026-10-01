namespace Cloudict.Abstractions
{
    /// <summary>
    /// What a probe of the focused text field found immediately before the caret.
    /// </summary>
    public readonly struct CaretProbe
    {
        private CaretProbe(bool known, char? before)
        {
            IsKnown = known;
            CharBefore = before;
        }

        /// <summary>False when the field could not be asked — the application does not expose its
        /// text, the probe timed out, or the platform has no way to ask at all.</summary>
        public bool IsKnown { get; }

        /// <summary>The character just before the caret (or before the selection, which typing
        /// replaces). Null with <see cref="IsKnown"/> true means the caret is at the very start.</summary>
        public char? CharBefore { get; }

        public static CaretProbe Unknown => new CaretProbe(false, null);
        public static CaretProbe AtStart => new CaretProbe(true, null);
        public static CaretProbe After(char c) => new CaretProbe(true, c);
    }

    /// <summary>
    /// Read-only view of where typed text is about to land in another application.
    ///
    /// <para>Used for one decision only: whether the first word of a burst of dictation needs a
    /// space in front of it. Nothing here moves the caret, changes the selection, touches the
    /// clipboard or sends a key — a probe that could disturb the user's document would be worse
    /// than the missing space it is meant to prevent.</para>
    /// </summary>
    public interface ICaretContext
    {
        /// <summary>Asks the focused field what precedes the caret. Never throws, and returns
        /// <see cref="CaretProbe.Unknown"/> rather than waiting long.</summary>
        CaretProbe ProbeCharBeforeCaret();

        /// <summary>An identity for the window keyboard input is going to, or 0 when the platform
        /// cannot tell. Used to recognise that dictation is continuing in the same place.</summary>
        long ForegroundWindowId();
    }
}
