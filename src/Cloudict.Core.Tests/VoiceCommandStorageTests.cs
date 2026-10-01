using System.Collections.Generic;
using System.Linq;
using Cloudict;
using Xunit;

namespace Cloudict.Core.Tests
{
    /// <summary>
    /// Covers how voice commands are stored per dictation language, and specifically the way a full
    /// set of them could become unreachable.
    ///
    /// <para>Before 3.x every command lived in one flat list. 3.x keeps a set per language and
    /// migrates the old list into Persian — but only when the Persian key was <em>missing</em>. The
    /// settings window could write an empty Persian set, at which point the key existed, the
    /// migration never ran again, and the user's commands were gone from the interface while still
    /// sitting in the settings file.</para>
    /// </summary>
    public class VoiceCommandStorageTests
    {
        [Fact]
        public void The_pre_3x_flat_list_is_adopted_as_the_persian_set()
        {
            var settings = new AppSettings
            {
                VoiceCommands = new List<VoiceCommand> { new VoiceCommand("ویرگول", CommandActionType.TypeText, "،") },
                VoiceCommandSets = new Dictionary<string, List<VoiceCommand>>()
            };

            var list = settings.GetVoiceCommandsFor("fa");

            Assert.Single(list);
            Assert.Equal("ویرگول", list[0].Phrase);
        }

        [Fact]
        public void An_empty_persian_set_does_not_strand_the_flat_list()
        {
            // Exactly the state a save with an empty grid left behind.
            var settings = new AppSettings
            {
                VoiceCommands = new List<VoiceCommand> { new VoiceCommand("ویرگول", CommandActionType.TypeText, "،") },
                VoiceCommandSets = new Dictionary<string, List<VoiceCommand>>
                {
                    ["fa"] = new List<VoiceCommand>()
                }
            };

            var list = settings.GetVoiceCommandsFor("fa");

            Assert.Single(list);
            Assert.Equal("ویرگول", list[0].Phrase);
        }

        [Fact]
        public void Adopting_the_flat_list_happens_once_so_deletions_stick()
        {
            var settings = new AppSettings
            {
                VoiceCommands = new List<VoiceCommand> { new VoiceCommand("ویرگول", CommandActionType.TypeText, "،") },
                VoiceCommandSets = new Dictionary<string, List<VoiceCommand>>()
            };

            Assert.Single(settings.GetVoiceCommandsFor("fa"));

            // The user deletes them all and saves.
            settings.SetVoiceCommandsFor("fa", new List<VoiceCommand>());

            Assert.Empty(settings.GetVoiceCommandsFor("fa"));
        }

        [Fact]
        public void A_language_with_its_own_set_is_left_alone()
        {
            var settings = new AppSettings
            {
                VoiceCommands = new List<VoiceCommand> { new VoiceCommand("ویرگول", CommandActionType.TypeText, "،") },
                VoiceCommandSets = new Dictionary<string, List<VoiceCommand>>
                {
                    ["fa"] = new List<VoiceCommand> { new VoiceCommand("نقطه", CommandActionType.TypeText, ".") }
                }
            };

            var list = settings.GetVoiceCommandsFor("fa");

            Assert.Single(list);
            Assert.Equal("نقطه", list[0].Phrase);
        }

        [Fact]
        public void Persian_gets_its_defaults_when_there_is_nothing_at_all()
        {
            var settings = new AppSettings
            {
                VoiceCommands = new List<VoiceCommand>(),
                VoiceCommandSets = new Dictionary<string, List<VoiceCommand>>()
            };

            Assert.NotEmpty(settings.GetVoiceCommandsFor("fa"));
        }

        [Fact]
        public void One_languages_commands_never_overwrite_anothers()
        {
            var settings = new AppSettings
            {
                VoiceCommands = new List<VoiceCommand>(),
                VoiceCommandSets = new Dictionary<string, List<VoiceCommand>>()
            };

            settings.SetVoiceCommandsFor("fa", new List<VoiceCommand> { new VoiceCommand("نقطه", CommandActionType.TypeText, ".") });
            settings.SetVoiceCommandsFor("en", new List<VoiceCommand>());

            Assert.Single(settings.GetVoiceCommandsFor("fa"));
            Assert.Empty(settings.GetVoiceCommandsFor("en"));
        }

        // ---- 3.2.4: the flat list stops doing three jobs at once ----------------------------

        /// <summary>
        /// The flat list used to mirror the active language. An English user's commands, mirrored
        /// there, were then adopted as the Persian set the next time Persian was empty.
        /// </summary>
        [Fact]
        public void English_commands_in_the_flat_list_never_become_persian_ones()
        {
            var settings = new AppSettings
            {
                TypingLanguage = "en",
                VoiceCommands = new List<VoiceCommand> { new VoiceCommand("comma", CommandActionType.TypeText, ",") },
                VoiceCommandSets = new Dictionary<string, List<VoiceCommand>>
                {
                    ["en"] = new List<VoiceCommand> { new VoiceCommand("comma", CommandActionType.TypeText, ",") },
                    ["fa"] = new List<VoiceCommand>()
                }
            };

            Assert.Empty(settings.GetVoiceCommandsFor("fa"));
            Assert.Single(settings.GetVoiceCommandsFor("en"));
        }

