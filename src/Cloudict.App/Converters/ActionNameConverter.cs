using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Cloudict.Services;

namespace Cloudict.App.Converters
{
    /// <summary>
    /// Shows a voice command's action in the user's language.
    ///
    /// <para>The model carries a <c>Action</c> property with these names hard-coded in Persian, from
    /// the days when that was the only interface language. Binding the raw enum instead showed
    /// "TypeText" and "SendKeys" to everyone. Neither is right, and the model has no business
    /// knowing about languages, so the translation happens here.</para>
    /// </summary>
    public sealed class ActionNameConverter : IValueConverter
    {
        public static readonly ActionNameConverter Instance = new ActionNameConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is CommandActionType type ? Describe(type) : string.Empty;

        public static string Describe(CommandActionType type) => type switch
        {
            CommandActionType.TypeText => Loc.Get("Common_TypeText"),
            CommandActionType.SendKeys => Loc.Get("Common_SendKey"),
            CommandActionType.ChangeToFarsi => Loc.Get("Common_ChangeToPersian"),
            CommandActionType.ChangeToEnglish => Loc.Get("Common_ChangeToEnglish"),
            _ => type.ToString()
        };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
