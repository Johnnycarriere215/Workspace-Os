using System;
using System.Linq;
using System.Windows.Media;

namespace WorkspaceOS.UI
{
    /// <summary>
    /// A built-in look: the nine bar colours plus the palette used to paint its
    /// matching 4K wallpaper. Every preset is original to WorkspaceOS, so the
    /// artwork ships with the app and needs no licensing or network access.
    /// </summary>
    public sealed class ThemePreset
    {
        public string Name { get; init; }
        public string Tagline { get; init; }

        public string BarBackground { get; init; }
        public string BarForeground { get; init; }
        public string AccentColor { get; init; }
        public string ActiveWorkspaceBackground { get; init; }
        public string ActiveWorkspaceForeground { get; init; }
        public string ModuleLabelColor { get; init; }
        public string ModuleBackground { get; init; }
        public string ModuleBorder { get; init; }
        public string SeparatorColor { get; init; }

        public Color Deep1 { get; init; }
        public Color Deep2 { get; init; }
        public Color Accent { get; init; }
        public Color Accent2 { get; init; }
        public WallpaperKind Wallpaper { get; init; }
    }

    public enum WallpaperKind
    {
        Void,
        Synth,
        Toxic,
        Solar,
        Frost,
        Blood
    }

    public static class ThemeCatalog
    {
        public static readonly ThemePreset[] All =
        {
            new ThemePreset
            {
                Name = "Neon Void",
                Tagline = "GD cyan on deep space",
                BarBackground = "#FF05080A",
                BarForeground = "#FFE8F8F5",
                AccentColor = "#FF7FA7A3",
                ActiveWorkspaceBackground = "#FF00E0CF",
                ActiveWorkspaceForeground = "#FF04100F",
                ModuleLabelColor = "#FF6FD8CF",
                ModuleBackground = "#FF081012",
                ModuleBorder = "#FF123A37",
                SeparatorColor = "#FF123A37",
                Deep1 = C("#FF03070A"), Deep2 = C("#FF07282C"),
                Accent = C("#FF00E0CF"), Accent2 = C("#FF35B6FF"),
                Wallpaper = WallpaperKind.Void
            },
            new ThemePreset
            {
                Name = "Midnight Synth",
                Tagline = "Retrowave pink and violet",
                BarBackground = "#FF0B0518",
                BarForeground = "#FFF8ECFF",
                AccentColor = "#FFB08CFF",
                ActiveWorkspaceBackground = "#FFFF2E93",
                ActiveWorkspaceForeground = "#FF12001A",
                ModuleLabelColor = "#FFFF9AD4",
                ModuleBackground = "#FF140A24",
                ModuleBorder = "#FF4A2360",
                SeparatorColor = "#FF4A2360",
                Deep1 = C("#FF0A0418"), Deep2 = C("#FF2E0A44"),
                Accent = C("#FFFF2E93"), Accent2 = C("#FF7B3CFF"),
                Wallpaper = WallpaperKind.Synth
            },
            new ThemePreset
            {
                Name = "Toxic Bloom",
                Tagline = "Acid green bio-glow",
                BarBackground = "#FF04120A",
                BarForeground = "#FFEEFFEE",
                AccentColor = "#FF7FD98A",
                ActiveWorkspaceBackground = "#FF9BFF3D",
                ActiveWorkspaceForeground = "#FF06120A",
                ModuleLabelColor = "#FFC7FF9B",
                ModuleBackground = "#FF07180C",
                ModuleBorder = "#FF1F4A24",
                SeparatorColor = "#FF1F4A24",
                Deep1 = C("#FF03110B"), Deep2 = C("#FF0B3518"),
                Accent = C("#FF9BFF3D"), Accent2 = C("#FF1FE08A"),
                Wallpaper = WallpaperKind.Toxic
            },
            new ThemePreset
            {
                Name = "Solar Flare",
                Tagline = "Amber heat on charcoal",
                BarBackground = "#FF120A03",
                BarForeground = "#FFFFF3E0",
                AccentColor = "#FFC9A06A",
                ActiveWorkspaceBackground = "#FFFFA229",
                ActiveWorkspaceForeground = "#FF1A0A00",
                ModuleLabelColor = "#FFFFC98A",
                ModuleBackground = "#FF1A0E05",
                ModuleBorder = "#FF4A2C10",
                SeparatorColor = "#FF4A2C10",
                Deep1 = C("#FF140904"), Deep2 = C("#FF43190A"),
                Accent = C("#FFFFA229"), Accent2 = C("#FFFF5E3A"),
                Wallpaper = WallpaperKind.Solar
            },
            new ThemePreset
            {
                Name = "Frost Byte",
                Tagline = "Ice cyan crystalline",
                BarBackground = "#FF060D14",
                BarForeground = "#FFEAF7FF",
                AccentColor = "#FF8FB8D9",
                ActiveWorkspaceBackground = "#FF7FE9FF",
                ActiveWorkspaceForeground = "#FF04141C",
                ModuleLabelColor = "#FFB8E9FF",
                ModuleBackground = "#FF081520",
                ModuleBorder = "#FF1C3A4E",
                SeparatorColor = "#FF1C3A4E",
                Deep1 = C("#FF050C14"), Deep2 = C("#FF123A52"),
                Accent = C("#FF7FE9FF"), Accent2 = C("#FFD6F6FF"),
                Wallpaper = WallpaperKind.Frost
            },
            new ThemePreset
            {
                Name = "Blood Moon",
                Tagline = "Crimson lunar haze",
                BarBackground = "#FF0E0507",
                BarForeground = "#FFFFEDEF",
                AccentColor = "#FFC98A93",
                ActiveWorkspaceBackground = "#FFFF3B5C",
                ActiveWorkspaceForeground = "#FF1A0308",
                ModuleLabelColor = "#FFFF9AA8",
                ModuleBackground = "#FF150709",
                ModuleBorder = "#FF4A1A22",
                SeparatorColor = "#FF4A1A22",
                Deep1 = C("#FF0C0406"), Deep2 = C("#FF310B13"),
                Accent = C("#FFFF3B5C"), Accent2 = C("#FFFF8A6B"),
                Wallpaper = WallpaperKind.Blood
            }
        };

        public static ThemePreset Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Best-effort match of the current bar colours back to a preset.</summary>
        public static ThemePreset Match(string barBackground, string activeBackground)
        {
            return All.FirstOrDefault(t =>
                string.Equals(t.BarBackground, barBackground, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.ActiveWorkspaceBackground, activeBackground, StringComparison.OrdinalIgnoreCase));
        }

        private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    }
}