        /// <summary>The exact state found in a real settings file: English active, no Persian set,
        /// and the Persian defaults sitting in the flat list because the loader kept refilling it.</summary>
        [Fact]
        public void An_english_user_sees_no_persian_commands_and_persian_still_gets_its_defaults()
        {
            var settings = new AppSettings
            {
                TypingLanguage = "en",
                VoiceCommands = AppSettings.GetDefaultCommands(),
                VoiceCommandSets = new Dictionary<string, List<VoiceCommand>> { ["en"] = new List<VoiceCommand>() }
            };

            Assert.Empty(settings.GetVoiceCommandsFor("en"));
            Assert.Equal(AppSettings.GetDefaultCommands().Count, settings.GetVoiceCommandsFor("fa").Count);
        }

        [Theory]
        [InlineData("en")]
        [InlineData("ar")]
        [InlineData("de")]
        [InlineData("zh")]
        public void Only_persian_ships_with_default_commands(string language)
        {
            var settings = new AppSettings { LegacyVoiceCommandsMigrated = true };

            Assert.Empty(settings.GetVoiceCommandsFor(language));
            Assert.NotEmpty(settings.GetVoiceCommandsFor("fa"));
        }

        [Fact]
        public void The_migration_runs_once_and_the_flat_list_is_retired()
        {
            var settings = new AppSettings
            {
                VoiceCommands = new List<VoiceCommand> { new VoiceCommand("ویرگول", CommandActionType.TypeText, "،") },
                VoiceCommandSets = new Dictionary<string, List<VoiceCommand>>()
            };

            settings.MigrateLegacyVoiceCommands();

            Assert.True(settings.LegacyVoiceCommandsMigrated);
            Assert.Empty(settings.VoiceCommands);

            // Anything that lands in the flat list afterwards — an older build writing to the same
            // file, say — is ignored rather than adopted again.
            settings.SetVoiceCommandsFor("fa", new List<VoiceCommand>());
            settings.VoiceCommands = AppSettings.GetDefaultCommands();

            Assert.Empty(settings.GetVoiceCommandsFor("fa"));
        }

        [Fact]
        public void Adopted_commands_are_copies_not_the_legacy_objects()
        {
            var original = new VoiceCommand("ویرگول", CommandActionType.TypeText, "،");
            var settings = new AppSettings
            {
                VoiceCommands = new List<VoiceCommand> { original },
                VoiceCommandSets = new Dictionary<string, List<VoiceCommand>>()
            };

            var adopted = settings.GetVoiceCommandsFor("fa").Single();

            Assert.NotSame(original, adopted);
            Assert.Equal(original.Phrase, adopted.Phrase);
            Assert.Equal(original.ActionValue, adopted.ActionValue);
        }

        [Fact]
        public void The_flag_and_the_sets_survive_a_save_and_load()
        {
            var settings = new AppSettings { TypingLanguage = "fa", LegacyVoiceCommandsMigrated = true };
            settings.SetVoiceCommandsFor("fa", new List<VoiceCommand> { new VoiceCommand(1, "نقطه", CommandActionType.TypeText, ".") });

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(settings);
            var loaded = Newtonsoft.Json.JsonConvert.DeserializeObject<AppSettings>(json);

            Assert.True(loaded.LegacyVoiceCommandsMigrated);
            Assert.Equal("نقطه", loaded.GetVoiceCommandsFor("fa").Single().Phrase);
        }

        [Fact]
        public void Clone_copies_every_stored_field()
        {
            var c = new VoiceCommand(7, "اینتر", CommandActionType.SendKeys, "Enter") { IsEnabled = false };
            var copy = c.Clone();

            Assert.NotSame(c, copy);
            Assert.Equal(c.Id, copy.Id);
            Assert.Equal(c.Phrase, copy.Phrase);
            Assert.Equal(c.ActionType, copy.ActionType);
            Assert.Equal(c.ActionValue, copy.ActionValue);
            Assert.Equal(c.IsEnabled, copy.IsEnabled);
            Assert.Equal(c.CreatedAt, copy.CreatedAt);
        }

        [Theory]
        [InlineData("ویرگول", true)]
        [InlineData("پاپاک", true)]
        [InlineData("comma", false)]
        [InlineData("فاصلة", false)]       // Arabic
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Persian_is_recognised_by_its_own_letters(string phrase, bool persian) =>
            Assert.Equal(persian, AppSettings.ContainsPersianLetter(phrase));

        [Theory]
        [InlineData("en", 0)]
        [InlineData("fa", 17)]
        public void Resetting_the_manager_uses_the_dictation_languages_defaults(string language, int expected)
        {
            var settings = new AppSettings { TypingLanguage = language, LegacyVoiceCommandsMigrated = true };
            var manager = new Cloudict.Services.VoiceCommandManager(settings);

            manager.ResetToDefaults();

            Assert.Equal(expected, manager.TotalCount);
            Assert.Empty(settings.VoiceCommands);
        }
    }
}
