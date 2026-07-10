using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Interop;
using WorkspaceOS.Core.VirtualDesktops;
using WorkspaceOS.Core.WindowSystem;

namespace WorkspaceOS.Core.Workspaces
{
    /// <summary>
    /// Workspace layer built on Windows' NATIVE Virtual Desktops.
    ///
    /// WorkspaceOS does not hide or move windows itself anymore — Win+1..9
    /// switch the real OS desktops (the same ones as Ctrl+Win+Arrow and Task
    /// View), so everything stays consistent with the shell. This class adds:
    ///   • absolute switching (Win+N) and send-to-desktop (Win+Shift+N)
    ///   • window rules (auto-assign new windows to a desktop; 0 = pinned to all)
    ///   • change tracking so the bar updates when desktops change natively.
    /// </summary>
    public class WorkspaceManager : IDisposable
    {
        private readonly ConfigService _configService;
        private readonly VirtualDesktopService _desktops = new();
        private readonly HashSet<IntPtr> _ruleApplied = new();   // windows we already ran rules for
        private NativeMethods.WinEventDelegate _winEventProc;    // keep delegate alive
        private readonly List<IntPtr> _hooks = new();
        private System.Windows.Threading.DispatcherTimer _pollTimer;
        private System.Windows.Threading.DispatcherTimer _sweepTimer;
        private int _lastIndex = -1;

        /// <summary>1-based index of the active native desktop.</summary>
        public int ActiveWorkspace => _desktops.GetCurrentIndex() + 1;

        /// <summary>Number of native desktops currently in existence.</summary>
        public int WorkspaceCount => _desktops.GetCount();

        public bool NativeApiAvailable => _desktops.Available;

        public event Action<int> ActiveWorkspaceChanged;

        public WorkspaceManager(ConfigService configService)
        {
            _configService = configService;
        }

        public void Initialize()
        {
            _desktops.Initialize();

            // Default experience: the configured workspaces (4 by default)
            // exist as real desktops immediately. Never deletes extras.
            _desktops.EnsureCount(Math.Max(1, _configService.Config.Workspaces.Count));
            _lastIndex = _desktops.GetCurrentIndex();

            // Run rules over windows that already exist.
            foreach (var w in WindowTracker.EnumerateManageable())
                ApplyRules(w);

            // Rules for windows that appear later.
            _winEventProc = OnWinEvent;
            _hooks.Add(NativeMethods.SetWinEventHook(NativeMethods.EVENT_OBJECT_SHOW, NativeMethods.EVENT_OBJECT_SHOW,
                IntPtr.Zero, _winEventProc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT));
            _hooks.Add(NativeMethods.SetWinEventHook(NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.EVENT_OBJECT_DESTROY,
                IntPtr.Zero, _winEventProc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT));

            // Detect desktop switches made outside WorkspaceOS (Ctrl+Win+Arrow,
            // Task View, another tool) so the bar always shows the truth.
            _pollTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _pollTimer.Tick += (_, _) =>
            {
                int idx = _desktops.GetCurrentIndex();
                if (idx != _lastIndex)
                {
                    _lastIndex = idx;
                    _configService.Config.General.ActiveWorkspace = idx + 1;
                    ActiveWorkspaceChanged?.Invoke(idx + 1);
                }
            };
            _pollTimer.Start();

            // Catch windows the SHOW event missed (some UWP apps).
            _sweepTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _sweepTimer.Tick += (_, _) =>
            {
                foreach (var w in WindowTracker.EnumerateManageable())
                    ApplyRules(w);
            };
            _sweepTimer.Start();
        }

        private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != 0 || hwnd == IntPtr.Zero) return; // OBJID_WINDOW only
            if (eventType == NativeMethods.EVENT_OBJECT_DESTROY)
            {
                _ruleApplied.Remove(hwnd);
                return;
            }
            if (eventType == NativeMethods.EVENT_OBJECT_SHOW)
            {
                if (_ruleApplied.Contains(hwnd) || !WindowTracker.IsManageable(hwnd)) return;
                var info = WindowTracker.GetInfo(hwnd);
                ApplyRules(info);
                App.FocusServiceInstance?.OnWindowShown(info);
            }
        }

        /// <summary>Applies the first matching rule (once per window). Workspace 0 = pin to all desktops.</summary>
        private void ApplyRules(TrackedWindow w)
        {
            if (_ruleApplied.Contains(w.Hwnd)) return;
            _ruleApplied.Add(w.Hwnd);

            int? ws = MatchRules(w);
            if (ws == null) return;

            if (ws.Value == 0)
            {
                if (!_desktops.PinWindow(w.Hwnd))
                    ConfigService.Log($"Rule pin failed for {w.ExeName} ({w.Title})");
            }
            else if (ws.Value != ActiveWorkspace)
            {
                if (!_desktops.MoveWindowToDesktop(w.Hwnd, ws.Value - 1))
                    ConfigService.Log($"Rule move failed for {w.ExeName} → workspace {ws.Value}");
            }
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

        /// <summary>Switch to native desktop N (1-based). Creates it if needed.</summary>
        public void SwitchTo(int workspace)
        {
            if (workspace < 1) return;
            if (_desktops.SwitchTo(workspace - 1))
            {
                _lastIndex = workspace - 1;
                _configService.Config.General.ActiveWorkspace = workspace;
                _configService.Save();
                ActiveWorkspaceChanged?.Invoke(workspace);
            }
        }

        /// <summary>Send the focused window to native desktop N (window stays put; you don't follow).</summary>
        public void SendForegroundToWorkspace(int workspace)
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (!WindowTracker.IsManageable(hwnd)) return;
            if (workspace == 0) _desktops.PinWindow(hwnd);
            else _desktops.MoveWindowToDesktop(hwnd, workspace - 1);
        }

        /// <summary>Pin one of our own windows (bar, popups) so it shows on every desktop.</summary>
        public void PinAppWindow(IntPtr hwnd) => _desktops.PinWindow(hwnd);

        /// <summary>Reconnect COM after an Explorer restart.</summary>
        public void ReconnectShell() => _desktops.Reconnect();

        public void Dispose()
        {
            _pollTimer?.Stop();
            _sweepTimer?.Stop();
            foreach (var h in _hooks) NativeMethods.UnhookWinEvent(h);
            _hooks.Clear();
        }
    }
}
