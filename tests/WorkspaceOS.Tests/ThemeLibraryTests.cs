using System;
using System.IO;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Themes;
using Xunit;

namespace WorkspaceOS.Tests
{
    public class ThemeLibraryTests
    {
        [Fact]
        public void BuiltInThemesIncludeTheChosenOmarchySet()
        {
            foreach (var name in new[] { "Gold Rush", "Harbor", "Harbor Dark", "Matte Black", "Vantablack", "Pokemon" })
                Assert.NotNull(ThemeLibrary.Find(name));
        }

        [Fact]
        public void ApplyWritesTheWholePaletteAndMarksVersionFive()
        {
            var appearance = new AppearanceConfig();
            var harbor = ThemeLibrary.Find("Harbor");

            ThemeLibrary.Apply(harbor, appearance);

            Assert.Equal("Harbor", appearance.BarTheme);
            Assert.Equal(5, appearance.BarThemeVersion);
            Assert.Equal("#FFDFE4C4", appearance.BarBackground);
            Assert.Equal("#FF1C2D28", appearance.BarForeground);
            Assert.Equal("#FF5E81AC", appearance.ActiveWorkspaceBackground);
            // Ink on the accent must stay readable rather than assume a dark theme.
            Assert.Equal("#FFFFFFFF", appearance.ActiveWorkspaceForeground);
            Assert.NotEqual(appearance.BarBackground, appearance.ModuleBackground);
        }

        [Fact]
        public void ParsesAnOmarchyColorsTomlWithCommentsAndSections()
        {
            string dir = Path.Combine(Path.GetTempPath(), "workspaceos-theme-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "colors.toml");

            try
            {
                File.WriteAllText(file,
                    "# a comment\n" +
                    "accent = \"#aabbcc\"\n" +
                    "background = \"#101010\"\n" +
                    "foreground = \"#eeeeee\"\n" +
                    "mode = \"dark\"\n" +
                    "[extra]\n" +
                    "color0 = \"#111111\"\n" +
                    "color15 = \"#ffffff\"\n");

                var theme = OmarchyTheme.Load(file, "Test Theme");

                Assert.Equal("Test Theme", theme.Name);
                Assert.Equal("#AABBCC", theme.Accent);
                Assert.Equal("#101010", theme.Background);
                Assert.False(theme.IsLight);
                Assert.Equal("#111111", theme.Color(0));
                Assert.Equal("#FFFFFF", theme.Color(15));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void LightBackgroundsGetDarkInk()
        {
            Assert.Equal("#000000", ThemeColor.ContrastText("#DFE4C4"));
            Assert.Equal("#FFFFFF", ThemeColor.ContrastText("#121212"));
        }

        [Fact]
        public void CycleMovesOffTheCurrentThemeAndWraps()
        {
            var appearance = new AppearanceConfig { BarTheme = "Gold Rush" };

            var next = ThemeLibrary.Cycle(appearance, 1);

            Assert.NotNull(next);
            Assert.NotEqual("Gold Rush", next.Name);
            Assert.Equal(next.Name, appearance.BarTheme);

            // Cycling the whole library returns to the starting theme.
            for (int i = 0; i < ThemeLibrary.All.Count - 1; i++)
                ThemeLibrary.Cycle(appearance, 1);
            Assert.Equal("Gold Rush", appearance.BarTheme);
        }

        [Fact]
        public void UnknownThemeNamesAreRejectedWithoutTouchingThePalette()
        {
            var appearance = new AppearanceConfig();
            string before = appearance.BarBackground;

            Assert.False(ThemeLibrary.Apply("No Such Theme", appearance));
            Assert.Equal(before, appearance.BarBackground);
        }
    }
}
