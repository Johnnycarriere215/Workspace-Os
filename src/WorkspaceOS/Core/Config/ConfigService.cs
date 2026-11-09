using System;
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
                    MigrateBarTheme();
                    MigrateTilingDefaults();
                    MergeHotkeyDefaults();
                }
            }
            catch (Exception ex)
            {
                Log("Config load failed, using defaults: " + ex.Message);
                try { File.Copy(ConfigPath, ConfigPath + ".corrupt", true); } catch { }
                Config = new AppConfig();
            }
            Save();
        }

        /// <summary>
        /// One-time restyle: upgrade saved configs to the Geometry Dash palette
        /// (void black, electric cyan, white ink). Only fires when the bar still
        /// uses a previous *default* palette, so explicit customisations survive.
        /// </summary>
        private void MigrateBarTheme()
        {
            var a = Config.Appearance;
            if (a.BarThemeVersion >= 4) return;

            bool oldBlack =
                string.Equals(a.BarBackground, "#FF000000", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.BarForeground, "#FFE0E0E0", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.FontFamily, "Consolas", StringComparison.OrdinalIgnoreCase);
            bool oldPokemon =
                string.Equals(a.BarBackground, "#FF0F2138", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.ActiveWorkspaceBackground, "#FFFFD700", StringComparison.OrdinalIgnoreCase);
            bool oldRise =
                (string.Equals(a.BarBackground, "#FF191717", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(a.BarBackground, "#FF181616", StringComparison.OrdinalIgnoreCase)) &&
                string.Equals(a.ActiveWorkspaceBackground, "#FFC4746E", StringComparison.OrdinalIgnoreCase);

            if (oldBlack || oldPokemon || oldRise)
            {
                var fresh = new AppearanceConfig();
                a.BarBackground = fresh.BarBackground;
                a.BarForeground = fresh.BarForeground;
                a.AccentColor = fresh.AccentColor;
                a.ActiveWorkspaceBackground = fresh.ActiveWorkspaceBackground;
                a.ActiveWorkspaceForeground = fresh.ActiveWorkspaceForeground;
                a.ModuleLabelColor = fresh.ModuleLabelColor;
                a.SeparatorColor = fresh.SeparatorColor;
                a.FontFamily = fresh.FontFamily;
                a.FontSize = fresh.FontSize;
                a.BarHeight = fresh.BarHeight;
                Log($"Appearance: migrated bar palette (v{a.BarThemeVersion}) to Geometry Dash.");
            }

            // The focused-window border followed the old gold accent.
            if (string.Equals(Config.Tiling.IndicatorColor, "#FFFFD75F", StringComparison.OrdinalIgnoreCase))
                Config.Tiling.IndicatorColor = "#FF00E0CF";

            a.BarThemeVersion = 4;
        }

        /// <summary>
        /// One-time v4 migration: configs from the 2.x line predate
        /// "Hyprland-style tiling by default". Enable tiling and adopt the
        /// Geometry Dash cyan indicator exactly once; SchemaVersion guards it
        /// so a later deliberate opt-out is never reverted.
        /// </summary>
        private void MigrateTilingDefaults()
        {
            var t = Config.Tiling;
            if (t.SchemaVersion >= 4) return;

            t.EnableTiling = true;
            t.IndicatorColor = "#FF00E0CF";
            t.SchemaVersion = 4;
            Log("Tiling: migrated config to v4 — tiling enabled, cyan GD indicator.");
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

            // Win+Space is a passthrough to the native Start menu now, not an
            // app action. Drop the short-lived custom binding from v4.0.1.
            if (Config.Hotkeys.Bindings.Remove("LauncherWinSpace"))
                Log("Hotkeys: removed the temporary Win+Space launcher binding; Win+Space now opens Start.");

            // v4.1: Win+Shift+Arrow moves a window to another monitor on Windows.
            // Older configs used those chords for split preselection; free them so
            // the new actions can take over (preselect remains rebindable). The
            // Linux daemon keeps the old defaults.
            if (System.OperatingSystem.IsWindows())
            {
                int clearedPreselect = 0;
                foreach (var dir in new[] { "Left", "Right", "Up", "Down" })
                {
                    string key = "TilePreselect" + dir;
                    if (Config.Hotkeys.Bindings.TryGetValue(key, out var chord) &&
                        string.Equals(chord, "Win+Shift+" + dir, StringComparison.OrdinalIgnoreCase))
                    {
                        Config.Hotkeys.Bindings[key] = "";
                        clearedPreselect++;
                    }
                }
                if (clearedPreselect > 0)
                    Log("Hotkeys: freed Win+Shift+Arrow for monitor moves (preselect unbound).");
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
