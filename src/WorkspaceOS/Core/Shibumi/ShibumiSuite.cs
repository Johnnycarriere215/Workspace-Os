using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WorkspaceOS.Core.Config;

namespace WorkspaceOS.Core.Shibumi
{
    /// <summary>One Shibumi plugin from the suite's own manifests.</summary>
    public sealed class ShibumiPlugin
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Version { get; init; } = "";
        public string Description { get; init; } = "";
        public string Role { get; init; } = "";
        public string Widget { get; init; } = "";
        public string WidgetDescription { get; init; } = "";
        public string Category { get; init; } = "";
        public string Section { get; init; } = "left";
        public IReadOnlyList<string> Kinds { get; init; } = Array.Empty<string>();

        /// <summary>Bar module key this plugin maps to on Windows (empty = no widget).</summary>
        public string Key { get; internal set; } = "";

        /// <summary>Windows API/backend that fulfils the plugin here.</summary>
        public string Backend { get; internal set; } = "";

        /// <summary>False when this machine cannot supply the plugin's data.</summary>
        public bool Available { get; internal set; } = true;

        public string DisplayName =>
            string.IsNullOrWhiteSpace(Widget) ? Name : Widget;
    }

    /// <summary>
    /// The Shibumi suite, on Windows.
    ///
    /// The catalogue is generated from the plugin manifests in the Shibumi-Shell
    /// fork (<c>Assets/shibumi/plugins.json</c>) — same ids, roles G1–G18, sections
    /// and descriptions — so this is the suite's own model rather than an
    /// imitation of its look. Each plugin is then bound to a Windows backend:
    /// G2 to the native virtual desktops, G4/G5/G16/G18 to the metrics service,
    /// G6 to Core Audio, G11 to Win32 networking, G12 to the power status API,
    /// G13 to WMI brightness, G14 to powercfg, G15 to the Bluetooth radio API,
    /// G17 to the system drive, G1 to the WorkspaceOS control centre.
    ///
    /// Plugins Windows cannot provide (the AI usage providers, Omarchy's own
    /// update channel, the Wayland MPRIS bridge) stay in the catalogue with
    /// <see cref="ShibumiPlugin.Available"/> false, so the Shibumi settings page
    /// tells the truth instead of silently dropping half the suite.
    /// </summary>
    public static class ShibumiSuite
    {
        private static readonly object Gate = new();
        private static List<ShibumiPlugin> _all;

        public static IReadOnlyList<ShibumiPlugin> All
        {
            get
            {
                lock (Gate)
                {
                    if (_all == null) _all = Load();
                    return _all;
                }
            }
        }

        public static int Count => All.Count;

        public static ShibumiPlugin Find(string idOrRoleOrKey)
        {
            if (string.IsNullOrWhiteSpace(idOrRoleOrKey)) return null;
            return All.FirstOrDefault(p =>
                string.Equals(p.Id, idOrRoleOrKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Role, idOrRoleOrKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Key, idOrRoleOrKey, StringComparison.OrdinalIgnoreCase));
        }

        public static IEnumerable<ShibumiPlugin> Widgets =>
            All.Where(p => !string.IsNullOrWhiteSpace(p.Key));

        /// <summary>Section for a plugin: the user's override wins over the manifest.</summary>
        public static string SectionFor(ShibumiPlugin plugin, ShibumiConfig cfg)
        {
            if (plugin == null) return "left";
            if (cfg?.Sections != null &&
                cfg.Sections.TryGetValue(plugin.Id, out var section) &&
                !string.IsNullOrWhiteSpace(section))
                return section.Trim().ToLowerInvariant();
            if (cfg?.Sections != null &&
                cfg.Sections.TryGetValue(plugin.Key, out var byKey) &&
                !string.IsNullOrWhiteSpace(byKey))
                return byKey.Trim().ToLowerInvariant();
            return string.IsNullOrWhiteSpace(plugin.Section) ? "left" : plugin.Section;
        }

        /// <summary>True when the user hid this widget from the bar.</summary>
        public static bool IsHidden(ShibumiPlugin plugin, ShibumiConfig cfg)
        {
            if (plugin == null || cfg?.HiddenWidgets == null) return false;
            return cfg.HiddenWidgets.Any(id =>
                string.Equals(id, plugin.Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, plugin.Key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, plugin.Role, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Bar module keys for a section, in suite order.</summary>
        public static List<string> KeysFor(string section, ShibumiConfig cfg)
        {
            return Widgets
                .Where(p => string.Equals(SectionFor(p, cfg), section, StringComparison.OrdinalIgnoreCase))
                .Where(p => !IsHidden(p, cfg) && p.Available)
                .Select(p => p.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static void Reload()
        {
            lock (Gate) _all = null;
        }

        private static List<ShibumiPlugin> Load()
        {
            var list = ReadManifestCatalogue() ?? Fallback();
            Bind(list);
            return list
                .OrderBy(p => RoleOrder(p.Role))
                .ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static int RoleOrder(string role)
        {
            if (!string.IsNullOrWhiteSpace(role) && role.Length > 1 && role[0] == 'G'
                && int.TryParse(role.Substring(1), out int n))
                return n;
            return 100;
        }

        private static List<ShibumiPlugin> ReadManifestCatalogue()
        {
            try
            {
                // Prefer a loose catalogue (development, a rebuilt portable copy),
                // then the copy embedded in the exe so an installed build always
                // carries the suite it was built from.
                string path = Path.Combine(AppContext.BaseDirectory, "Assets", "shibumi", "plugins.json");
                string json = null;
                if (File.Exists(path))
                {
                    json = File.ReadAllText(path);
                }
                else
                {
                    using var resource = typeof(ShibumiSuite).Assembly
                        .GetManifestResourceStream("WorkspaceOS.Assets.shibumi.plugins.json");
                    if (resource == null) return null;
                    using var reader = new StreamReader(resource);
                    json = reader.ReadToEnd();
                }

                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty("plugins", out var plugins)
                    || plugins.ValueKind != JsonValueKind.Array) return null;

                var list = new List<ShibumiPlugin>();
                foreach (var entry in plugins.EnumerateArray())
                {
                    var kinds = new List<string>();
                    if (entry.TryGetProperty("kinds", out var kindsElement)
                        && kindsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var kind in kindsElement.EnumerateArray())
                            kinds.Add(kind.GetString() ?? "");
                    }

                    list.Add(new ShibumiPlugin
                    {
                        Id = Text(entry, "id"),
                        Name = Text(entry, "name"),
                        Version = Text(entry, "version"),
                        Description = Text(entry, "description"),
                        Role = Text(entry, "role"),
                        Widget = Text(entry, "widget"),
                        WidgetDescription = Text(entry, "widgetDescription"),
                        Category = Text(entry, "category"),
                        Section = Text(entry, "section") is { Length: > 0 } section ? section : "left",
                        Kinds = kinds
                    });
                }
                return list.Count > 0 ? list : null;
            }
            catch { return null; }
        }

        private static string Text(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value)) return "";
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        }

        /// <summary>
        /// Windows mapping. Keys match the bar's module names; the backend string is
        /// what the Shibumi settings page shows under each plugin.
        /// </summary>
        private static void Bind(List<ShibumiPlugin> list)
        {
            foreach (var plugin in list)
            {
                switch (plugin.Id)
                {
                    case "hancore.shibumi.control-center":
                        plugin.Key = "ControlCenter"; plugin.Backend = "WorkspaceOS control centre"; break;
                    case "hancore.shibumi.workspaces":
                        plugin.Key = "Workspaces"; plugin.Backend = "Windows virtual desktops"; break;
                    case "hancore.shibumi.status":
                        plugin.Key = "Status"; plugin.Backend = "Windows notification area"; break;
                    case "hancore.shibumi.memory":
                        plugin.Key = "RAM"; plugin.Backend = "Global memory status"; break;
                    case "hancore.shibumi.cpu":
                        plugin.Key = "CPU"; plugin.Backend = "System processor times"; break;
                    case "hancore.shibumi.audio":
                        plugin.Key = "Volume"; plugin.Backend = "Core Audio (WASAPI)"; break;
                    case "hancore.shibumi.ai":
                        plugin.Key = "AI"; plugin.Backend = "Not available on Windows"; plugin.Available = false; break;
                    case "hancore.shibumi.center":
                        plugin.Key = "Clock"; plugin.Backend = "Windows clock + system status"; break;
                    case "hancore.shibumi.media":
                        plugin.Key = "Media"; plugin.Backend = "System Media Transport Controls"; break;
                    case "hancore.shibumi.quick-access":
                        plugin.Key = "Quick"; plugin.Backend = "Themes, wallpaper, lock, settings"; break;
                    case "hancore.shibumi.network":
                        plugin.Key = "Net"; plugin.Backend = "Win32 network interfaces"; break;
                    case "hancore.shibumi.battery":
                        plugin.Key = "Battery"; plugin.Backend = "GetSystemPowerStatus"; break;
                    case "hancore.shibumi.brightness":
                        plugin.Key = "Brightness"; plugin.Backend = "WMI monitor brightness"; break;
                    case "hancore.shibumi.power-profile":
                        plugin.Key = "Power"; plugin.Backend = "powercfg power schemes"; break;
                    case "hancore.shibumi.bluetooth":
                        plugin.Key = "Bluetooth"; plugin.Backend = "Bluetooth radio API"; break;
                    case "hancore.shibumi.temperature":
                        plugin.Key = "Temp"; plugin.Backend = "WMI thermal zone"; break;
                    case "hancore.shibumi.gpu":
                        plugin.Key = "GPU"; plugin.Backend = "GPU engine performance counters"; break;
                    case "hancore.shibumi.storage":
                        plugin.Key = "Disk"; plugin.Backend = "System drive free space"; break;
                    case "hancore.shibumi.update-center":
                        plugin.Key = "Update"; plugin.Backend = "Windows Update"; break;
                    default:
                        // state / reactor / power-state are services, not widgets.
                        plugin.Key = "";
                        plugin.Backend = plugin.Kinds.Contains("service") ? "Suite service" : "Host component";
                        break;
                }
            }
        }

        /// <summary>
        /// Compile-time copy of the suite's bar plugins. Used only when the JSON
        /// asset is missing (tests, a stripped portable layout), so the bar never
        /// silently loses its widgets.
        /// </summary>
        private static List<ShibumiPlugin> Fallback()
        {
            ShibumiPlugin P(string role, string id, string name, string widget, string section,
                string description) => new()
            {
                Role = role, Id = id, Name = name, Widget = widget, Section = section,
                Description = description, Version = "0.2.5"
            };

            return new List<ShibumiPlugin>
            {
                P("G1","hancore.shibumi.control-center","Shibumi Control Center","Shibumi Control Center","left","The Shibumi control centre."),
                P("G2","hancore.shibumi.workspaces","Shibumi Workspaces","Shibumi workspaces","left","Workspace pills and panel."),
                P("G3","hancore.shibumi.status","Shibumi Status","Shibumi status","left","Tray and notification status."),
                P("G4","hancore.shibumi.memory","Shibumi Memory","Shibumi memory","left","Memory load and detail panel."),
                P("G5","hancore.shibumi.cpu","Shibumi CPU","Shibumi CPU","left","Processor load and detail panel."),
                P("G6","hancore.shibumi.audio","Shibumi Audio","Shibumi audio","left","Volume, mute and mixer."),
                P("G7","hancore.shibumi.ai","Shibumi AI Usage","Shibumi AI usage","left","AI provider usage."),
                P("G8","hancore.shibumi.center","Shibumi Center","Shibumi center","center","Clock, date and status indicators."),
                P("G9","hancore.shibumi.media","Shibumi Media","Shibumi media","right","Media transport and spectrum."),
                P("G10","hancore.shibumi.quick-access","Shibumi Quick Access","Shibumi quick access","right","Theme, wallpaper and quick tools."),
                P("G11","hancore.shibumi.network","Shibumi Network","Shibumi network","right","Network state and details."),
                P("G12","hancore.shibumi.battery","Shibumi Battery","Shibumi battery","right","Battery level and health."),
                P("G13","hancore.shibumi.brightness","Shibumi Brightness","Shibumi brightness","right","Display brightness."),
                P("G14","hancore.shibumi.power-profile","Shibumi Power Profile","Shibumi power profile","right","Power scheme."),
                P("G15","hancore.shibumi.bluetooth","Shibumi Bluetooth","Shibumi Bluetooth","right","Bluetooth radio and devices."),
                P("G16","hancore.shibumi.temperature","Shibumi Temperature","Shibumi temperature","right","Thermal sensors."),
                P("G17","hancore.shibumi.gpu","Shibumi GPU","Shibumi GPU","right","GPU load and VRAM."),
                P("G18","hancore.shibumi.storage","Shibumi Storage","Shibumi storage","right","Disk usage."),
                P("","hancore.shibumi.update-center","Shibumi Update Center","Shibumi update center","right","Windows Update."),
                P("","hancore.shibumi.state","Shibumi State","","","Configuration and palette state."),
                P("","hancore.shibumi.reactor","Shibumi Reactor","","","Event and quote service."),
                P("","hancore.shibumi.power-state","Shibumi Power State","","","Shared battery and power state.")
            };
        }
    }
}
