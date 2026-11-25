using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Themes;

namespace WorkspaceOS.Core.Taskbar
{
    /// <summary>One registry value a Windhawk mod reads for a setting.</summary>
    internal readonly record struct WindhawkSetting(string Name, string Value, bool Numeric = false);

    /// <summary>
    /// A Windhawk mod whose settings follow the theme: which mod it is, where its
    /// preset files live, the flat registry values and the YAML for manual paste.
    /// The behaviour lives in <see cref="WindhawkPresetStore"/>.
    /// </summary>
    internal sealed class WindhawkModPreset
    {
        private readonly string _folder;
        private readonly Func<AppearanceConfig, IReadOnlyList<WindhawkSetting>> _settings;
        private readonly Func<AppearanceConfig, string> _yaml;

        public WindhawkModPreset(
            string modId,
            string folder,
            string surface,
            string[] stalePrefixes,
            Func<AppearanceConfig, IReadOnlyList<WindhawkSetting>> settings,
            Func<AppearanceConfig, string> yaml,
            Func<bool> available = null,
            string unavailableLog = null)
        {
            ModId = modId;
            _folder = folder;
            Surface = surface;
            StalePrefixes = stalePrefixes;
            _settings = settings;
            _yaml = yaml;
            Available = available;
            UnavailableLog = unavailableLog;
        }

        public string ModId { get; }

        /// <summary>How the mod is named in log lines, e.g. <c>taskbar</c>.</summary>
        public string Surface { get; }

        /// <summary>
        /// Settings that are arrays (<c>controlStyles[3].target</c>) and so can
        /// leave stale trailing entries behind when a preset gets shorter.
        /// </summary>
        public string[] StalePrefixes { get; }

        /// <summary>Whether the mod is installed; <c>null</c> means always.</summary>
        public Func<bool> Available { get; }

        public string UnavailableLog { get; }

        public bool ReportedMissing { get; set; }

        public string ModKey => @"SOFTWARE\Windhawk\Engine\Mods\" + ModId;

        public string SettingsKey => ModKey + @"\Settings";

        public string PresetPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WorkspaceOS", _folder, ModId + ".yaml");

        public string RegistryJsonPath => Path.ChangeExtension(PresetPath, ".registry.json");

        public bool IsAvailable => Available == null || Available();

        public IReadOnlyList<WindhawkSetting> Settings(AppearanceConfig a) => _settings(a);

        public string Yaml(AppearanceConfig a) => _yaml(a);
    }

    /// <summary>
    /// Stages a preset on disk and applies it to Windhawk's machine-wide settings.
    ///
    /// Windhawk stores mod settings under HKLM, which needs elevation, so the app
    /// either writes them itself when it is elevated or starts the
    /// highest-privileges task the setup script registers once; running an
    /// existing task needs no prompt, and Windhawk re-reads a mod's settings on
    /// its own, so neither path restarts its engine.
    ///
    /// New values are written before stale ones are pruned, and the change is
    /// announced only afterwards. That ordering matters: on a settings change the
    /// File Explorer styler re-initialises its XAML hooks inside every open
    /// Explorer window, so it must never be able to read a half-written set (a
    /// style pointing at a constant that is not there yet).
    /// </summary>
    internal static class WindhawkPresetStore
    {
        /// <summary>The task <c>tools/apply-windhawk-taskbar.ps1</c> registers.</summary>
        public const string TaskName = "WorkspaceOS Taskbar Preset";

        /// <summary>Writes the YAML preset and the flat registry values.</summary>
        public static string Write(WindhawkModPreset preset, AppearanceConfig a)
        {
            string path = preset.PresetPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, preset.Yaml(a), new UTF8Encoding(false));
            File.WriteAllText(preset.RegistryJsonPath, RegistryJson(preset, a), new UTF8Encoding(false));
            return path;
        }

