using System.Collections.Generic;
using System.Text;
using Microsoft.Win32;
using WorkspaceOS.Core.Config;

namespace WorkspaceOS.Core.Taskbar
{
    /// <summary>
    /// The Windhawk <c>windows-11-file-explorer-styler</c> settings for the
    /// active theme.
    ///
    /// Explorer paints its own opaque XAML background, so the only way to see the
    /// desktop through it is that mod: it sets a blur behind the window
    /// (<c>backgroundTranslucentEffect: acrylicblur</c>) and restyles the XAML
    /// tree. The preset uses the mod's <c>WindowGlass</c> theme and replaces the
    /// two brushes that would otherwise paint it solid with a <c>WindhawkBlur</c>
    /// in the bar's paper colour:
    ///   * <c>MainContentBG</c> is the file list, Home and Gallery background. The
    ///     theme sets it to an opaque <c>SystemChromeAltHighColor</c>, which is why
    ///     the theme alone looks like a flat dark panel.
    ///   * <c>Background</c> is the menu and command-bar flyout background.
    /// Everything else the theme already sets to <c>Transparent</c>, so the blur
    /// behind the window shows through.
    ///
    /// The tint is deliberately more opaque than the taskbar's: a whole window of
    /// text over a bright wallpaper needs more separation than a bar does.
    ///
    /// The mod is installed from Windhawk's own mod list, so until it is present
    /// the preset is only staged and the log says so once per session.
    /// </summary>
    public static class ExplorerGlass
    {
        public const string ModId = "windows-11-file-explorer-styler";

        private const string ThemeName = "WindowGlass";
        private const string TranslucentEffect = "acrylicblur";

        /// <summary>The theme constants that would otherwise paint Explorer solid.</summary>
        private static readonly string[] ConstantNames = { "Background", "MainContentBG" };

        internal static readonly WindhawkModPreset Descriptor = new WindhawkModPreset(
            ModId,
            folder: "explorer",
            surface: "File Explorer",
            stalePrefixes: new[] { "styleConstants[" },
            settings: Settings,
            yaml: Yaml,
            available: () => IsInstalled,
            unavailableLog: "Taskbar: the File Explorer styler mod is not installed; Explorer glass "
                + "is staged. Install 'Windows 11 File Explorer Styler' in Windhawk to enable it.");

        public static string PresetPath => Descriptor.PresetPath;

        public static string RegistryJsonPath => Descriptor.RegistryJsonPath;

        /// <summary>Whether Windhawk has the mod compiled and enabled.</summary>
        public static bool IsInstalled
        {
            get
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(Descriptor.ModKey);
                    return key?.GetValue("LibraryFileName") is string library && library.Length > 0;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>The blur the mod paints Explorer with, tinted with the paper.</summary>
        public static string Brush(string paper) =>
            "<WindhawkBlur BlurAmount=\"24\" TintColor=\"" + paper + "\" " +
            "TintOpacity=\"0.50\" TintLuminosityOpacity=\"0.60\" FallbackColor=\"" + paper + "\"/>";

        /// <summary>The mod's textual settings, for manual paste.</summary>
        public static string Build(AppearanceConfig a) => Descriptor.Yaml(a);

        /// <summary>The flat registry values for the same settings.</summary>
        public static string BuildRegistryJson(AppearanceConfig a) =>
            WindhawkPresetStore.RegistryJson(Descriptor, a);

        /// <summary>Writes the YAML preset and the flat registry values.</summary>
        public static string Write(AppearanceConfig a) => WindhawkPresetStore.Write(Descriptor, a);

        private static IReadOnlyList<WindhawkSetting> Settings(AppearanceConfig a)
        {
            var settings = new List<WindhawkSetting>
            {
                new WindhawkSetting("theme", ThemeName),
                new WindhawkSetting("backgroundTranslucentEffect", TranslucentEffect)
            };

            var constants = ConstantsFor(a);
            for (int i = 0; i < constants.Count; i++)
                settings.Add(new WindhawkSetting($"styleConstants[{i}]", constants[i]));

            return settings;
        }

        private static string Yaml(AppearanceConfig a)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Paste into Windhawk -> Windows 11 File Explorer Styler -> Advanced -> Textual settings.");
            sb.AppendLine("theme: " + ThemeName);
            sb.AppendLine("backgroundTranslucentEffect: " + TranslucentEffect);
            sb.AppendLine("styleConstants:");
            foreach (string constant in ConstantsFor(a))
                sb.AppendLine("  - '" + constant.Replace("'", "''") + "'");
            return sb.ToString();
        }

        /// <summary>The style constants this preset overrides, in write order.</summary>
        private static List<string> ConstantsFor(AppearanceConfig a)
        {
            string brush = Brush(WindhawkPresetStore.Paper(a));
            var constants = new List<string>(ConstantNames.Length);
            foreach (string name in ConstantNames)
                constants.Add(name + "=" + brush);
            return constants;
        }
    }
}
