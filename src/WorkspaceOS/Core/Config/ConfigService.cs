using System;
using System.IO;
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
