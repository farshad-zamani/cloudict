namespace Cloudict.Speech
{
    /// <summary>
    /// The buffered destination for dictated words — the "Final text" box the user can edit.
    ///
    /// <para>Live transfer types straight into whatever application has focus and needs nothing from
    /// the UI. Buffered mode is different: words are inserted <em>at the caret</em>, so the user can
    /// click somewhere mid-text and have the next word land there. That behaviour depends on caret
    /// state the view owns, so the session asks for it through this interface rather than keeping a
    /// private copy that would drift the moment the user typed anything themselves.</para>
    /// </summary>
    public interface IDictationOutput
    {
        /// <summary>The full contents of the final-text box.</summary>
        string FinalText { get; set; }

        /// <summary>The caret position within <see cref="FinalText"/>.</summary>
        int CaretIndex { get; set; }

        /// <summary>Gives the final-text box focus, so the caret is visible where the text landed.</summary>
        void FocusFinalText();
    }
}
