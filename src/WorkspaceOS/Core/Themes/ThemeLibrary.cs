using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WorkspaceOS.Core.Config;

namespace WorkspaceOS.Core.Themes
{
    /// <summary>
    /// The theme set the bar cycles through: ports of Omarchy themes plus any
    /// <c>colors.toml</c> a user drops into <c>%APPDATA%\WorkspaceOS\themes\&lt;name&gt;\</c>.
    /// Applying a theme only writes the bar palette, so every existing surface
    /// (bar, settings, launcher, calendar) recolours through the same path.
    /// </summary>
    public static class ThemeLibrary
    {
        /// <summary>Ordered for cycling: the palettes this build ships.</summary>
        public static readonly IReadOnlyList<OmarchyTheme> BuiltIn = new[]
        {
            Theme("Gold Rush", "Gold on charcoal",
                "#121212", "#D9D9D9", "#C9A227", "#805B10", "#926C15",
                "#76520E", "#DBB42C", "#A47E1B", "#FAD643", "#926C15", "#B69121", "#805B10", "#EDC531",
                "#805B10", "#EDC531", "#B69121", "#FFEE69", "#A47E1B", "#C9A227", "#DBB42C", "#FAD643"),

            Theme("Harbor", "Paper light with cool ink",
                "#dfe4c4", "#1c2d28", "#5e81ac", "#606c68", "#c8d4a8",
                "#dfe4c4", "#b14752", "#556753", "#dc8164", "#4c6c94", "#8a5b81", "#3d727d", "#384f54",
                "#7d8794", "#b14752", "#556753", "#dc8164", "#4c6c94", "#8a5b81", "#3d727d", "#1c2d28"),

            Theme("Harbor Dark", "Deep harbour, coral accent",
                "#1B1B1B", "#efebdc", "#e75a50", "#89856b", "#323232",
                "#333333", "#F44336", "#a99b7a", "#e67e22", "#e75a50", "#6d6d6d", "#e58980", "#77838a",
                "#4a4a4a", "#F44336", "#C0AF7F", "#e67e22", "#e75a50", "#9aa0a6", "#e58980", "#f4f2e8"),

            Theme("Matte Black", "Orange on flat black",
                "#121212", "#bebebe", "#e68e0d", "#8a8a8d", "#515151",
                "#333333", "#D35F5F", "#FFC107", "#b91c1c", "#e68e0d", "#D35F5F", "#bebebe", "#bebebe",
                "#8a8a8d", "#B91C1C", "#FFC107", "#b90a0a", "#f59e0b", "#B91C1C", "#eaeaea", "#ffffff"),

            Theme("Vantablack", "Pure black, silver ink",
                "#000000", "#ffffff", "#8d8d8d", "#5c5c5c", "#ffffff",
                "#404040", "#a4a4a4", "#b6b6b6", "#cecece", "#8d8d8d", "#9b9b9b", "#b0b0b0", "#ececec",
                "#5c5c5c", "#a4a4a4", "#b6b6b6", "#cecece", "#8d8d8d", "#9b9b9b", "#b0b0b0", "#ffffff"),

            Theme("Pokemon", "Indigo night, gold ink",
                "#0f2138", "#fdf4c4", "#9a98be", "#7893b4", "#fdf4c4",
                "#0f2138", "#c56363", "#76c276", "#cebd7c", "#9a98be", "#d095d0", "#9fd5d5", "#fdf4c4",
                "#7893b4", "#e1a3a3", "#b1dfb1", "#e5dbb2", "#c3c2da", "#e5bee5", "#c2e7e7", "#fff7cc"),

            Theme("Rose Pine", "Soft rose on cream",
                "#faf4ed", "#575279", "#56949f", "#9893a5", "#dfdad9",
                "#f2e9e1", "#b4637a", "#286983", "#ea9d34", "#56949f", "#907aa9", "#d7827e", "#575279",
                "#9893a5", "#b4637a", "#286983", "#ea9d34", "#56949f", "#907aa9", "#d7827e", "#575279"),

            Theme("Tokyo Night", "Midnight blue neon",
                "#1a1b26", "#a9b1d6", "#7aa2f7", "#444b6a", "#7aa2f7",
                "#32344a", "#f7768e", "#9ece6a", "#e0af68", "#7aa2f7", "#ad8ee6", "#449dab", "#787c99",
                "#444b6a", "#ff7a93", "#b9f27c", "#ff9e64", "#7da6ff", "#bb9af7", "#0db9d7", "#acb0d0"),

            Theme("Catppuccin", "Mocha pastels",
                "#1e1e2e", "#cdd6f4", "#89b4fa", "#585b70", "#f5e0dc",
                "#45475a", "#f38ba8", "#a6e3a1", "#f9e2af", "#89b4fa", "#f5c2e7", "#94e2d5", "#bac2de",
                "#585b70", "#f38ba8", "#a6e3a1", "#f9e2af", "#89b4fa", "#f5c2e7", "#94e2d5", "#a6adc8"),

            Theme("Gruvbox", "Warm retro earth",
                "#282828", "#d4be98", "#7daea3", "#3c3836", "#d65d0e",
                "#3c3836", "#ea6962", "#a9b665", "#d8a657", "#7daea3", "#d3869b", "#89b482", "#d4be98",
                "#3c3836", "#ea6962", "#a9b665", "#d8a657", "#7daea3", "#d3869b", "#89b482", "#d4be98"),

            Theme("Nord", "Arctic blue slate",
                "#2e3440", "#d8dee9", "#81a1c1", "#4c566a", "#4c566a",
                "#3b4252", "#bf616a", "#a3be8c", "#ebcb8b", "#81a1c1", "#b48ead", "#88c0d0", "#e5e9f0",
                "#4c566a", "#bf616a", "#a3be8c", "#ebcb8b", "#81a1c1", "#b48ead", "#8fbcbb", "#eceff4"),

            Theme("Everforest", "Mossy forest calm",
                "#2d353b", "#d3c6aa", "#7fbbb3", "#475258", "#d3c6aa",
                "#475258", "#e67e80", "#a7c080", "#dbbc7f", "#7fbbb3", "#d699b6", "#83c092", "#d3c6aa",
                "#475258", "#e67e80", "#a7c080", "#dbbc7f", "#7fbbb3", "#d699b6", "#83c092", "#d3c6aa"),

            Theme("Kanagawa", "Ink wash indigo",
                "#1f1f28", "#dcd7ba", "#7e9cd8", "#727169", "#2d4f67",
                "#090618", "#c34043", "#76946a", "#c0a36e", "#7e9cd8", "#957fb8", "#6a9589", "#c8c093",
                "#727169", "#e82424", "#98bb6c", "#e6c384", "#7fb4ca", "#938aa9", "#7aa89f", "#dcd7ba"),

            Theme("Miasma", "Swampy olive haze",
                "#222222", "#c2c2b0", "#78824b", "#666666", "#78824b",
                "#000000", "#685742", "#5f875f", "#b36d43", "#78824b", "#bb7744", "#c9a554", "#d7c483",
                "#666666", "#685742", "#5f875f", "#b36d43", "#78824b", "#bb7744", "#c9a554", "#d7c483")
        };

