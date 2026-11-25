using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Metrics;
using WorkspaceOS.Core.Shibumi;

namespace WorkspaceOS.UI
{
    /// <summary>
    /// Detail panels for every Shibumi bar widget, on Windows. One class so the
    /// panels share the same 8 px anchor gap, dismissal and refresh cadence; each
    /// case is the Windows backend named in <see cref="ShibumiSuite"/>.
    /// </summary>
    public static class ShibumiWidgetPanels
    {
        private static ShibumiPanelWindow _open;
        private static string _openKey;

        public static void Toggle(string key, FrameworkElement anchor, Window bar)
        {
            if (string.IsNullOrWhiteSpace(key)) return;

            bool same = string.Equals(_openKey, key, StringComparison.OrdinalIgnoreCase);
            Close();
            if (same) return;

            // Widgets that are not a panel of their own.
            switch (key)
            {
                case "ControlCenter":
                    ControlCenterWindow.Toggle();
                    return;
                case "Status":
                    Open("ms-settings:notifications");
                    return;
                case "Update":
                    Open("ms-settings:windowsupdate");
                    return;
                case "Workspaces":
                    return;   // the pills are the control
            }

            var panel = Build(key);
            if (panel == null) return;

            _open = panel;
            _openKey = key;
            panel.Closed += (_, _) =>
            {
                if (ReferenceEquals(_open, panel)) { _open = null; _openKey = null; }
            };
            panel.ShowUnder(anchor, bar);
        }

        public static void Close()
        {
            var panel = _open;
            _open = null;
            _openKey = null;
            if (panel == null) return;
            try { panel.Close(); } catch { }
        }

        /// <summary>Dev-only: renders one widget's panel to a PNG.</summary>
        public static bool Snapshot(string key, string path)
        {
            var panel = Build(key);
            if (panel == null) return false;
            bool ok = panel.SnapshotPanel(path);
            try { panel.Close(); } catch { }
            return ok;
        }

        private static ShibumiPanelWindow Build(string key)
        {
            var s = App.Metrics?.Latest ?? new SystemSnapshot();
            var plugin = ShibumiSuite.Find(key);
            string subtitle = plugin?.WidgetDescription ?? "";

            switch (key)
            {
                case "CPU": return Cpu(s, subtitle);
                case "RAM": return Memory(s, subtitle);
                case "GPU": return Gpu(s, subtitle);
                case "Disk": return Storage(s, subtitle);
                case "Temp": return Temperature(s, subtitle);
                case "Volume": return Volume(subtitle);
                case "Net": return Network(subtitle);
                case "Battery": return Battery(s, subtitle);
                case "Brightness": return Brightness(subtitle);
                case "Power": return PowerProfile(subtitle);
                case "Bluetooth": return Bluetooth(subtitle);
                case "Quick": return QuickAccess(subtitle);
                default: return null;
            }
        }

        private static ShibumiPanelWindow Cpu(SystemSnapshot s, string subtitle)
        {
            var panel = new ShibumiPanelWindow("CPU", subtitle, 286);
            var load = panel.Row("Load", $"{s.CpuPercent:0}%");
            panel.Meter(s.CpuPercent / 100.0);
            var temp = panel.Row("Temperature", s.CpuTempC > 0 ? $"{s.CpuTempC:0} °C" : "—");
            panel.Row("Processes", App.Metrics?.TopProcesses(1).Count.ToString() ?? "—");

            var heading = panel.Label("TOP PROCESSES", 9);
            heading.Margin = new Thickness(0, 10, 0, 4);
            panel.Add(heading);
            foreach (var process in App.Metrics?.TopProcesses(5) ?? new System.Collections.Generic.List<ProcessInfoRow>())
            {
                panel.Row(process.Name, $"{process.CpuPercent:0.0}%", line: false);
            }

            panel.Refresh += () =>
            {
                var latest = App.Metrics?.Latest;
                if (latest == null) return;
                ValueOf(load)!.Text = $"{latest.CpuPercent:0}%";
                ValueOf(temp)!.Text = latest.CpuTempC > 0 ? $"{latest.CpuTempC:0} °C" : "—";
            };
            return panel;
        }

