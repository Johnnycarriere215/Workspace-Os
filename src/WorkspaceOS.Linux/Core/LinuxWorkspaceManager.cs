using System;
using System.Collections.Generic;
using System.Linq;
using WorkspaceOS.Core.Config;

namespace WorkspaceOS.Linux
{
    /// <summary>
    /// Linux counterpart of the Windows WorkspaceManager: tracks client
    /// windows, applies auto-assign rules, switches desktops. Uses the same
    /// WindowRule model (Match / MatchType / Workspace) as the Windows build.
    /// </summary>
    internal sealed class LinuxWorkspaceManager
    {
        private readonly ConfigService _configs;
        private readonly X11Backend _x;
        private readonly Dictionary<long, XWindow> _known = new();

        public LinuxWorkspaceManager(ConfigService configs, X11Backend x)
        {
            _configs = configs;
            _x = x;
        }

        public int WorkspaceCount => Math.Max(1, _configs.Config.Workspaces.Count);

        public IReadOnlyDictionary<long, XWindow> KnownWindows => _known;

        /// <summary>Refreshes the window index; returns all currently listed windows.</summary>
        public List<XWindow> Refresh()
        {
            var windows = _x.ListWindows();
            _known.Clear();
            foreach (var w in windows) _known[w.Id.ToInt64()] = w;
            return windows;
        }

        public XWindow Find(long hwnd) => _known.TryGetValue(hwnd, out var w) ? w : null;

        public void SwitchTo(int workspace)
        {
            int d = Math.Clamp(workspace - 1, 0, 35);
            if (_x.SwitchDesktop(d))
            {
                _configs.Config.General.ActiveWorkspace = workspace;
                _configs.Save();
            }
        }

        /// <summary>
        /// Sends the focused window to a workspace. Workspace 0 = pin to all
        /// (sticky on EWMH), matching the Windows "pin to all workspaces" rule.
        /// </summary>
        public bool SendFocusedToWorkspace(int workspace, bool follow)
        {
            var id = _x.FocusedWindow();
            if (id == IntPtr.Zero) return false;

            if (workspace <= 0)
                return _x.Sticky(id);
            return _x.SendToDesktop(id, Math.Clamp(workspace - 1, 0, 35), follow);
        }

        /// <summary>
        /// Auto-assign rule check, mirroring WorkspaceManager.MatchRules on
        /// Windows: first enabled rule whose pattern matches wins.
        /// </summary>
        public int? MatchRule(XWindow w)
        {
            foreach (var rule in _configs.Config.Rules)
            {
                if (rule == null || !rule.Enabled || string.IsNullOrWhiteSpace(rule.Match)) continue;
                string input = rule.MatchType switch
                {
                    "Title" => w.Title ?? "",
                    "Class" => w.WmClass ?? "",
                    "Process" => w.WmClass ?? "",
                    _ => w.WmClass ?? "",   // Executable default
                };
                bool matched = rule.MatchType == "Regex"
                    ? RegexMatches(w.WmClass, w.Title, rule.Match)
                    : input.Contains(rule.Match, StringComparison.OrdinalIgnoreCase);
                if (matched) return Math.Clamp(rule.Workspace, 0, WorkspaceCount);
            }
            return null;
        }

        private static bool RegexMatches(string wmClass, string title, string pattern)
        {
            try
            {
                var rx = new System.Text.RegularExpressions.Regex(
                    pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(50));
                return rx.IsMatch(wmClass ?? "") || rx.IsMatch(title ?? "");
            }
            catch { return false; }
        }

        /// <summary>Moves windows that match an auto-assign rule to their workspace.</summary>
        public void ApplyAutoAssignRules(IEnumerable<XWindow> windows)
        {
            foreach (var w in windows)
            {
                if (w.Desktop < 0) continue;    // sticky windows stay
                int? target = MatchRule(w);
                if (target is int t && t > 0 && w.Desktop != t - 1)
                    _x.SendToDesktop(w.Id, t - 1, activate: false);
            }
        }
    }
}
