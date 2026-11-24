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
                    RemoveWindowsTilingConfig();
                    MigrateBarTheme();
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

            a.BarThemeVersion = 4;
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

            // 4.8.0: Win+Space opens the WorkspaceOS launcher again (4.1–4.7 handed
            // it back to the native Start menu). Only the untouched default pair is
            // swapped — a launcher key the user rebound is never overwritten.
            if (System.OperatingSystem.IsWindows()
                && (!Config.Hotkeys.Bindings.TryGetValue("Launcher", out var launcher)
                    || string.IsNullOrWhiteSpace(launcher)
                    || string.Equals(launcher, "Alt+Space", StringComparison.OrdinalIgnoreCase))
                && Config.Hotkeys.Bindings.TryGetValue("StartMenu", out var startMenu)
                && string.Equals(startMenu, "Win+Space", StringComparison.OrdinalIgnoreCase))
            {
                Config.Hotkeys.Bindings["Launcher"] = "Win+Space";
                Config.Hotkeys.Bindings["LauncherAlt"] = "Alt+Space";
                Config.Hotkeys.Bindings["StartMenu"] = "";
                Log("Hotkeys: Win+Space now opens the WorkspaceOS launcher (Alt+Space still works).");
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
                if (removed > 0)
                    Log($"Hotkeys: removed {removed} obsolete tiling binding(s).");
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
