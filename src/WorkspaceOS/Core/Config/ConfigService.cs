using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkspaceOS.Core.Config
{
    /// <summary>Loads/saves config.json from %APPDATA%\WorkspaceOS. Thread-safe, atomic writes.</summary>
    public class ConfigService
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            PropertyNameCaseInsensitive = true,
        };

        private readonly object _lock = new();

        public static string DataDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WorkspaceOS");

        public static string ConfigPath => Path.Combine(DataDir, "config.json");
        public static string LogPath => Path.Combine(DataDir, "workspaceos.log");

        public AppConfig Config { get; private set; } = new();

        public event Action ConfigChanged;

        public void Load()
        {
            Directory.CreateDirectory(DataDir);
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    Config = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts) ?? new AppConfig();
                    RemoveWindowsTilingConfig();
                    MigrateBarTheme();
                    MigrateShibumiLayout();
                    MergeHotkeyDefaults();
                }
            }
            catch (Exception ex)
            {
                Log("Config load failed, using defaults: " + ex.Message);
                try { File.Copy(ConfigPath, ConfigPath + ".corrupt", true); } catch { }
                Config = new AppConfig();
            }
            RemoveWindowsTilingConfig();
            Save();
        }

        private void RemoveWindowsTilingConfig()
        {
            if (System.OperatingSystem.IsWindows()) Config.Tiling = null;
        }

        /// <summary>
        /// One-time move into the Omarchy theme system. 5.0 replaced the hand-made
        /// presets, so an upgraded config keeps the colours it already has instead
        /// of being repainted: the palette is still valid and only its preset name
        /// goes away. Omarchy themes are one click away in the bar's logo menu.
        /// </summary>
        private void MigrateBarTheme()
        {
            var a = Config.Appearance;
            if (a.BarThemeVersion >= 5) return;

            a.BarTheme = "Custom";
            a.BarThemeVersion = 5;

            // 5.0 shows logos instead of the old bar marks; a name from the old
            // catalog would leave an empty button. A custom image path still wins.
            // Kept here rather than read from the UI catalog: the tests compile
            // this file without the Windows-only UI layer.
            var logos = new[] { "Shibumi", "Hyprland", "Arch", "Omarchy", "Omacom", "Dragon", "Mark" };
            if (string.Equals(a.BarIcon, "WorkspaceOS", StringComparison.OrdinalIgnoreCase))
                a.BarIcon = "Shibumi";
            if (!logos.Any(n => string.Equals(n, a.BarIcon, StringComparison.OrdinalIgnoreCase)))
                a.BarIcon = "Shibumi";

            Log("Appearance: kept your palette as 'Custom'; the bar's logo menu lists the Omarchy themes.");
        }

        /// <summary>
        /// Moves an untouched bar onto the Shibumi shell once. A form the user
        /// picked themselves — Fit, Dock, Notch — is left alone, and so is every
        /// colour; only the default "Full" strip becomes the Shibumi run.
        /// </summary>
        private void MigrateShibumiLayout()
        {
            var bar = Config.Bar;
            if (bar?.Shibumi == null || bar.Shibumi.LayoutVersion >= 1) return;

            if (string.Equals(bar.Profile, "Full", StringComparison.OrdinalIgnoreCase))
            {
                bar.Profile = "Shibumi";
                Log("Bar: switched to the Shibumi shell (one run, pill widgets).");
            }
            bar.Shibumi.LayoutVersion = 1;
        }

        /// <summary>
        /// Adds bindings introduced by a newer build (e.g. the Win+Space launcher)
        /// without touching keys the user already has. A chord that is already in
        /// use is never duplicated — AHK refuses to load duplicate hotkeys.
        /// </summary>
        private void MergeHotkeyDefaults()
        {
            if (Config.Hotkeys.Bindings == null)
                Config.Hotkeys.Bindings = new HotkeyConfig().Bindings;

            // Win+Space goes back to the Windows-key menu (Start), which is what
            // it did before 4.8.0 took the chord for the launcher; the launcher
            // moved to Alt+Space. Only the untouched 4.8 default pair is swapped —
            // a launcher key the user rebound themselves is never overwritten.
            if (System.OperatingSystem.IsWindows()
                && Config.Hotkeys.Bindings.TryGetValue("Launcher", out var launcher)
                && string.Equals(launcher, "Win+Space", StringComparison.OrdinalIgnoreCase)
                && Config.Hotkeys.Bindings.TryGetValue("StartMenu", out var startMenu)
                && string.IsNullOrWhiteSpace(startMenu))
            {
                Config.Hotkeys.Bindings["Launcher"] = "Alt+Space";
                Config.Hotkeys.Bindings["LauncherAlt"] = "";
                Config.Hotkeys.Bindings["StartMenu"] = "Win+Space";
                Log("Hotkeys: Win+Space opens the Start menu again; the launcher is on Alt+Space.");
            }

            // Drop the short-lived custom Win+Space launcher binding from v4.0.1.
            if (Config.Hotkeys.Bindings.Remove("LauncherWinSpace"))
                Log("Hotkeys: removed the temporary Win+Space launcher binding; Win+Space opens the launcher.");

            // The in-app screenshot tool is gone (it took Win+Shift+S from Snipping
            // Tool). Drop the binding so the native capture shortcut works again.
            if (Config.Hotkeys.Bindings.Remove("Screenshot"))
                Log("Hotkeys: removed the screenshot binding; Win+Shift+S is Snipping Tool again.");

            if (System.OperatingSystem.IsWindows())
            {
                // Tiling was removed from the Windows app. Delete its old
                // bindings so they do not keep swallowing keys with no handler.
                int removed = 0;
                foreach (var key in Config.Hotkeys.Bindings.Keys
                             .Where(HotkeyConfig.IsTilingAction).ToList())
                {
                    Config.Hotkeys.Bindings.Remove(key);
                    removed++;
                }
                if (Config.Hotkeys.Bindings.Remove("ToggleTilingWm")) removed++;
                if (removed > 0)
                    Log($"Hotkeys: removed {removed} obsolete tiling binding(s).");
            }

            // Ctrl+Win+Shift+Space used to cycle themes blind; it opens the theme
            // picker now. Release the old default so the new binding can take it —
            // a chord the user rebound themselves is left alone.
            if (Config.Hotkeys.Bindings.TryGetValue("CycleTheme", out var cycle)
                && string.Equals(cycle, "Ctrl+Win+Shift+Space", StringComparison.OrdinalIgnoreCase))
            {
                Config.Hotkeys.Bindings["CycleTheme"] = "";
                Log("Hotkeys: Ctrl+Win+Shift+Space opens the theme picker.");
            }

            // Win+Arrow is Aero Snap. Earlier builds put the window nudges on
            // those chords; give them back when they are still the untouched
            // default, and leave a chord the user picked alone.
            if (System.OperatingSystem.IsWindows())
            {
                var snap = new Dictionary<string, string>
                {
                    ["MoveWindowLeft"] = "Win+Left",
                    ["MoveWindowRight"] = "Win+Right",
                    ["MoveWindowUp"] = "Win+Up",
                    ["MoveWindowDown"] = "Win+Down"
                };
                int released = 0;
                foreach (var kv in snap)
                {
                    if (Config.Hotkeys.Bindings.TryGetValue(kv.Key, out var chord)
                        && string.Equals(chord, kv.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        Config.Hotkeys.Bindings[kv.Key] = "";
                        released++;
                    }
                }
                if (released > 0)
                    Log($"Hotkeys: returned {released} Win+Arrow chord(s) to Aero Snap.");
            }

            var defaults = new HotkeyConfig().Bindings;
            bool added = false;
            foreach (var kv in defaults)
            {
                if (Config.Hotkeys.Bindings.ContainsKey(kv.Key)) continue;
                if (Config.Hotkeys.Bindings.Values.Any(v =>
                        string.Equals(v, kv.Value, StringComparison.OrdinalIgnoreCase))) continue;
                Config.Hotkeys.Bindings[kv.Key] = kv.Value;
                added = true;
            }
            if (added) Log("Hotkeys: added new default binding(s).");
        }

        public void Save()
        {
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(DataDir);
                    var tmp = ConfigPath + ".tmp";
                    File.WriteAllText(tmp, JsonSerializer.Serialize(Config, JsonOpts));
                    File.Move(tmp, ConfigPath, true);
                }
                catch (Exception ex) { Log("Config save failed: " + ex.Message); }
            }
        }

        public void NotifyChanged()
        {
            Save();
            ConfigChanged?.Invoke();
        }

        public string Export() => JsonSerializer.Serialize(Config, JsonOpts);

        public bool Import(string json)
        {
            try
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts);
                if (cfg == null) return false;
                Config = cfg;
                RemoveWindowsTilingConfig();
                MigrateBarTheme();
                MigrateShibumiLayout();
                MergeHotkeyDefaults();
                NotifyChanged();
                return true;
            }
            catch (Exception ex) { Log("Import failed: " + ex.Message); return false; }
        }

        private static readonly object LogLock = new();
        public static void Log(string message)
        {
            try
            {
                lock (LogLock)
                {
                    Directory.CreateDirectory(DataDir);
                    File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
                }
            }
            catch { }
        }
    }
}