        public static string ThemesDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WorkspaceOS", "themes");

        private static List<OmarchyTheme> _all;
        private static readonly object Gate = new();

        /// <summary>Built-ins plus every drop-in theme found on disk.</summary>
        public static IReadOnlyList<OmarchyTheme> All
        {
            get
            {
                lock (Gate)
                {
                    if (_all != null) return _all;

                    var list = new List<OmarchyTheme>(BuiltIn);
                    foreach (var theme in ScanDropIns())
                    {
                        int existing = list.FindIndex(t =>
                            string.Equals(t.Name, theme.Name, StringComparison.OrdinalIgnoreCase));
                        if (existing >= 0) list[existing] = theme;   // user override wins
                        else list.Add(theme);
                    }
                    _all = list;
                    return _all;
                }
            }
        }

        /// <summary>Forgets cached drop-ins so the next access rescans the folder.</summary>
        public static void Reload()
        {
            lock (Gate) _all = null;
        }

        public static OmarchyTheme Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Writes a theme into the appearance config (the single palette source).</summary>
        public static void Apply(OmarchyTheme theme, AppearanceConfig a)
        {
            if (theme == null || a == null) return;

            a.BarTheme = theme.Name;
            a.BarBackground = ThemeColor.Alpha(theme.Background, 0xFF);
            a.BarForeground = ThemeColor.Alpha(theme.Foreground, 0xFF);
            a.AccentColor = ThemeColor.Alpha(theme.InactiveBorder, 0xFF);
            a.ActiveWorkspaceBackground = ThemeColor.Alpha(theme.Accent, 0xFF);
            a.ActiveWorkspaceForeground = ThemeColor.Alpha(ThemeColor.ContrastText(theme.Accent), 0xFF);
            a.ModuleLabelColor = ThemeColor.Alpha(theme.Accent, 0xFF);
            a.ModuleBackground = ThemeColor.Alpha(
                ThemeColor.Blend(theme.Background, theme.Foreground, theme.IsLight ? 0.10 : 0.07), 0xFF);
            a.ModuleBorder = ThemeColor.Alpha(
                ThemeColor.Blend(theme.Background, theme.Foreground, theme.IsLight ? 0.22 : 0.16), 0xFF);
            a.SeparatorColor = a.ModuleBorder;
            a.BarThemeVersion = 5;
        }

