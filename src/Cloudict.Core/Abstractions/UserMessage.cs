using System;

namespace Cloudict.Abstractions
{
    public enum UserMessageSeverity { Information, Warning, Error }

    /// <summary>
    /// Something the user should be told about, raised by code that has no way to show it.
    ///
    /// <para>Core carries the localization <em>key</em> and its arguments, never a finished
    /// sentence: it has no access to the resource dictionaries, and deciding how a message appears
    /// — dialog, status bar, tray balloon — belongs to the UI. This is what allowed
    /// <see cref="Services.SettingsManager"/> to stop calling <c>MessageBox.Show</c> directly, which
    /// was the last thing tying it to WPF.</para>
    /// </summary>
    public sealed class UserMessageEventArgs : EventArgs
    {
        public UserMessageEventArgs(string messageKey, string titleKey, UserMessageSeverity severity, params object[] args)
        {
            MessageKey = messageKey;
            TitleKey = titleKey;
            Severity = severity;
            Args = args ?? Array.Empty<object>();
        }

        /// <summary>Resource key of the message body.</summary>
        public string MessageKey { get; }

        /// <summary>Resource key of the dialog title.</summary>
        public string TitleKey { get; }

        public UserMessageSeverity Severity { get; }

        /// <summary>Format arguments for the message body.</summary>
        public object[] Args { get; }
    }

    /// <summary>Implemented by Core services that need to reach the user.</summary>
    public interface IUserMessageSource
    {
        event EventHandler<UserMessageEventArgs> UserMessage;
    }
}
