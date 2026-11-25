using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using WorkspaceOS.Core.Config;

namespace WorkspaceOS.Core.Cava
{
    /// <summary>
    /// Drives the user's own cava.exe and turns its raw output into bar heights
    /// the UI can paint with theme colours.
    ///
    /// cava is started with a generated config that only changes the output
    /// section (<c>method = raw</c>, ASCII levels on stdout), so the user's own
    /// cava config, shaders and theme files are never modified. The visualiser
    /// is a child process: if it crashes or is missing, the bar simply hides the
    /// widget and the rest of the app is unaffected.
    /// </summary>
    public sealed class CavaService : IDisposable
    {
        private readonly ConfigService _configs;
        private Process _process;
        private volatile bool _running;
        private double[] _bars = Array.Empty<double>();
        private readonly object _gate = new();

        public CavaService(ConfigService configs) => _configs = configs;

        /// <summary>Latest frame, normalised 0..1.</summary>
        public double[] Bars
        {
            get { lock (_gate) return (double[])_bars.Clone(); }
        }

        public bool Running => _running;
        public int BarCount { get; private set; } = 18;

        /// <summary>Raised on the cava reader thread after each frame.</summary>
        public event Action LevelsChanged;

        public static string GeneratedConfigPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WorkspaceOS", "cava-workspaceos.conf");

        /// <summary>Locates cava.exe: configured path, the usual install spots, then PATH.</summary>
        public static string FindCava(string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

            var candidates = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "cava", "cava.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "cava", "cava.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "cava", "cava.exe")
            };

            foreach (var path in candidates)
                if (File.Exists(path)) return path;

            string env = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in env.Split(Path.PathSeparator))
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    string path = Path.Combine(dir.Trim(), "cava.exe");
                    if (File.Exists(path)) return path;
                }
                catch { }
            }
            return "";
        }

        public void Start()
        {
            Stop();

            var bar = _configs.Config.Bar;
            if (!bar.ShowCava) return;

            string exe = FindCava(bar.CavaPath);
            if (string.IsNullOrWhiteSpace(exe))
            {
                ConfigService.Log("Cava: cava.exe not found — the bar's visualiser stays hidden.");
                return;
            }

            BarCount = Math.Clamp(bar.CavaBars <= 0 ? 18 : bar.CavaBars, 8, 48);
            if (!TryWriteConfig()) return;

            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    Arguments = $"-p \"{GeneratedConfigPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _process.OutputDataReceived += OnOutput;
                _process.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                        ConfigService.Log("Cava: " + e.Data.Trim());
                };
                _process.Exited += (_, _) =>
                {
                    _running = false;
                    ConfigService.Log("Cava: process exited; visualiser hidden.");
                };

                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                _running = true;
                ConfigService.Log($"Cava: visualiser started ({BarCount} bars) from {exe}.");
            }
            catch (Exception ex)
            {
                _running = false;
                ConfigService.Log("Cava: could not start (" + ex.Message + ").");
                _process?.Dispose();
                _process = null;
            }
        }

        public void Stop()
        {
            _running = false;
            var process = _process;
            _process = null;
            if (process == null) return;

            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch { }
            finally { process.Dispose(); }

            lock (_gate) _bars = Array.Empty<double>();
            LevelsChanged?.Invoke();
        }

        /// <summary>Restarts after a settings change (bar count, path, on/off).</summary>
        public void Restart() => Start();

        private void OnOutput(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;

            // raw/ascii frames are "12;40;77;..." — one line per frame.
            var parts = e.Data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            var frame = new double[parts.Length];
            int read = 0;
            foreach (var part in parts)
            {
                if (!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                    continue;
                frame[read++] = Math.Clamp(value / 100.0, 0, 1);
            }

            if (read == 0) return;
            if (read != frame.Length) Array.Resize(ref frame, read);

            lock (_gate) _bars = frame;
            LevelsChanged?.Invoke();
        }

        private bool TryWriteConfig()
        {
            try
            {
                string path = GeneratedConfigPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

                var text = new StringBuilder();
                text.AppendLine("# Generated by WorkspaceOS for its bar widget — edits are overwritten.");
                text.AppendLine("[general]");
                text.AppendLine($"bars = {BarCount}");
                text.AppendLine("framerate = 60");
                text.AppendLine("sensitivity = 110");
                text.AppendLine("lower_cutoff_freq = 50");
                text.AppendLine("higher_cutoff_freq = 12000");
                text.AppendLine();
                // No [input] section: the Windows build rejects an explicit input
                // method and uses WASAPI loopback on its own.
                text.AppendLine();
                text.AppendLine("[output]");
                text.AppendLine("method = raw");
                text.AppendLine("raw_target = /dev/stdout");
                text.AppendLine("data_format = ascii");
                text.AppendLine("ascii_max_range = 100");
                text.AppendLine("bar_delimiter = 59");
                text.AppendLine("frame_delimiter = 10");
                text.AppendLine();
                text.AppendLine("[smoothing]");
                text.AppendLine("noise_reduction = 60");
                text.AppendLine("monstercat = 1");
                text.AppendLine("waves = 0");

                File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                ConfigService.Log("Cava: could not write its config (" + ex.Message + ").");
                return false;
            }
        }

        public void Dispose() => Stop();
    }
}