        private static ShibumiPanelWindow Memory(SystemSnapshot s, string subtitle)
        {
            var panel = new ShibumiPanelWindow("Memory", subtitle, 286);
            double total = s.RamTotal / 1024.0 / 1024 / 1024;
            double used = (s.RamTotal - s.RamAvailable) / 1024.0 / 1024 / 1024;
            var usedRow = panel.Row("Used", $"{used:0.0} / {total:0.0} GB");
            panel.Meter(s.RamPercent / 100.0);
            var available = panel.Row("Available", $"{s.RamAvailable / 1024.0 / 1024 / 1024:0.0} GB");
            panel.Row("Load", $"{s.RamPercent:0}%");

            var heading = panel.Label("TOP PROCESSES", 9);
            heading.Margin = new Thickness(0, 10, 0, 4);
            panel.Add(heading);
            foreach (var process in App.Metrics?.TopProcesses(5) ?? new System.Collections.Generic.List<ProcessInfoRow>())
            {
                panel.Row(process.Name, $"{process.MemoryMB:0} MB", line: false);
            }

            panel.Refresh += () =>
            {
                var latest = App.Metrics?.Latest;
                if (latest == null) return;
                double t = latest.RamTotal / 1024.0 / 1024 / 1024;
                double u = (latest.RamTotal - latest.RamAvailable) / 1024.0 / 1024 / 1024;
                ValueOf(usedRow)!.Text = $"{u:0.0} / {t:0.0} GB";
                ValueOf(available)!.Text = $"{latest.RamAvailable / 1024.0 / 1024 / 1024:0.0} GB";
            };
            return panel;
        }

        private static ShibumiPanelWindow Gpu(SystemSnapshot s, string subtitle)
        {
            var panel = new ShibumiPanelWindow("GPU", subtitle, 286);
            var load = panel.Row("Load", s.GpuPercent >= 0 ? $"{s.GpuPercent:0}%" : "—");
            panel.Meter(s.GpuPercent >= 0 ? s.GpuPercent / 100.0 : 0);
            panel.Row("VRAM used", s.VramUsedMB >= 0 ? $"{s.VramUsedMB / 1024.0:0.0} GB" : "—");
            var temp = panel.Row("Temperature", s.GpuTempC > 0 ? $"{s.GpuTempC:0} °C" : "—");

            panel.Refresh += () =>
            {
                var latest = App.Metrics?.Latest;
                if (latest == null) return;
                ValueOf(load)!.Text = latest.GpuPercent >= 0 ? $"{latest.GpuPercent:0}%" : "—";
                ValueOf(temp)!.Text = latest.GpuTempC > 0 ? $"{latest.GpuTempC:0} °C" : "—";
            };
            return panel;
        }

        private static ShibumiPanelWindow Storage(SystemSnapshot s, string subtitle)
        {
            var panel = new ShibumiPanelWindow("Storage", subtitle, 286);
            double totalGb = s.DiskTotalBytes / 1024.0 / 1024 / 1024;
            double freeGb = s.DiskFreeBytes / 1024.0 / 1024 / 1024;
            panel.Row("Drive", "System");
            var used = panel.Row("Used", $"{(totalGb - freeGb):0} / {totalGb:0} GB");
            panel.Meter(totalGb > 0 ? (totalGb - freeGb) / totalGb : 0);
            panel.Row("Free", $"{freeGb:0} GB");
            var read = panel.Row("Read", Rate(s.DiskReadBps), line: false);

            panel.Refresh += () =>
            {
                var latest = App.Metrics?.Latest;
                if (latest == null) return;
                double t = latest.DiskTotalBytes / 1024.0 / 1024 / 1024;
                double f = latest.DiskFreeBytes / 1024.0 / 1024 / 1024;
                ValueOf(used)!.Text = $"{(t - f):0} / {t:0} GB";
                ValueOf(read)!.Text = Rate(latest.DiskReadBps);
            };
            return panel;
        }

        private static ShibumiPanelWindow Temperature(SystemSnapshot s, string subtitle)
        {
            var panel = new ShibumiPanelWindow("Temperature", subtitle, 286);
            var cpu = panel.Row("CPU", s.CpuTempC > 0 ? $"{s.CpuTempC:0} °C" : "—");
            var gpu = panel.Row("GPU", s.GpuTempC > 0 ? $"{s.GpuTempC:0} °C" : "—", line: false);
            var hint = panel.Label("Windows exposes thermal sensors only where the firmware publishes them.", 9.5);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 10, 0, 0);
            panel.Add(hint);
            panel.Refresh += () =>
            {
                var latest = App.Metrics?.Latest;
                if (latest == null) return;
                ValueOf(cpu)!.Text = latest.CpuTempC > 0 ? $"{latest.CpuTempC:0} °C" : "—";
                ValueOf(gpu)!.Text = latest.GpuTempC > 0 ? $"{latest.GpuTempC:0} °C" : "—";
            };
            return panel;
        }

