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
    }
}
