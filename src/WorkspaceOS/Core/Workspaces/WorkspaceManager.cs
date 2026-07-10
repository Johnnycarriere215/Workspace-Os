using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Interop;
using WorkspaceOS.Core.WindowSystem;

namespace WorkspaceOS.Core.Workspaces
{
    /// <summary>
    /// Linux-style workspaces WITHOUT tiling. Each managed top-level window is
    /// assigned to a workspace; switching hides the windows of other workspaces
    /// and shows the active one, leaving geometry untouched.
    ///
    /// Safety: every window we hide is journaled to disk. If WorkspaceOS crashes
    /// or is killed, the next start (or the rescue tool) restores all windows.
    /// </summary>
    public class WorkspaceManager : IDisposable
    {
        private readonly ConfigService _configService;
        private readonly Dictionary<IntPtr, int> _windowWorkspace = new(); // hwnd -> workspace (0 = all/pinned)
        private readonly HashSet<IntPtr> _hiddenByUs = new();
        private NativeMethods.WinEventDelegate _winEventProc; // keep delegate alive
        private readonly List<IntPtr> _hooks = new();
        private System.Windows.Threading.DispatcherTimer _sweepTimer;

        public int ActiveWorkspace { get; private set; } = 1;
        public event Action<int> ActiveWorkspaceChanged;

        private static string JournalPath => Path.Combine(ConfigService.DataDir, "hidden-windows.json");
        private static string SessionPath => Path.Combine(ConfigService.DataDir, "session.json");

        public WorkspaceManager(ConfigService configService)
        {
            _configService = configService;
        }

        public void Initialize()
        {
            // Crash recovery: if a previous session left windows hidden, unhide them.
            RecoverOrphanedWindows();

            ActiveWorkspace = Math.Max(1, _configService.Config.General.ActiveWorkspace);

            // Adopt all current windows into the active workspace (or per rules).
            foreach (var w in WindowTracker.EnumerateManageable())
                AssignByRulesOrDefault(w, ActiveWorkspace);

            if (_configService.Config.General.RestoreWorkspacesOnStart)
                RestoreSession();

            // Watch for new/destroyed windows.
            _winEventProc = OnWinEvent;
            _hooks.Add(NativeMethods.SetWinEventHook(NativeMethods.EVENT_OBJECT_SHOW, NativeMethods.EVENT_OBJECT_SHOW,
                IntPtr.Zero, _winEventProc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT));
            _hooks.Add(NativeMethods.SetWinEventHook(NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.EVENT_OBJECT_DESTROY,
                IntPtr.Zero, _winEventProc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT));

            // Periodic sweep: adopt windows missed by events, drop dead handles.
            _sweepTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _sweepTimer.Tick += (_, _) => Sweep();
            _sweepTimer.Start();
        }

