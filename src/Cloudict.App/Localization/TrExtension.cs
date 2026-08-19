using System;
using Avalonia.Markup.Xaml;

namespace Cloudict.App.Localization
{
    /// <summary>
    /// Resolves a localized string in XAML: <c>Text="{loc:Tr Main_Ready}"</c>.
    ///
    /// <para>WPF used <c>{DynamicResource}</c> against a ResourceDictionary of strings. Avalonia can
    /// do something similar, but routing through the same <see cref="Loc"/> API the code-behind and
    /// Core already use keeps one lookup path and one fallback rule for the whole application —
    /// rather than XAML and C# disagreeing about what a missing key should render as.</para>
    ///
    /// <para>The value is resolved once when the view loads. Changing language requires an
    /// application restart, exactly as it did before.</para>
    /// </summary>
    public sealed class TrExtension : MarkupExtension
    {
        public TrExtension() { }

        public TrExtension(string key) => Key = key;

        /// <summary>The localization key to resolve.</summary>
        public string Key { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider) =>
            string.IsNullOrWhiteSpace(Key) ? string.Empty : Loc.Get(Key);
    }
}