        private static ShibumiPanelWindow Volume(string subtitle)
        {
            var panel = new ShibumiPanelWindow("Audio", subtitle, 296);

            double ceiling = CoreAudio.GetMaxPercent();          // 200 % with a gain stage, else 100
            bool boosted = ceiling > 100.5;
            double percent = Math.Clamp(CoreAudio.GetVolumePercent(), 0, ceiling);
            bool muted = CoreAudio.GetMasterMute();

            var state = panel.Row("Output", muted ? "Muted" : "Default device");
            void Say(string text) => ValueOf(state)!.Text = text;

            void Apply(double value)
            {
                CoreAudio.SetVolumePercent(value);
                if (!muted || value <= 0) return;
                CoreAudio.SetMasterMute(false);
                muted = false;
                Say("Default device");
            }

            // One slider for the whole range: 0–100 % is the endpoint itself and
            // everything above it is the preamp stage the output carries, so the
            // arrow keys walk straight through 100 % into boost.
            var volume = panel.CapsuleSlider("Volume", percent / ceiling,
                f => Apply(f * ceiling), describe: f => Level(f * ceiling));

            panel.Nudge = steps =>
            {
                double next = Math.Clamp(CoreAudio.GetVolumePercent() + steps, 0, ceiling);
                Apply(next);
                volume.Set(next / ceiling);
            };

            var caption = panel.Label(boosted
                ? "Above 100 % the preamp stage lifts this output past 0 dB."
                : GainStage.Registered
                    ? "EqualizerAPO is installed but the audio engine is not running it: this output stops at 100 %."
                    : "No gain stage is attached: this output stops at 100 %.", 9.5);
            caption.TextWrapping = TextWrapping.Wrap;
            caption.Margin = new Thickness(0, 2, 0, 0);
            panel.Add(caption);

            var buttons = panel.Buttons();
            var mute = panel.Button(muted ? "Unmute" : "Mute", () => { });
            mute.MouseLeftButtonUp += (_, _) =>
            {
                try
                {
                    muted = !CoreAudio.GetMasterMute();
                    CoreAudio.SetMasterMute(muted);
                    ValueOf(state)!.Text = muted ? "Muted" : "Default device";
                    mute.Child = new TextBlock
                    {
                        Text = muted ? "Unmute" : "Mute",
                        FontSize = 10.5,
                        Foreground = panel.Ink
                    };
                }
                catch { }
            };
            buttons.Children.Add(mute);
            buttons.Children.Add(panel.Button("Sound settings", () => Open("ms-settings:sound")));
            panel.Add(buttons);
            return panel;
        }

        /// <summary>The level as it is actually applied: percent, plus dB above 100 %.</summary>
        private static string Level(double percent) =>
            percent <= 100.5 ? $"{percent:0}%" : $"{percent:0}%  +{20 * Math.Log10(percent / 100.0):0.0} dB";

        private static ShibumiPanelWindow Network(string subtitle)
        {
            var panel = new ShibumiPanelWindow("Network", subtitle, 300);
            var s = App.Metrics?.Latest ?? new SystemSnapshot();

            var name = panel.Row("Interface", PlatformInfo.PrimaryNetwork());
            var down = panel.Row("Download", Rate(s.NetDownBps));
            var up = panel.Row("Upload", Rate(s.NetUpBps), line: false);

            var buttons = panel.Buttons();
            buttons.Children.Add(panel.Button("Network settings", () => Open("ms-settings:network")));
            buttons.Children.Add(panel.Button("Wi-Fi", () => Open("ms-settings:network-wifi")));
            panel.Add(buttons);

            panel.Refresh += () =>
            {
                var latest = App.Metrics?.Latest;
                if (latest == null) return;
                ValueOf(name)!.Text = PlatformInfo.PrimaryNetwork();
                ValueOf(down)!.Text = Rate(latest.NetDownBps);
                ValueOf(up)!.Text = Rate(latest.NetUpBps);
            };
            return panel;
        }

        private static ShibumiPanelWindow Battery(SystemSnapshot s, string subtitle)
        {
            var panel = new ShibumiPanelWindow("Battery", subtitle, 286);
            if (s.BatteryPercent < 0)
            {
                panel.Add(panel.Label("No battery on this machine.", 10.5));
                return panel;
            }

            var charge = panel.Row("Charge", $"{s.BatteryPercent}%");
            panel.Meter(s.BatteryPercent / 100.0);
            var power = panel.Row("Power", s.OnAc ? "On AC" : "Discharging", line: false);

            var buttons = panel.Buttons();
            buttons.Children.Add(panel.Button("Power settings", () => Open("ms-settings:batterysaver")));
            panel.Add(buttons);

            panel.Refresh += () =>
            {
                var latest = App.Metrics?.Latest;
                if (latest == null || latest.BatteryPercent < 0) return;
                ValueOf(charge)!.Text = $"{latest.BatteryPercent}%";
                ValueOf(power)!.Text = latest.OnAc ? "On AC" : "Discharging";
            };
            return panel;
        }

