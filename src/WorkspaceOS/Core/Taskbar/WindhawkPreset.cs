using System.Collections.Generic;
using System.Text;
using WorkspaceOS.Core.Config;

namespace WorkspaceOS.Core.Taskbar
{
    /// <summary>
    /// The Windhawk <c>windows-11-taskbar-styler</c> settings for the active theme.
    ///
    /// The Windows 11 taskbar is drawn with XAML and ignores the accent policy, so
    /// the mod is the only surface that can make it glassy. This writes the mod's
    /// own <c>controlStyles</c> rather than one of its bundled themes: the theme
    /// paints the bar with a fixed grey and only exposes it through a style
    /// constant, while <c>controlStyles</c> lets the glass carry the theme's paper
    /// directly and survives mod updates.
    ///
    /// The glass is the mod's <c>WindhawkBlur</c> brush tinted with the theme's
    /// paper: a real backdrop blur rather than <c>AcrylicBrush</c>, which renders
    /// opaque inside the taskbar island. The taskbar's own fill and border are
    /// collapsed so nothing opaque sits over the blur, and the brush's own
    /// <c>FallbackColor</c> covers the case where the blur cannot be drawn.
    /// </summary>
    public static class WindhawkPreset
    {
        /// <summary>The Windhawk mod that owns the Windows 11 taskbar surface.</summary>
        public const string ModId = "windows-11-taskbar-styler";

        internal static readonly WindhawkModPreset Descriptor = new WindhawkModPreset(
            ModId,
            folder: "taskbar",
            surface: "taskbar",
            stalePrefixes: new[] { "controlStyles[" },
            settings: Settings,
            yaml: Yaml);

        public static string PresetPath => Descriptor.PresetPath;

        /// <summary>Flat registry values Windhawk uses for the same settings.</summary>
        public static string RegistryJsonPath => Descriptor.RegistryJsonPath;

        /// <summary>
        /// The glass, tinted with the theme paper. This is the mod's own
        /// <c>WindhawkBlur</c> brush, not <c>AcrylicBrush</c>: acrylic inside the
        /// taskbar island renders opaque on Windows 11 (a known Windows
        /// limitation the mod itself works around), which is why an acrylic bar
        /// looks like a flat panel instead of glass. <c>WindhawkBlur</c> blurs the
        /// real backdrop, so the bar picks up whatever window is behind it.
        /// </summary>
        public static string Brush(string paper) =>
            "<WindhawkBlur BlurAmount=\"24\" TintColor=\"" + paper + "\" " +
            "TintOpacity=\"0.30\" TintLuminosityOpacity=\"0.45\" FallbackColor=\"" + paper + "\"/>";

        /// <summary>The mod's textual settings, for manual paste.</summary>
        public static string Build(AppearanceConfig a) => Descriptor.Yaml(a);

        /// <summary>
        /// The flat registry values for the same settings. The app cannot write
        /// HKLM without elevation; the setup script and the elevated task consume
        /// this so the taskbar follows the bar instead of a stale tint.
        /// </summary>
        public static string BuildRegistryJson(AppearanceConfig a) =>
            WindhawkPresetStore.RegistryJson(Descriptor, a);

        /// <summary>Writes the YAML preset and the flat registry values.</summary>
        public static string Write(AppearanceConfig a) => WindhawkPresetStore.Write(Descriptor, a);

        private static IReadOnlyList<WindhawkSetting> Settings(AppearanceConfig a)
        {
            var settings = new List<WindhawkSetting>
            {
                new WindhawkSetting("clickThroughTaskbar", "0", Numeric: true)
            };

            var styles = Styles(WindhawkPresetStore.Paper(a));
            for (int i = 0; i < styles.Count; i++)
            {
                settings.Add(new WindhawkSetting($"controlStyles[{i}].target", styles[i].Target));
                for (int s = 0; s < styles[i].Styles.Length; s++)
                    settings.Add(new WindhawkSetting($"controlStyles[{i}].styles[{s}]", styles[i].Styles[s]));
            }
            return settings;
        }

        private static string Yaml(AppearanceConfig a)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Paste into Windhawk -> Windows 11 Taskbar Styler -> Advanced -> Textual settings.");
            sb.AppendLine("clickThroughTaskbar: false");
            sb.AppendLine("controlStyles:");
            foreach (var (target, styles) in Styles(WindhawkPresetStore.Paper(a)))
            {
                sb.AppendLine("  - target: " + target);
                sb.AppendLine("    styles:");
                foreach (string style in styles)
                    sb.AppendLine("      - '" + style.Replace("'", "''") + "'");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Target/stylesheet pairs: the glass surface, the themed fill, a rounded
        /// tray pill and slightly rounded task buttons. These are the styles that
        /// make the bar read as part of the desktop instead of stock Windows, and
        /// they work on every mod version because they are the mod's core setting.
        /// </summary>
        private static List<(string Target, string[] Styles)> Styles(string paper) =>
            new()
            {
                ("Taskbar.TaskbarFrame#TaskbarFrame", new[]
                {
                    "MaxWidth=10000",
                    "HorizontalAlignment=Stretch",
                    "Width=Auto",
                    "MinWidth=Auto"
                }),
                ("Taskbar.TaskbarFrame#TaskbarFrame > Grid#RootGrid", new[]
                {
                    "Padding=10,0,10,0",
                    "Margin=0",
                    "BorderThickness=0",
                    "CornerRadius=0",
                    "Background:=" + Brush(paper)
                }),
                ("Rectangle#BackgroundFill", new[] { "Visibility=Collapsed" }),
                ("Rectangle#BackgroundStroke", new[] { "Visibility=Collapsed" }),
                ("Grid#SystemTrayFrameGrid", new[]
                {
                    "Background:=#00000000",
                    "CornerRadius=20",
                    "BackgroundSizing=InnerBorderEdge",
                    "Margin=0,2,0,3"
                }),
                ("Taskbar.TaskListLabeledButtonPanel > Border#BackgroundElement", new[]
                {
                    "CornerRadius=6"
                }),
                ("TaskListButton > Taskbar.TaskListLabeledButtonPanel", new[]
                {
                    "Padding=5,6,5,6"
                })
            };
    }
}
