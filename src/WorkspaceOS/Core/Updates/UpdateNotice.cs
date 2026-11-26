using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using WorkspaceOS.Core.Config;

namespace WorkspaceOS.Core.Updates
{
    /// <summary>
    /// A quiet "a newer release exists" notice. One GitHub API call at most twice a
    /// day, cached in %APPDATA%\WorkspaceOS\update-check.json, and nothing is ever
    /// downloaded, staged or installed: the app only names the release, links to its
    /// installer, and prints the from-source command as a fallback. The user takes
    /// the step when they choose to.
    /// </summary>
    public static class UpdateNotice
    {
        private const string LatestReleaseApi =
            "https://api.github.com/repos/Johnnycarriere215/Workspace-Os/releases/latest";

        /// <summary>The installer asset name every release publishes.</summary>
        public const string SetupFileName = "WorkspaceOS-Setup.exe";

        /// <summary>
        /// The one-line command that rebuilds from source and reinstalls. Kept as the
        /// fallback action: it needs the .NET SDK and a few minutes of compiling, so
        /// the installer download is offered first.
        /// </summary>
        public const string InstallCommand =
            "powershell -NoProfile -ExecutionPolicy Bypass -Command \"iwr https://raw.githubusercontent.com/Johnnycarriere215/Workspace-Os/main/tools/install-from-source.ps1 -OutFile $env:TEMP\\wos-install.ps1; & $env:TEMP\\wos-install.ps1 -Force\"";

        private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(12);
        private static readonly HttpClient Http = CreateClient();
        private static bool _checking;

        /// <summary>This copy's version, e.g. 5.2.1.</summary>
        public static Version Current { get; } = ReadCurrentVersion();

        /// <summary>The newer release's version (e.g. "5.3.0"), or null when current.</summary>
        public static string Latest { get; private set; }

        /// <summary>True once releases have been looked at (cache or network).</summary>
        public static bool Checked { get; private set; }

        /// <summary>True when the last check could not reach GitHub.</summary>
        public static bool Failed { get; private set; }

        /// <summary>Raised (off the UI thread) when <see cref="Latest"/> changes.</summary>
        public static event Action Changed;

        public static string ReleaseUrl(string version) =>
            "https://github.com/Johnnycarriere215/Workspace-Os/releases/tag/v" + version;

        /// <summary>The announced release's Setup.exe, pinned to that release's tag.</summary>
        public static string SetupUrl(string version) =>
            "https://github.com/Johnnycarriere215/Workspace-Os/releases/download/v" + version +
            "/" + SetupFileName;

        /// <summary>
        /// Shows a cached answer immediately, then refreshes it in the background.
        /// Safe to call once at startup; every failure is silent by design.
        /// </summary>
        public static void Start()
        {
            LoadCache();
            _ = CheckAsync();
        }

        /// <summary>Asks GitHub for the newest release and updates <see cref="Latest"/>.</summary>
        public static async Task CheckAsync()
        {
            if (_checking) return;
            _checking = true;
            try
            {
                if (IsCacheFresh()) { Checked = true; return; }

                string json = await Http.GetStringAsync(LatestReleaseApi).ConfigureAwait(false);
                if (!TryParseVersion(ReadTag(json), out var latest)) return;

                SaveCache(latest);
                Checked = true;
                SetLatest(latest > Current ? latest.ToString(3) : null);
            }
            catch (Exception ex)
            {
                // Offline, rate-limited or the repo moved: saying nothing is correct.
                Failed = true;
                Checked = true;
                ConfigService.Log("Update check skipped: " + ex.Message);
            }
            finally { _checking = false; }
        }

        private static void SetLatest(string version)
        {
            if (string.Equals(Latest, version, StringComparison.Ordinal)) return;
            Latest = version;
            try { Changed?.Invoke(); } catch { }
        }

        private sealed class CacheEntry
        {
            public DateTime CheckedUtc { get; set; }
            public string Latest { get; set; }
        }

        private static string CachePath => Path.Combine(ConfigService.DataDir, "update-check.json");

        private static void LoadCache()
        {
            try
            {
                var entry = ReadCache();
                if (entry == null) return;
                if (TryParseVersion(entry.Latest, out var latest) && latest > Current)
                    SetLatest(latest.ToString(3));
                Checked = true;
            }
            catch { }
        }

        private static bool IsCacheFresh()
        {
            var entry = ReadCache();
            return entry != null && DateTime.UtcNow - entry.CheckedUtc < CheckEvery;
        }

        private static CacheEntry ReadCache()
        {
            try
            {
                if (!File.Exists(CachePath)) return null;
                return JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(CachePath));
            }
            catch { return null; }
        }

        private static void SaveCache(Version latest)
        {
            try
            {
                Directory.CreateDirectory(ConfigService.DataDir);
                File.WriteAllText(CachePath, JsonSerializer.Serialize(new CacheEntry
                {
                    CheckedUtc = DateTime.UtcNow,
                    Latest = latest.ToString(3)
                }));
            }
            catch { }
        }

        private static string ReadTag(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
        }

        /// <summary>"v5.3.0-rc1" → 5.3.0; anything unrecognisable is ignored.</summary>
        private static bool TryParseVersion(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string cleaned = text.Trim().TrimStart('v', 'V');
            int suffix = cleaned.IndexOfAny(new[] { '-', '+' });
            if (suffix > 0) cleaned = cleaned.Substring(0, suffix);
            return Version.TryParse(cleaned, out version);
        }

        private static Version ReadCurrentVersion()
        {
            var v = typeof(UpdateNotice).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            // GitHub's API answers 403 to a request that carries no User-Agent.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WorkspaceOS-UpdateCheck");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }
    }
}
