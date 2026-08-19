using Cloudict.Abstractions;
using Cloudict.Services;
using Xunit;

namespace Cloudict.Tests
{
    /// <summary>
    /// The action values these parse come straight out of users' existing settings files, written by
    /// Windows-only 2.x builds. Every spelling the old <c>SystemCommandExecutor</c> accepted has to
    /// keep working after the move to Core, or upgrading silently breaks people's voice commands.
    /// </summary>
    public class KeyCommandParserTests
    {
        [Theory]
        [InlineData("Enter", InjectedKey.Enter)]
        [InlineData("enter", InjectedKey.Enter)]
        [InlineData("RETURN", InjectedKey.Enter)]
        [InlineData("{ENTER}", InjectedKey.Enter)]
        [InlineData("Tab", InjectedKey.Tab)]
        [InlineData("Space", InjectedKey.Space)]
        [InlineData("Backspace", InjectedKey.Backspace)]
        [InlineData("Delete", InjectedKey.Delete)]
        [InlineData("Esc", InjectedKey.Escape)]
        [InlineData("pgdn", InjectedKey.PageDown)]
        [InlineData("uparrow", InjectedKey.Up)]
        [InlineData("F5", InjectedKey.F5)]
        [InlineData("a", InjectedKey.A)]
        [InlineData("7", InjectedKey.D7)]
        public void ParsesSingleKeys(string input, InjectedKey expected)
        {
            Assert.True(KeyCommandParser.TryParse(input, out var result));
            Assert.Equal(expected, result.Key);
            Assert.Equal(KeyModifiers.None, result.Modifiers);
        }

        [Theory]
        [InlineData("اینتر", InjectedKey.Enter)]
        [InlineData("انتر", InjectedKey.Enter)]
        [InlineData("تب", InjectedKey.Tab)]
        [InlineData("اسپیس", InjectedKey.Space)]
        [InlineData("دلیت", InjectedKey.Delete)]
        [InlineData("خروج", InjectedKey.Escape)]
        public void ParsesPersianAliasesFromExistingSettings(string input, InjectedKey expected)
        {
            Assert.True(KeyCommandParser.TryParse(input, out var result));
            Assert.Equal(expected, result.Key);
        }

        [Theory]
        [InlineData("Ctrl+Backspace", InjectedKey.Backspace, KeyModifiers.Control)]
        [InlineData("ctrl+c", InjectedKey.C, KeyModifiers.Control)]
        [InlineData("Alt+F4", InjectedKey.F4, KeyModifiers.Alt)]
        [InlineData("Alt+Tab", InjectedKey.Tab, KeyModifiers.Alt)]
        [InlineData("Ctrl+Shift+S", InjectedKey.S, KeyModifiers.Control | KeyModifiers.Shift)]
        public void ParsesCombinations(string input, InjectedKey key, KeyModifiers modifiers)
        {
            Assert.True(KeyCommandParser.TryParse(input, out var result));
            Assert.Equal(key, result.Key);
            Assert.Equal(modifiers, result.Modifiers);
        }

        [Theory]
        [InlineData("copy", InjectedKey.C)]
        [InlineData("paste", InjectedKey.V)]
        [InlineData("cut", InjectedKey.X)]
        [InlineData("undo", InjectedKey.Z)]
        [InlineData("selectall", InjectedKey.A)]
        [InlineData("save", InjectedKey.S)]
        public void ParsesNamedEditingShortcutsAsControlChords(string input, InjectedKey expected)
        {
            Assert.True(KeyCommandParser.TryParse(input, out var result));
            Assert.Equal(expected, result.Key);
            Assert.Equal(KeyModifiers.Control, result.Modifiers);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not a key")]
        [InlineData("Ctrl+")]
        [InlineData("F13")]
        public void RejectsUnrecognizedValuesWithoutThrowing(string input)
        {
            Assert.False(KeyCommandParser.TryParse(input, out _));
        }
    }

    public class KeyNamesTests
    {
        [Fact]
        public void RoundTripsLettersDigitsAndFunctionKeys()
        {
            foreach (var key in new[] { InjectedKey.A, InjectedKey.Z, InjectedKey.D0, InjectedKey.D9, InjectedKey.F1, InjectedKey.F12 })
                Assert.Equal(key, KeyNames.Parse(KeyNames.ToDisplayName(key)));
        }

        [Fact]
        public void HotkeyBindingDescribesItselfInModifierOrder()
        {
            var binding = new HotkeyBinding(InjectedKey.A, KeyModifiers.Control | KeyModifiers.Alt);
            Assert.Equal("Ctrl+Alt+A", binding.ToString());
        }

        [Fact]
        public void BindingWithoutModifiersIsNotValidForGlobalRegistration()
        {
            Assert.False(new HotkeyBinding(InjectedKey.A, KeyModifiers.None).IsValid);
            Assert.True(new HotkeyBinding(InjectedKey.A, KeyModifiers.Control).IsValid);
        }
    }
}
