using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Themes;

namespace WorkspaceOS.UI
{
    /// <summary>
    /// The gallery's view of <see cref="ThemeLibrary"/>: each Omarchy theme becomes
    /// a card with its bar palette plus the colours used to paint the matching 4K
    /// wallpaper. The theme's name is the identity — applying a card also sets
    /// <c>Appearance.BarTheme</c>, which is what the tiler and taskbar preset read.
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
        Blood,
        Waves,
        Petals,
        Dunes,
        Mono
    }

    public static class ThemeCatalog
    {
        public static readonly ThemePreset[] All = Build();

        public static ThemePreset Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Best-effort match of the current bar colours back to a theme.</summary>
        public static ThemePreset Match(string barBackground, string activeBackground)
        {
            return All.FirstOrDefault(t =>
                string.Equals(t.BarBackground, barBackground, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.ActiveWorkspaceBackground, activeBackground, StringComparison.OrdinalIgnoreCase));
        }

        private static ThemePreset[] Build()
        {
            var kinds = (WallpaperKind[])Enum.GetValues(typeof(WallpaperKind));
            var list = new List<ThemePreset>();

            int index = 0;
            foreach (var theme in ThemeLibrary.BuiltIn)
            {
                // Apply into a throwaway config: one mapping, no duplicated palette.
                var a = new AppearanceConfig();
                ThemeLibrary.Apply(theme, a);

                list.Add(new ThemePreset
                {
                    Name = theme.Name,
                    Tagline = theme.Tagline,
                    BarBackground = a.BarBackground,
                    BarForeground = a.BarForeground,
                    AccentColor = a.AccentColor,
                    ActiveWorkspaceBackground = a.ActiveWorkspaceBackground,
                    ActiveWorkspaceForeground = a.ActiveWorkspaceForeground,
                    ModuleLabelColor = a.ModuleLabelColor,
                    ModuleBackground = a.ModuleBackground,
                    ModuleBorder = a.ModuleBorder,
                    SeparatorColor = a.SeparatorColor,
                    Deep1 = C(theme.Background),
                    Deep2 = C(ThemeColor.Blend(theme.Background, theme.Accent, 0.40)),
                    Accent = C(theme.Accent),
                    Accent2 = C(theme.Color(6)),
                    Wallpaper = kinds[index++ % kinds.Length]
                });
            }
            return list.ToArray();
        }

        private static Color C(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(ThemeColor.Normalize(hex)); }
            catch { return Colors.Black; }
        }
    }
}
