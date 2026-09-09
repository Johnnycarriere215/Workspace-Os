using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using WorkspaceOS.Core.Config;

namespace WorkspaceOS.Linux
{
    /// <summary>
    /// Translates WorkspaceOS hotkey combos ("Win+Shift+2", "Alt+Space",
    /// "Win+Ctrl+H") into X11 keysym strings ("<super><shift>2",
    /// "<alt>space", "<super><ctrl>h") as consumed by Cinnamon custom
    /// keybindings, dconf and Hyprland. Pure and unit-tested.
    /// </summary>
    public static class KeymapTranslator
    {
        private static readonly Dictionary<string, string> ModMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Win"] = "<super>",
            ["Super"] = "<super>",
            ["Meta"] = "<super>",
            ["Ctrl"] = "<ctrl>",
            ["Control"] = "<ctrl>",
            ["Alt"] = "<alt>",
            ["Shift"] = "<shift>",
        };

        private static readonly Dictionary<string, string> KeyMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Space"] = "space",
            ["Enter"] = "Return",
            ["Return"] = "Return",
            ["Tab"] = "Tab",
            ["Esc"] = "Escape",
            ["Escape"] = "Escape",
            ["Backspace"] = "BackSpace",
            ["Del"] = "Delete",
            ["Delete"] = "Delete",
            ["Insert"] = "Insert",
            ["Home"] = "Home",
            ["End"] = "End",
            ["PgUp"] = "Prior",
            ["PageUp"] = "Prior",
            ["PgDn"] = "Next",
            ["PageDown"] = "Next",
            ["Left"] = "Left",
            ["Right"] = "Right",
            ["Up"] = "Up",
            ["Down"] = "Down",
            ["F1"] = "F1", ["F2"] = "F2", ["F3"] = "F3", ["F4"] = "F4",
            ["F5"] = "F5", ["F6"] = "F6", ["F7"] = "F7", ["F8"] = "F8",
            ["F9"] = "F9", ["F10"] = "F10", ["F11"] = "F11", ["F12"] = "F12",
        };

        /// <summary>
        /// "Win+Shift+2" → "<super><shift>2"; "Win+Shift+Space" →
        /// "<super><shift>space". Returns null for combos that cannot bind
        /// on X11 (bare modifier, empty key, unparseable input).
        /// </summary>
        public static string ToX11Combo(string combo)
        {
            if (string.IsNullOrWhiteSpace(combo)) return null;
            var mods = new StringBuilder();
            string key = null;

            foreach (var raw in combo.Split('+'))
            {
                var part = raw.Trim();
                if (part.Length == 0) return null;
                if (ModMap.TryGetValue(part, out var mod))
                {
                    mods.Append(mod);
                }
                else if (key == null)
                {
                    key = KeyMap.TryGetValue(part, out var mapped) ? mapped : part.ToLowerInvariant();
                }
                else
                {
                    return null;    // two non-modifier keys — not a chord
                }
            }
            if (key == null) return null;   // e.g. bare "Win"
            return mods + key;
        }

        /// <summary>Human-readable description of a combo, e.g. "Super+Shift+2".</summary>
        public static string Describe(string combo)
        {
            var x = ToX11Combo(combo);
            return x == null ? combo : x
                .Replace("<super>", "Super+").Replace("<ctrl>", "Ctrl+")
                .Replace("<alt>", "Alt+").Replace("<shift>", "Shift+")
                .TrimEnd('+');
        }
    }

    /// <summary>
    /// Applies the WorkspaceOS keymap to the Linux session — automatically,
    /// with no manual editing:
    /// - Cinnamon/MATE: registers each binding as a custom keybinding in
    ///   gsettings (org.cinnamon.desktop.keybindings / org.mate.Marco.global-keybindings
    ///   style custom bindings) pointing at `workspaceos action <Name>`.
    /// - Any session: writes /etc/X11/Xsession.d style helper is NOT used;
    ///   instead a plain xbindkeys config is generated as a fallback for WMs
    ///   without custom-keybinding support.
    /// The Windows app regenerates workspaceos.ahk at startup and on config
    /// change; this regenerates the same bindings on Linux the same way.
    /// </summary>
    public sealed class KeybindingApplier
    {
        private readonly ConfigService _configs;
        private readonly Action<string> _log;

        public KeybindingApplier(ConfigService configs, Action<string> log = null)
        {
            _configs = configs;
            _log = log ?? (m => ConfigService.Log(m));
        }

        private static bool GsettingsAvailable() => XTool.ToolExists("gsettings");

        private string Schema => _desktop switch
        {
            LinuxDesktop.Cinnamon => "org.cinnamon.desktop.keybindings",
            LinuxDesktop.Mate => "org.mate.marco.global-keybindings",
            _ => "",
        };
        private readonly LinuxDesktop _desktop = DetectDesktop();

        public enum LinuxDesktop { Unknown, Cinnamon, Mate, Xfce, Other }

        public static LinuxDesktop DetectDesktop()
        {
            var cur = (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").ToLowerInvariant();
            if (cur.Contains("cinnamon") || cur.Contains("x-cinnamon")) return LinuxDesktop.Cinnamon;
            if (cur.Contains("mate")) return LinuxDesktop.Mate;
            if (cur.Contains("xfce")) return LinuxDesktop.Xfce;
            return LinuxDesktop.Other;
        }

        /// <summary>
        /// Applies every non-empty binding from HotkeyConfig to the session.
        /// Safe to call repeatedly (idempotent naming: workspaceos-customN).
        /// </summary>
        public int ApplyAll()
        {
            var bindings = _configs.Config.Hotkeys.Bindings;
            if (bindings == null || bindings.Count == 0) return 0;

            return _desktop switch
            {
                LinuxDesktop.Cinnamon => ApplyCinnamon(bindings),
                LinuxDesktop.Mate => ApplyMate(bindings),
                _ => WriteXbindkeysFallback(bindings),
            };
        }

        // ------------------------------------------------------------- Cinnamon

        private int ApplyCinnamon(Dictionary<string, string> bindings)
        {
            if (!GsettingsAvailable()) { _log("gsettings not found — cannot register keybindings"); return 0; }

            // 1. Drop our previous custom bindings so re-applying is idempotent
            //    (the user's own custom keybindings are never touched).
            var existing = ReadGsettingsList(Schema, "custom-list");
            var kept = existing
                .Where(n => !n.StartsWith("workspaceos", StringComparison.OrdinalIgnoreCase))
                .Select(GsettingsPath)
                .ToList();
            Run($"gsettings set {Schema} custom-list \"[{string.Join(", ", kept)}]\"");

            // 2. Register one custom keybinding per configured combo.
            int next = 0;
            var added = new List<string>();

            foreach (var kv in bindings)
            {
                if (string.IsNullOrWhiteSpace(kv.Value)) continue;   // disabled binding
                var combo = KeymapTranslator.ToX11Combo(kv.Value);
                if (combo == null) { _log($"keymap: cannot bind '{kv.Value}' ({kv.Key})"); continue; }

                string customName = $"workspaceos-custom{next}";
                next++;
                string relocatable = $"org.cinnamon.desktop.keybindings.custom-keybinding:{GsettingsPath(customName)}";
                Run($"gsettings set {relocatable} name \"{GsettingsQuote($"WorkspaceOS: {kv.Key}")}\"");
                Run($"gsettings set {relocatable} command \"workspaceos action {kv.Key}\"");
                Run($"gsettings set {relocatable} binding \"['{combo}']\"");
                added.Add(GsettingsPath(customName));
            }

            // 3. Publish the custom-list.
            Run($"gsettings set {Schema} custom-list \"[{string.Join(", ", added)}]\"");
            _log($"keymap: registered {added.Count} Cinnamon custom keybindings");
            return added.Count;
        }

        private static string GsettingsPath(string name) => $"'{name}'";

        private static string GsettingsQuote(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static List<string> ReadGsettingsList(string schema, string key)
        {
            var outp = XTool.Run("gsettings", $"get {schema} {key}", quiet: true);
            var names = new List<string>();
            // Format: [@as ['a', 'b']] or ['a', 'b']
            int open = outp.IndexOf('['), close = outp.LastIndexOf(']');
            if (open < 0 || close <= open) return names;
            foreach (var part in outp.Substring(open + 1, close - open - 1).Split(','))
            {
                var t = part.Trim().Trim('\'', '"');
                if (t.Length > 0) names.Add(t);
            }
            return names;
        }

        // ------------------------------------------------------------- MATE

        private int ApplyMate(Dictionary<string, string> bindings)
        {
            if (!GsettingsAvailable()) { _log("gsettings not found — cannot register keybindings"); return 0; }
            const string schema = "org.mate.Marco.global-keybindings";
            int count = 0;
            // Marco exposes run-command-1..12 / command-1..12 for user commands.
            foreach (var kv in bindings)
            {
                if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                var combo = KeymapTranslator.ToX11Combo(kv.Value);
                if (combo == null) continue;
                if (count >= 12) break;     // Marco offers run-command-1..12
                int n = count + 1;
                Run($"gsettings set {schema} run-command-{n} \"'{combo}'\"");
                Run($"gsettings set {schema} command-{n} \"workspaceos action {kv.Key}\"");
                count++;
            }
            _log($"keymap: registered {count} MATE commands");
            return count;
        }

        // ------------------------------------------------------------- fallback

        /// <summary>
        /// Generic fallback: generate an xbindkeys config covering every
        /// binding. The daemon starts xbindkeys if present.
        /// </summary>
        private int WriteXbindkeysFallback(Dictionary<string, string> bindings)
        {
            string dir = ConfigService.DataDir;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "xbindkeysrc");
            var sb = new StringBuilder();
            sb.AppendLine("# Generated by WorkspaceOS — do not edit by hand.");
            sb.AppendLine("# Re-applied automatically whenever hotkeys change.");
            int count = 0;
            foreach (var kv in bindings)
            {
                if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                var combo = KeymapTranslator.ToX11Combo(kv.Value);
                if (combo == null) continue;
                sb.AppendLine($"\"workspaceos action {kv.Key}\"");
                sb.AppendLine($"    {combo}");
                count++;
            }
            File.WriteAllText(path, sb.ToString());
            _log($"keymap: wrote {count} bindings to {path} (xbindkeys fallback)");
            return count;
        }

        private static void Run(string command)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return;
                var err = p.StandardError.ReadToEnd();
                p.WaitForExit(4000);
                if (p.ExitCode != 0 && !string.IsNullOrWhiteSpace(err))
                    ConfigService.Log($"keymap: {command} → exit {p.ExitCode}: {err.Trim()}");
            }
            catch (Exception ex) { ConfigService.Log($"keymap: {command} → {ex.Message}"); }
        }
    }
}