        /// <summary>Applies by name; returns false when the theme is not installed.</summary>
        public static bool Apply(string name, AppearanceConfig a)
        {
            var theme = Find(name);
            if (theme == null) return false;
            Apply(theme, a);
            return true;
        }

        /// <summary>
        /// Moves to the next (or previous) theme in library order, wraps around,
        /// and returns the theme that is now active.
        /// </summary>
        public static OmarchyTheme Cycle(AppearanceConfig a, int direction = 1)
        {
            var list = All;
            if (list.Count == 0) return null;

            int index = list.ToList().FindIndex(t =>
                string.Equals(t.Name, a?.BarTheme, StringComparison.OrdinalIgnoreCase));
            if (index < 0) index = 0;
            else index = ((index + Math.Sign(direction == 0 ? 1 : direction)) % list.Count + list.Count) % list.Count;

            var theme = list[index];
            Apply(theme, a);
            return theme;
        }

        private static IEnumerable<OmarchyTheme> ScanDropIns()
        {
            string root = ThemesDirectory;
            if (!Directory.Exists(root)) yield break;

            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                string file = Path.Combine(dir, "colors.toml");
                if (!File.Exists(file)) continue;

                OmarchyTheme theme = null;
                try { theme = OmarchyTheme.Load(file, Path.GetFileName(dir)); }
                catch { /* a malformed drop-in must not break cycling */ }
                if (theme != null && !string.IsNullOrWhiteSpace(theme.Name)) yield return theme;
            }
        }

        private static OmarchyTheme Theme(string name, string tagline,
            string background, string foreground, string accent, string muted, string selection,
            params string[] colors)
        {
            return new OmarchyTheme
            {
                Name = name,
                Tagline = tagline,
                Background = ThemeColor.Normalize(background),
                Foreground = ThemeColor.Normalize(foreground),
                Accent = ThemeColor.Normalize(accent),
                Muted = ThemeColor.Normalize(muted),
                Selection = ThemeColor.Normalize(selection),
                IsLight = ThemeColor.Luminance(background) > 0.62,
                Colors = colors.Select(ThemeColor.Normalize).ToList()
            };
        }
    }
}
