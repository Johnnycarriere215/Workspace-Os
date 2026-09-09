using System.Collections.Generic;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Linux;
using Xunit;

namespace WorkspaceOS.Tests
{
    /// <summary>
    /// The Linux keymap translator: WorkspaceOS combos → X11 keysym
    /// strings as consumed by Cinnamon custom keybindings and xbindkeys.
    /// </summary>
    public class LinuxKeymapTests
    {
        [Theory]
        [InlineData("Win+1", "<super>1")]
        [InlineData("Win+Shift+2", "<super><shift>2")]
        [InlineData("Win+Ctrl+H", "<super><ctrl>h")]
        [InlineData("Alt+Space", "<alt>space")]
        [InlineData("Win+H", "<super>h")]
        [InlineData("Win+Left", "<super>Left")]
        [InlineData("Win+F1", "<super>F1")]
        [InlineData("Win+Shift+Space", "<super><shift>space")]
        [InlineData("Super+Escape", "<super>Escape")]
        [InlineData("Win+Shift+T", "<super><shift>t")]
        public void TranslatesStandardCombos(string combo, string expected)
        {
            Assert.Equal(expected, KeymapTranslator.ToX11Combo(combo));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Win")]           // bare modifier cannot bind
        [InlineData("Win+Shift")]     // two modifiers, no key
        [InlineData("Win+H+J")]       // two keys is not a chord
        public void RejectsUnbindableCombos(string combo)
        {
            Assert.Null(KeymapTranslator.ToX11Combo(combo));
        }

        [Fact]
        public void DescribeIsHumanReadable()
        {
            Assert.Equal("Super+Shift+2", KeymapTranslator.Describe("Win+Shift+2"));
            Assert.Equal("Alt+space", KeymapTranslator.Describe("Alt+Space"));
        }

        /// <summary>
        /// Every default binding in HotkeyConfig must translate — otherwise
        /// part of the shipped keymap silently never registers on Linux.
        /// (Disabled bindings map to "" and are skipped by design.)
        /// </summary>
        [Fact]
        public void AllDefaultBindingsTranslate()
        {
            var bindings = new HotkeyConfig().Bindings;
            Assert.NotEmpty(bindings);
            foreach (var kv in bindings)
            {
                if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                Assert.True(KeymapTranslator.ToX11Combo(kv.Value) != null,
                    $"default binding {kv.Key}='{kv.Value}' must translate to X11");
            }
        }

        /// <summary>
        /// The whole keymap round-trips through gsettings-style quoting:
        /// combos contain no characters that break `gsettings set ... "['<combo>']"`.
        /// </summary>
        [Fact]
        public void CombosAreSafeForGsettingsQuoting()
        {
            foreach (var kv in new HotkeyConfig().Bindings)
            {
                if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                var combo = KeymapTranslator.ToX11Combo(kv.Value);
                if (combo == null) continue;
                Assert.DoesNotContain("'", combo);
                Assert.DoesNotContain("\"", combo);
                Assert.DoesNotContain("$", combo);
            }
        }
    }
}
