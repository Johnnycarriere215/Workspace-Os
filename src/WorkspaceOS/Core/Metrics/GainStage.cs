using System;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace WorkspaceOS.Core.Metrics
{
    /// <summary>
    /// The gain stage that lets the volume go past 100 %. Windows stops an output
    /// at 0 dB, so the shell asks EqualizerAPO — attached to the endpoint as an
    /// audio processing object — for a preamp in dB. Everything the volume slider
    /// shows above 100 % is this file; at or below 100 % the stage is out of the
    /// path and the endpoint does all the work.
    /// </summary>
    public static class GainStage
    {
        /// <summary>Headroom the slider is allowed to ask for; 6 dB doubles the signal.</summary>
        public const double MaxDb = 6.0;

        private const string EapoSfx = "{EACD2258-FCAC-4FF4-B36D-419E924A6D79}";
        private const string PremixKey = "{d3993a3f-99c2-4402-b5ec-a92a0367664b},5";

        private static readonly string[] ConfigDirs =
        {
            @"C:\Program Files\EqualizerAPO\config",
            @"C:\EqualizerAPO\config"
        };

        private static string _dir;
        private static bool _attached;
        private static bool _registered;
        private static DateTime _checked = DateTime.MinValue;

        /// <summary>
        /// An endpoint id looks like "{0.0.0.00000000}.{guid}" while the device
        /// registry keys the same endpoint by the guid alone.
        /// </summary>
        private static string EndpointKey(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            int split = id.LastIndexOf("}.", StringComparison.Ordinal);
            return split >= 0 ? id.Substring(split + 2) : id;
        }

        /// <summary>The file the preamp line lives in; null when the stage is absent.</summary>
        public static string BoostFile => _dir == null ? null : Path.Combine(_dir, "workspaceos-boost.txt");

        /// <summary>True when the audio engine is actually running the preamp.</summary>
        public static bool Available
        {
            get
            {
                Refresh();
                return _attached;
            }
        }

        /// <summary>
        /// True when EqualizerAPO is registered on the default output, whether or
        /// not the engine is loading it — the difference the panel reports.
        /// </summary>
        public static bool Registered
        {
            get
            {
                Refresh();
                return _registered;
            }
        }

        private static void Refresh()
        {
            _dir ??= FindConfigDir();
            if (DateTime.UtcNow - _checked < TimeSpan.FromSeconds(10)) return;
            _registered = _dir != null && IsRegistered();
            _attached = _registered && IsAttached();
            _checked = DateTime.UtcNow;
        }

        /// <summary>The volume slider's ceiling: 200 % with a stage (6 dB), 100 % without.</summary>
        public static double MaxPercent => Available ? 100.0 * Math.Pow(10, MaxDb / 20.0) : 100.0;

        /// <summary>Linear gain the preamp currently applies (1.0 = transparent).</summary>
        public static double Gain
        {
            get
            {
                _dir ??= FindConfigDir();
                if (_dir == null) return 1.0;
                double db = ReadDb();
                return db <= 0.01 ? 1.0 : Math.Pow(10, db / 20.0);
            }
        }

        /// <summary>Writes the preamp; 1.0 removes it from the path.</summary>
        public static void SetGain(double linear)
        {
            _dir ??= FindConfigDir();
            if (_dir == null) return;

            double db = linear <= 1.0001 ? 0 : Math.Min(MaxDb, 20 * Math.Log10(linear));
            try
            {
                File.WriteAllText(BoostFile,
                    "# Written by WorkspaceOS: the volume boost past 100 %.\r\n" +
                    "Preamp: " + db.ToString("0.0", CultureInfo.InvariantCulture) + " dB\r\n");
            }
            catch { /* a locked config must not break the volume keys */ }
        }

        private static string FindConfigDir()
        {
            foreach (var dir in ConfigDirs)
            {
                try
                {
                    if (File.Exists(Path.Combine(dir, "config.txt"))) return dir;
                }
                catch { }
            }
            return null;
        }

        private static double ReadDb()
        {
            try
            {
                foreach (var line in File.ReadAllLines(BoostFile))
                {
                    string text = line.Trim();
                    if (!text.StartsWith("Preamp:", StringComparison.OrdinalIgnoreCase)) continue;
                    text = text.Substring(7).Trim().Replace("dB", "", StringComparison.OrdinalIgnoreCase).Trim();
                    return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double db) ? db : 0;
                }
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Whether the default output's effect list contains EqualizerAPO. The key
        /// is readable (Windows only lets the audio service write it), which is
        /// enough to tell the truth about whether a boost would do anything.
        /// </summary>
        private static bool IsAttached()
        {
            string id = EndpointKey(CoreAudio.DefaultDeviceId());
            if (string.IsNullOrEmpty(id)) return false;

            // FxProperties is the list the audio engine builds its effect chain
            // from. EqualizerAPO's own device selector also records the CLSID
            // under Properties, which here happens without the engine picking it
            // up — trusting that copy would offer a boost that never reaches the
            // speakers, so only the effective list counts as available.
            try
            {
                string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\"
                              + id + @"\FxProperties";
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key?.GetValue(PremixKey) is not string[] list) return false;
                foreach (var entry in list)
                    if (string.Equals(entry, EapoSfx, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            return false;
        }

        /// <summary>Whether EqualizerAPO is recorded on the output at all.</summary>
        private static bool IsRegistered()
        {
            string id = EndpointKey(CoreAudio.DefaultDeviceId());
            if (string.IsNullOrEmpty(id)) return false;

            foreach (var store in new[] { "Properties", "FxProperties" })
            {
                try
                {
                    string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\"
                                  + id + @"\" + store;
                    using var key = Registry.LocalMachine.OpenSubKey(path);
                    if (key?.GetValue(PremixKey) is not string[] list) continue;
                    foreach (var entry in list)
                        if (string.Equals(entry, EapoSfx, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch { }
            }

            return false;
        }
    }
}