        private static ShibumiPanelWindow Brightness(string subtitle)
        {
            var panel = new ShibumiPanelWindow("Brightness", subtitle, 286);
            if (!PlatformInfo.HasBrightness)
            {
                panel.Add(panel.Label("This display does not expose a brightness control.", 10.5));
                var settings = panel.Buttons();
                settings.Children.Add(panel.Button("Display settings", () => Open("ms-settings:display")));
                panel.Add(settings);
                return panel;
            }

            int current = PlatformInfo.Brightness;
            panel.Slider("Display", current / 100.0, value => PlatformInfo.SetBrightness((int)Math.Round(value * 100)));

            var presets = panel.Buttons();
            foreach (int percent in new[] { 25, 50, 75, 100 })
            {
                int target = percent;
                presets.Children.Add(panel.Button($"{percent}%", () => PlatformInfo.SetBrightness(target)));
            }
            panel.Add(presets);
            return panel;
        }

        private static ShibumiPanelWindow PowerProfile(string subtitle)
        {
            var panel = new ShibumiPanelWindow("Power profile", subtitle, 300);
            var schemes = PlatformInfo.PowerSchemes();
            if (schemes.Count == 0)
            {
                panel.Add(panel.Label("powercfg did not report any power schemes.", 10.5));
                return panel;
            }

            panel.Row("Active", schemes.FirstOrDefault(p => p.Active)?.Name ?? "—", line: false);
            var list = panel.Buttons();
            foreach (var scheme in schemes)
            {
                var item = scheme;
                var button = panel.Button(scheme.Name, () =>
                {
                    PlatformInfo.SetPowerScheme(item.Guid);
                    panel.Close();
                }, primary: scheme.Active);
                list.Children.Add(button);
            }
            panel.Add(list);
            return panel;
        }

        private static ShibumiPanelWindow Bluetooth(string subtitle)
        {
            var panel = new ShibumiPanelWindow("Bluetooth", subtitle, 296);
            string radio = PlatformInfo.BluetoothRadio();
            panel.Row("Radio", string.IsNullOrEmpty(radio) ? "Not found" : radio);
            var hint = panel.Label("Windows keeps device pairing in Settings; the panel opens it directly.", 9.5);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 8, 0, 0);
            panel.Add(hint);

            var buttons = panel.Buttons();
            buttons.Children.Add(panel.Button("Bluetooth settings", () => Open("ms-settings:bluetooth")));
            panel.Add(buttons);
            return panel;
        }

        private static ShibumiPanelWindow QuickAccess(string subtitle)
        {
            var panel = new ShibumiPanelWindow("Quick access", subtitle, 300);
            var appearance = App.Configs.Config.Appearance;
            var theme = Core.Themes.ThemeLibrary.Find(appearance.BarTheme);
            panel.Row("Theme", theme?.Name ?? appearance.BarTheme);

            var themes = panel.Buttons();
            themes.Children.Add(panel.Button("Next theme", () => App.CycleTheme(1), primary: true));
            themes.Children.Add(panel.Button("Re-apply wallpaper", () =>
                Core.Themes.ThemeApplication.Apply(App.Configs.Config, forceWallpaper: true)));
            panel.Add(themes);

            var actions = panel.Buttons();
            actions.Children.Add(panel.Button("Settings", SettingsWindow.ShowSettings));
            actions.Children.Add(panel.Button("Task Manager", MonitorWindow.OpenTaskManager));
            actions.Children.Add(panel.Button("Display", () => Open("ms-settings:display")));
            actions.Children.Add(panel.Button("Personalisation", () => Open("ms-settings:personalization")));
            panel.Add(actions);
            return panel;
        }

        // ---- helpers -------------------------------------------------------

        private static TextBlock ValueOf(Grid row) =>
            row?.Children.Count > 1 ? row.Children[1] as TextBlock : null;

        private static string Rate(double bytesPerSecond)
        {
            if (bytesPerSecond < 0) return "—";
            if (bytesPerSecond >= 1024 * 1024) return $"{bytesPerSecond / 1024 / 1024:0.0} MB/s";
            if (bytesPerSecond >= 1024) return $"{bytesPerSecond / 1024:0} KB/s";
            return $"{bytesPerSecond:0} B/s";
        }

        private static void Open(string uri)
        {
            try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
            catch { }
        }
    }
}
