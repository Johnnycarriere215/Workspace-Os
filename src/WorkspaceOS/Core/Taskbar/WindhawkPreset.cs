using System;
using System.IO;
using System.Text;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Themes;

namespace WorkspaceOS.Core.Taskbar
{
    /// <summary>
    /// Produces the Windhawk <c>windows-11-taskbar-styler</c> settings for the
    /// active theme. The mod is enabled and applied at the machine level by
    /// <c>tools/apply-windhawk-taskbar.ps1</c>; this class owns the data those
    /// settings need and regenerates it when the theme changes.
    /// </summary>
    public static class WindhawkPreset
    {
        public static string PresetPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "WorkspaceOS", "taskbar", "windows-11-taskbar-styler.yaml");
            }
        }

        /// <summary>
        /// The mod's textual settings. TranslucentTaskbar is the mod's tested
        /// blur surface; CommonBgBrush is overridden with the theme paper so the
        /// taskbar follows the bar instead of the stock grey.
        /// </summary>
        public static string Build(AppearanceConfig a)
        {
            var theme = ThemeLibrary.Find(a?.BarTheme);
            string paper = StripAlpha(theme?.Background ?? a?.BarBackground ?? "#121212");
            string brush = CommonBrush(paper);

            var sb = new StringBuilder();
            sb.AppendLine("# Paste into Windhawk -> Windows 11 Taskbar Styler -> Advanced -> Textual settings.");
            sb.AppendLine("theme: TranslucentTaskbar");
            sb.AppendLine("clickThroughTaskbar: false");
            sb.AppendLine("themeResourceVariables:");
            sb.AppendLine("  - '" + brush.Replace("'", "''") + "'");
            return sb.ToString();
        }

        /// <summary>
        /// Flat registry values Windhawk uses for the same settings. The app
        /// cannot write HKLM without elevation; the setup script consumes this.
        /// </summary>
        public static string BuildRegistryJson(AppearanceConfig a)
        {
            var theme = ThemeLibrary.Find(a?.BarTheme);
            string paper = StripAlpha(theme?.Background ?? a?.BarBackground ?? "#121212");
            string brush = CommonBrush(paper).Replace("\\", "\\\\").Replace("\"", "\\\"");
            return "{\"theme\":\"TranslucentTaskbar\",\"clickThroughTaskbar\":0,"
                 + "\"themeResourceVariables[0]\":\"" + brush + "\"}";
        }

        /// <summary>Writes the YAML preset and the flat registry values.</summary>
        public static string Write(AppearanceConfig a)
        {
            string path = PresetPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, Build(a), new UTF8Encoding(false));
            File.WriteAllText(Path.ChangeExtension(path, ".registry.json"),
                BuildRegistryJson(a), new UTF8Encoding(false));
            return path;
        }

        private static string CommonBrush(string paper) =>
            "<WindhawkBlur BlurAmount=\"20\" TintColor=\"" + paper +
            "\" TintOpacity=\"0.55\" TintLuminosityOpacity=\"0.70\" FallbackColor=\"" + paper + "\"/>";

        private static string StripAlpha(string argb)
        {
            string value = (argb ?? "").TrimStart('#');
            if (value.Length == 8) value = value.Substring(2);
            return value.Length == 6 ? "#" + value : "#121212";
        }
    }
}