        private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != 0 || hwnd == IntPtr.Zero) return; // OBJID_WINDOW only
            if (eventType == NativeMethods.EVENT_OBJECT_DESTROY)
            {
                _windowWorkspace.Remove(hwnd);
                if (_hiddenByUs.Remove(hwnd)) SaveJournal();
                return;
            }
            if (eventType == NativeMethods.EVENT_OBJECT_SHOW)
            {
                if (_windowWorkspace.ContainsKey(hwnd) || _hiddenByUs.Contains(hwnd)) return;
                if (!WindowTracker.IsManageable(hwnd)) return;
                var info = WindowTracker.GetInfo(hwnd);
                int ws = AssignByRulesOrDefault(info, ActiveWorkspace);
                // If a rule sends it elsewhere, hide it now.
                if (ws != 0 && ws != ActiveWorkspace)
                    HideWindow(hwnd);
                App.FocusServiceInstance?.OnWindowShown(info);
            }
        }

        /// <summary>Applies window rules; returns assigned workspace (0 = pinned to all).</summary>
        private int AssignByRulesOrDefault(TrackedWindow w, int fallback)
        {
            int ws = MatchRules(w) ?? fallback;
            _windowWorkspace[w.Hwnd] = ws;
            return ws;
        }

        private int? MatchRules(TrackedWindow w)
        {
            foreach (var rule in _configService.Config.Rules)
            {
                if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Match)) continue;
                bool hit = rule.MatchType switch
                {
                    "Executable" => w.ExeName.Equals(rule.Match, StringComparison.OrdinalIgnoreCase)
                                    || w.ExePath.EndsWith(rule.Match, StringComparison.OrdinalIgnoreCase),
                    "Process" => Path.GetFileNameWithoutExtension(w.ExeName).Equals(
                                    Path.GetFileNameWithoutExtension(rule.Match), StringComparison.OrdinalIgnoreCase),
                    "Title" => w.Title.Contains(rule.Match, StringComparison.OrdinalIgnoreCase),
                    "Class" => w.ClassName.Equals(rule.Match, StringComparison.OrdinalIgnoreCase),
                    "Regex" => SafeRegex(rule.Match, w.Title) || SafeRegex(rule.Match, w.ExeName),
                    _ => false
                };
                if (hit) return rule.Workspace;
            }
            return null;
        }

        private static bool SafeRegex(string pattern, string input)
        {
            try { return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50)); }
            catch { return false; }
        }

        public void SwitchTo(int workspace)
        {
            if (workspace < 1 || workspace == ActiveWorkspace) return;

            Sweep(); // adopt any unknown windows first so nothing is orphaned

            foreach (var kv in _windowWorkspace.ToList())
            {
                var hwnd = kv.Key;
                int ws = kv.Value;
                if (!NativeMethods.IsWindow(hwnd)) { _windowWorkspace.Remove(hwnd); _hiddenByUs.Remove(hwnd); continue; }
                if (ws == 0) continue; // pinned to all workspaces

                if (ws == workspace) ShowWindowBack(hwnd);
                else if (ws == ActiveWorkspace) HideWindow(hwnd);
            }

            ActiveWorkspace = workspace;
            _configService.Config.General.ActiveWorkspace = workspace;
            _configService.Save();
            SaveSession();
            ActiveWorkspaceChanged?.Invoke(workspace);
        }

        /// <summary>Sends the focused window to a workspace and hides it if that workspace is not active.</summary>
        public void SendForegroundToWorkspace(int workspace)
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (!WindowTracker.IsManageable(hwnd)) return;
            _windowWorkspace[hwnd] = workspace;
            if (workspace != 0 && workspace != ActiveWorkspace) HideWindow(hwnd);
            SaveSession();
        }

        public void SetWindowWorkspace(IntPtr hwnd, int workspace)
        {
            _windowWorkspace[hwnd] = workspace;
            if (workspace != 0 && workspace != ActiveWorkspace) HideWindow(hwnd);
            else ShowWindowBack(hwnd);
            SaveSession();
        }

        public IReadOnlyDictionary<IntPtr, int> Assignments => _windowWorkspace;

        public int WindowCount(int workspace) => _windowWorkspace.Count(kv => kv.Value == workspace && NativeMethods.IsWindow(kv.Key));

        private void HideWindow(IntPtr hwnd)
        {
            if (_hiddenByUs.Contains(hwnd)) return;
            if (NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_HIDE))
            {
                _hiddenByUs.Add(hwnd);
                SaveJournal();
            }
        }

        private void ShowWindowBack(IntPtr hwnd)
        {
            if (!_hiddenByUs.Contains(hwnd)) return;
            NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_SHOWNA);
            _hiddenByUs.Remove(hwnd);
            SaveJournal();
        }

        private void Sweep()
        {
            foreach (var w in WindowTracker.EnumerateManageable())
            {
                if (!_windowWorkspace.ContainsKey(w.Hwnd))
                {
                    int ws = AssignByRulesOrDefault(w, ActiveWorkspace);
                    if (ws != 0 && ws != ActiveWorkspace) HideWindow(w.Hwnd);
                }
            }
            // prune dead
            foreach (var hwnd in _windowWorkspace.Keys.Where(h => !NativeMethods.IsWindow(h)).ToList())
            {
                _windowWorkspace.Remove(hwnd);
                _hiddenByUs.Remove(hwnd);
            }
        }

        // ---- persistence -------------------------------------------------

        private void SaveJournal()
        {
            try
            {
                File.WriteAllText(JournalPath, JsonSerializer.Serialize(_hiddenByUs.Select(h => h.ToInt64()).ToList()));
            }
            catch { }
        }

        private void RecoverOrphanedWindows()
        {
            try
            {
                if (!File.Exists(JournalPath)) return;
                var handles = JsonSerializer.Deserialize<List<long>>(File.ReadAllText(JournalPath)) ?? new();
                foreach (var h in handles)
                {
                    var hwnd = new IntPtr(h);
                    if (NativeMethods.IsWindow(hwnd) && !NativeMethods.IsWindowVisible(hwnd))
                        NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_SHOWNA);
                }
                File.Delete(JournalPath);
                if (handles.Count > 0) ConfigService.Log($"Recovered {handles.Count} windows from previous session journal.");
            }
            catch (Exception ex) { ConfigService.Log("Recovery failed: " + ex.Message); }
        }

        private class SessionEntry { public string Exe { get; set; } = ""; public string Title { get; set; } = ""; public int Workspace { get; set; } }

        /// <summary>Persist exe/title -> workspace so assignments survive restarts (best effort re-match).</summary>
        private void SaveSession()
        {
            try
            {
                var entries = new List<SessionEntry>();
                foreach (var kv in _windowWorkspace)
                {
                    if (!NativeMethods.IsWindow(kv.Key)) continue;
                    var info = WindowTracker.GetInfo(kv.Key);
                    entries.Add(new SessionEntry { Exe = info.ExeName, Title = info.Title, Workspace = kv.Value });
                }
                File.WriteAllText(SessionPath, JsonSerializer.Serialize(entries));
            }
            catch { }
        }

        private void RestoreSession()
        {
            try
            {
                if (!File.Exists(SessionPath)) return;
                var entries = JsonSerializer.Deserialize<List<SessionEntry>>(File.ReadAllText(SessionPath)) ?? new();
                foreach (var kv in _windowWorkspace.ToList())
                {
                    var info = WindowTracker.GetInfo(kv.Key);
                    var match = entries.FirstOrDefault(e => e.Exe.Equals(info.ExeName, StringComparison.OrdinalIgnoreCase)
                                                            && e.Title == info.Title)
                             ?? entries.FirstOrDefault(e => e.Exe.Equals(info.ExeName, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        _windowWorkspace[kv.Key] = match.Workspace;
                        if (match.Workspace != 0 && match.Workspace != ActiveWorkspace) HideWindow(kv.Key);
                    }
                }
            }
            catch (Exception ex) { ConfigService.Log("Session restore failed: " + ex.Message); }
        }

        /// <summary>Show every window we ever hid. Called on clean shutdown.</summary>
        public void RestoreAllWindows()
        {
            foreach (var hwnd in _hiddenByUs.ToList())
            {
                if (NativeMethods.IsWindow(hwnd))
                    NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_SHOWNA);
            }
            _hiddenByUs.Clear();
            try { File.Delete(JournalPath); } catch { }
            SaveSession();
        }

        public void Dispose()
        {
            _sweepTimer?.Stop();
            foreach (var h in _hooks) NativeMethods.UnhookWinEvent(h);
            _hooks.Clear();
            RestoreAllWindows();
        }
    }
}
