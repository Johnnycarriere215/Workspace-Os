using System;
using System.Collections.Generic;
using System.Linq;
using WorkspaceOS.Core.Config;
using Xunit;

namespace WorkspaceOS.Tests
{
    /// <summary>
    /// The shipped binding table is what every new install gets, so it has to be
    /// internally consistent: every workspace reachable both ways, no chord used
    /// twice, and nothing bound that the platform cannot run.
    /// </summary>
    public class HotkeyDefaultsTests
    {
        private static Dictionary<string, string> Windows() => WindowsDefaults();

        private static Dictionary<string, string> WindowsDefaults()
        {
            var bindings = HotkeyConfig.CreateDefaults();
            HotkeyConfig.ApplyPlatformDefaults(bindings, windows: true);
            return bindings;
        }

        private static Dictionary<string, string> LinuxDefaults()
        {
            var bindings = HotkeyConfig.CreateDefaults();
            HotkeyConfig.ApplyPlatformDefaults(bindings, windows: false);
            return bindings;
        }

        [Fact]
        public void EveryWorkspaceCanBeSwitchedToAndSentTo()
        {
            var bindings = Windows();
            for (int i = 1; i <= 9; i++)
            {
                Assert.True(bindings.TryGetValue($"Workspace{i}", out var switchTo));
                Assert.False(string.IsNullOrWhiteSpace(switchTo), $"Workspace{i} has no default chord");

                Assert.True(bindings.TryGetValue($"SendToWorkspace{i}", out var sendTo));
                Assert.False(string.IsNullOrWhiteSpace(sendTo), $"SendToWorkspace{i} has no default chord");
            }
        }

        [Fact]
        public void NoChordIsBoundToTwoActions()
        {
            AssertNoDuplicates(Windows(), "Windows");
            AssertNoDuplicates(LinuxDefaults(), "Linux");
        }

        private static void AssertNoDuplicates(Dictionary<string, string> bindings, string platform)
        {
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in bindings.Where(k => !string.IsNullOrWhiteSpace(k.Value)))
            {
                string chord = Normalize(kv.Value);
                Assert.False(seen.TryGetValue(chord, out var owner),
                    $"{platform}: '{kv.Key}' and '{owner}' both use {kv.Value}");
                seen[chord] = kv.Key;
            }
        }

        /// <summary>Modifier order and case do not make two chords different.</summary>
        private static string Normalize(string binding)
        {
            var mods = new List<string>();
            string key = "";
            foreach (var part in binding.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                switch (part.ToLowerInvariant())
                {
                    case "win": case "super": case "meta": mods.Add("win"); break;
                    case "ctrl": case "control": mods.Add("ctrl"); break;
                    case "alt": mods.Add("alt"); break;
                    case "shift": mods.Add("shift"); break;
                    default: key = part.ToLowerInvariant(); break;
                }
            }
            mods.Sort(StringComparer.Ordinal);
            return string.Join("+", mods) + "|" + key;
        }

        [Fact]
        public void WindowsKeepsAeroSnapAndShipsNoTiling()
        {
            var bindings = Windows();

            // Win+Arrow must stay Windows' own snap chords.
            foreach (var key in new[] { "MoveWindowLeft", "MoveWindowRight", "MoveWindowUp", "MoveWindowDown" })
            {
                Assert.True(bindings.TryGetValue(key, out var chord), $"{key} is missing from the defaults");
                Assert.True(string.IsNullOrWhiteSpace(chord), $"{key} still takes a chord from Aero Snap");
            }

            // Monitor moves and workspace sends are the Win+Shift family.
            Assert.Equal("Win+Shift+Left", bindings["MoveWindowToMonitorLeft"]);

            Assert.DoesNotContain(bindings.Keys, HotkeyConfig.IsTilingAction);
        }

        [Fact]
        public void WindowsDefaultsAnswerTheDocumentedChords()
        {
            var bindings = Windows();

            Assert.Equal("Alt+Space", bindings["Launcher"]);
            Assert.Equal("Win+Space", bindings["StartMenu"]);
            Assert.Equal("Ctrl+Win+A", bindings["VolumePanel"]);
            Assert.Equal("Ctrl+Win+Shift+Space", bindings["ThemePicker"]);
            Assert.Equal("", bindings["CycleTheme"]);
            Assert.Equal("Ctrl+Win+S", bindings["ScratchpadSend"]);
            Assert.Equal("Win+S", bindings["ScratchpadToggle"]);
            Assert.Equal("Win+W", bindings["CloseWindow"]);
        }

        [Fact]
        public void LinuxKeepsItsTilingChords()
        {
            var bindings = LinuxDefaults();

            Assert.Equal("Alt+Space", bindings["Launcher"]);
            Assert.Equal("Win+Shift+Left", bindings["TilePreselectLeft"]);
            Assert.Equal("Win+H", bindings["TileFocusLeft"]);
            Assert.True(string.IsNullOrWhiteSpace(bindings["MoveWindowToMonitorLeft"]));
        }

        [Fact]
        public void PlatformPassingAnEmptyTableDoesNotThrow()
        {
            HotkeyConfig.ApplyPlatformDefaults(null, windows: true);
            HotkeyConfig.ApplyPlatformDefaults(new Dictionary<string, string>(), windows: false);
        }
    }
}