        /// <summary>The flat registry values for the same settings.</summary>
        public static string RegistryJson(WindhawkModPreset preset, AppearanceConfig a)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            bool first = true;
            foreach (var setting in preset.Settings(a))
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(setting.Name).Append("\":");
                if (setting.Numeric)
                {
                    sb.Append(setting.Value);
                }
                else
                {
                    sb.Append('"')
                      .Append(setting.Value.Replace("\\", "\\\\").Replace("\"", "\\\""))
                      .Append('"');
                }
            }
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// True when Windhawk already holds exactly this preset. Every value is
        /// compared, so an edit to the preset itself is applied on the next theme
        /// change instead of being masked by an unchanged tint, and stale entries
        /// left by an older, longer preset count as out of date.
        /// </summary>
        public static bool IsCurrent(WindhawkModPreset preset, AppearanceConfig a)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(preset.SettingsKey);
                if (key == null) return false;

                var expected = preset.Settings(a);
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var setting in expected)
                {
                    names.Add(setting.Name);
                    object actual = key.GetValue(setting.Name);
                    if (setting.Numeric)
                    {
                        if (!int.TryParse(setting.Value, out int wanted) || !(actual is int number) || number != wanted)
                            return false;
                    }
                    else if (!string.Equals(actual as string, setting.Value, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }

                foreach (string name in key.GetValueNames())
                {
                    if (names.Contains(name)) continue;
                    foreach (string prefix in preset.StalePrefixes)
                    {
                        if (name.StartsWith(prefix, StringComparison.Ordinal)) return false;
                    }
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>Applies one preset, directly when elevated or through the task.</summary>
        public static bool Apply(WindhawkModPreset preset, AppearanceConfig a) =>
            ApplyAll(new[] { preset }, a);

        /// <summary>Applies several presets in a single elevated run.</summary>
        public static bool ApplyAll(IReadOnlyList<WindhawkModPreset> presets, AppearanceConfig a)
        {
            try
            {
                if (IsElevated())
                {
                    bool written = false;
                    foreach (var preset in presets)
                    {
                        if (!preset.IsAvailable) continue;
                        written |= WriteMachineSettings(preset, a);
                    }
                    return written;
                }

                bool ok = RunTask();
                LogTaskResult(ok);
                return ok;
            }
            catch (Exception ex)
            {
                ConfigService.Log("Taskbar: could not apply the Windhawk glass (" + ex.Message + ").");
                return false;
            }
        }

        /// <summary>Writes one preset straight into the machine-wide mod settings.</summary>
        private static bool WriteMachineSettings(WindhawkModPreset preset, AppearanceConfig a)
        {
            // Never create the key of a mod that is not installed: Windhawk would
            // list it as a broken entry.
            using var mod = Registry.LocalMachine.OpenSubKey(preset.ModKey, writable: true);
            if (mod == null) return false;

            using var settings = mod.CreateSubKey("Settings");
            if (settings == null) return false;

            var values = preset.Settings(a);
            var names = new HashSet<string>(StringComparer.Ordinal);

            // Write the new values first, so a reader never sees a gap.
            foreach (var setting in values)
            {
                names.Add(setting.Name);
                if (setting.Numeric)
                {
                    if (int.TryParse(setting.Value, out int number))
                        settings.SetValue(setting.Name, number, RegistryValueKind.DWord);
                }
                else
                {
                    settings.SetValue(setting.Name, setting.Value, RegistryValueKind.String);
                }
            }

            // Only then drop what a longer, older preset left behind.
            foreach (string name in settings.GetValueNames())
            {
                if (names.Contains(name)) continue;
                foreach (string prefix in preset.StalePrefixes)
                {
                    if (name.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        settings.DeleteValue(name, false);
                        break;
                    }
                }
            }

            // Announce the change last, once the set is complete.
            mod.SetValue("SettingsChangeTime",
                (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), RegistryValueKind.DWord);

            ConfigService.Log("Shell: " + preset.Surface + " glass written (elevated).");
            return true;
        }

        public static bool IsElevated()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        /// <summary>Applies the staged presets through the elevated task.</summary>
        public static bool RunTask()
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", "/run /tn \"" + TaskName + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p = Process.Start(psi);
                if (p == null) return false;
                p.WaitForExit(5000);
                return p.HasExited && p.ExitCode == 0;
            }
            catch { return false; }
        }

        public static void LogTaskResult(bool ok)
        {
            ConfigService.Log(ok
                ? "Shell: theme glass handed to the elevated task."
                : "Shell: the glass presets are staged; run tools\\apply-windhawk-taskbar.ps1 "
                  + "elevated once so they can be applied without prompting.");
        }

        /// <summary>
        /// The paper colour the bar is painted with. It comes from the appearance
        /// first (that is what <c>BarBackground</c> is set to when a theme is
        /// applied), so the glass follows the bar even if a theme's colours were
        /// edited by hand, and only falls back to the theme entry.
        /// </summary>
        public static string Paper(AppearanceConfig a)
        {
            var theme = ThemeLibrary.Find(a?.BarTheme);
            string value = (a?.BarBackground ?? theme?.Background ?? "#121212").TrimStart('#');
            if (value.Length == 8) value = value.Substring(2);
            return value.Length == 6 ? "#" + value : "#121212";
        }
    }
}
