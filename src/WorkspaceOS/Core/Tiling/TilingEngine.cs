using System;
using System.Collections.Generic;
using System.Linq;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Interop;
using WorkspaceOS.Core.VirtualDesktops;
using WorkspaceOS.Core.WindowSystem;
using WorkspaceOS.Core.Workspaces;

namespace WorkspaceOS.Core.Tiling
{
    /// <summary>A window kept out of the tree on purpose (rule or user toggle).</summary>
    public sealed class FloatingEntry
    {
        public IntPtr Hwnd;
        public RECT Rect;
        public bool WasMaximized;
    }

    /// <summary>All tiling state for one workspace on one monitor.</summary>
    public sealed class TiledArea
    {
        public int Workspace;                // 1-based
        public IntPtr Monitor;
        public string Device = "";
        public RECT WorkArea;
        public LayoutTree Tree;
        public List<FloatingEntry> Floating = new();
        public IntPtr Focused;               // last focused tiled window
    }

    /// <summary>
    /// The tiling engine: owns one LayoutTree per (workspace × monitor), adopts/releases
    /// windows from WinEvents, applies geometry, and exposes every user command.
    /// All public methods must run on the UI (STA) thread — VirtualDesktopService requires it.
    /// </summary>
    public sealed class TilingEngine : IDisposable
    {
        private readonly ConfigService _configs;
        private readonly WorkspaceManager _workspaces;
        private NativeMethods.WinEventDelegate _winEventProc;   // keep delegate alive
        private readonly List<IntPtr> _hooks = new();
        private System.Windows.Threading.DispatcherTimer _applyTimer;
        private readonly HashSet<(int ws, string dev)> _dirty = new();
        private readonly Dictionary<IntPtr, DateTime> _recentlyPositioned = new();  // event feedback suppression
        private readonly HashSet<IntPtr> _ignored = new();      // rule action "Ignore"
        private readonly List<IntPtr> _scratchpad = new();      // hidden scratchpad windows
        private bool _scratchpadVisible;

        public bool Enabled { get; private set; }
        public event Action StateChanged;                       // debug UI / tests

        public TilingEngine(ConfigService configs, WorkspaceManager workspaces)
        {
            _configs = configs;
            _workspaces = workspaces;
            _workspaces.ActiveWorkspaceChanged += _ => OnWorkspaceSwitched();
        }

        private TilingConfig Cfg => _configs.Config.Tiling;

        /// <summary>Active areas; key = (workspace, monitor device name).</summary>
        public Dictionary<(int ws, string dev), TiledArea> Areas { get; } = new();

        // ------------------------------------------------------------------ lifecycle

        public void Initialize()
        {
            Enabled = Cfg.EnableTiling;
            if (!Enabled) { ConfigService.Log("Tiling: disabled in settings."); return; }

            SyncMonitors();

            _winEventProc = OnWinEvent;
            Hook(NativeMethods.EVENT_OBJECT_SHOW);
            Hook(NativeMethods.EVENT_OBJECT_UNCLOAKED);
            Hook(NativeMethods.EVENT_OBJECT_DESTROY);
            Hook(NativeMethods.EVENT_OBJECT_FOCUS);
            Hook(NativeMethods.EVENT_SYSTEM_MINIMIZESTART);
            Hook(NativeMethods.EVENT_SYSTEM_MINIMIZEEND);
            Hook(NativeMethods.EVENT_SYSTEM_MOVESIZEEND);

            // Debounced layout applier: WinEvents arrive in bursts; we coalesce.
            _applyTimer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(80) };
            _applyTimer.Tick += (_, _) => ApplyDirty();
            _applyTimer.Start();

            // First adoption pass for windows that already exist.
            AdoptExisting();
            MarkAllDirty();
            ConfigService.Log("Tiling: engine started.");
        }

        private void Hook(uint ev) =>
            _hooks.Add(NativeMethods.SetWinEventHook(ev, ev, IntPtr.Zero, _winEventProc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT));

        public void Dispose()
        {
            _applyTimer?.Stop();
            foreach (var h in _hooks) NativeMethods.UnhookWinEvent(h);
            _hooks.Clear();
            ClearIndicator();
        }

        public void ApplyConfigChange()
        {
            bool want = Cfg.EnableTiling;
            if (want && !Enabled) Initialize();
            else if (!want && Enabled)
            {
                Enabled = false;
                ClearIndicator();
                ConfigService.Log("Tiling: disabled at runtime; windows left as-is.");
            }
            else if (Enabled) { SyncMonitors(); MarkAllDirty(); }
        }

        // ------------------------------------------------------------------ monitors / areas

        /// <summary>Creates areas for new monitors, drops dead ones, refreshes work areas.</summary>
        public void SyncMonitors()
        {
            var monitors = NativeMethods.EnumMonitors();
            var seen = new HashSet<string>();

            foreach (var (handle, bounds, work, device) in monitors)
            {
                seen.Add(device);
                foreach (int ws in Enumerable.Range(1, Math.Max(1, _configs.Config.Workspaces.Count)))
                {
                    var key = (ws, device);
                    if (!Areas.TryGetValue(key, out var area))
                    {
                        area = new TiledArea
                        {
                            Workspace = ws,
                            Monitor = handle,
                            Device = device,
                            Tree = new LayoutTree(Cfg, handle, device, work)
                        };
                        Areas[key] = area;
                    }
                    area.Monitor = handle;
                    area.WorkArea = work;
                    area.Tree.WorkArea = work;
                }
            }

            foreach (var dead in Areas.Keys.Where(k => !seen.Contains(k.dev)).ToList())
                Areas.Remove(dead);
        }

        private TiledArea ActiveArea(IntPtr monitor, string device) =>
            Areas.TryGetValue((ActiveWorkspace, device), out var a) ? a : null;

        private int ActiveWorkspace => _workspaces.ActiveWorkspace;

        // ------------------------------------------------------------------ adoption

        private void AdoptExisting()
        {
            foreach (var w in WindowTracker.EnumerateTileCandidates())
                TryAdopt(w.Hwnd, initialPass: true);
        }

        /// <summary>Evaluates rules and either tiles, floats, or ignores a window.</summary>
        private void TryAdopt(IntPtr hwnd, bool initialPass = false)
        {
            if (!Enabled || hwnd == IntPtr.Zero) return;
            if (_ignored.Contains(hwnd)) return;
            if (FindTiled(hwnd) != null || FindFloating(hwnd) != null) return;
            if (IsOurs(hwnd)) return;
            if (!WindowTracker.IsTileCandidate(hwnd)) return;

            var info = WindowTracker.GetInfo(hwnd);

            switch (MatchTilingRule(info))
            {
                case "Ignore":
                    _ignored.Add(hwnd);
                    return;
                case "Float":
                    FloatWindow(hwnd, keepRect: true);
                    return;
            }

            // Owned dialogs float instead of tiling (never break modal flows).
            if (!initialPass && WindowTracker.LooksLikeDialog(hwnd))
            {
                FloatWindow(hwnd, keepRect: true);
                return;
            }

            // Maximized/fullscreen windows keep their state until the user interacts.
            if (NativeMethods.IsZoomed(hwnd)) { FloatWindow(hwnd, keepRect: true, wasMaximized: true); return; }

            var area = AreaFor(hwnd);
            if (area == null) return;

            var insertAt = area.Tree.FindLeaf(area.Focused) ?? area.Tree.Root?.LeavesInOrder().LastOrDefault();
            var pre = TakePreselect();
            var cursor = CursorPos();
            area.Tree.Insert(hwnd, insertAt, pre, cursor.x, cursor.y);
            area.Focused = hwnd;
            MarkDirty(area);
            ConfigService.Log($"Tiling: adopted 0x{hwnd.ToInt64():X} ({info.ExeName}) into ws{area.Workspace}/{area.Device}");
        }

        private static bool IsOurs(IntPtr hwnd) =>
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid) != 0 && pid == Environment.ProcessId;

        /// <summary>Which area should own a window: its current monitor × current workspace.</summary>
        private TiledArea AreaFor(IntPtr hwnd)
        {
            var mon = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var match = Areas.Values.FirstOrDefault(a => a.Monitor == mon && a.Workspace == ActiveWorkspace)
                     ?? Areas.Values.FirstOrDefault(a => a.Workspace == ActiveWorkspace);
            if (match != null) return match;
            SyncMonitors();
            return Areas.Values.FirstOrDefault(a => a.Workspace == ActiveWorkspace);
        }

        // ------------------------------------------------------------------ WinEvents

        private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != 0 || hwnd == IntPtr.Zero) return;
            try
            {
                switch (eventType)
                {
                    case NativeMethods.EVENT_OBJECT_SHOW:
                        if (!NativeMethods.IsCloaked(hwnd) && WindowTracker.IsTileCandidate(hwnd))
                            TryAdopt(hwnd);
                        break;

                    case NativeMethods.EVENT_OBJECT_UNCLOAKED:
                        // UWP windows become visible when their desktop is shown.
                        TryAdopt(hwnd);
                        break;

                    case NativeMethods.EVENT_OBJECT_DESTROY:
                        OnWindowGone(hwnd);
                        break;

                    case NativeMethods.EVENT_OBJECT_FOCUS:
                        OnFocus(hwnd);
                        break;

                    case NativeMethods.EVENT_SYSTEM_MINIMIZESTART:
                        // Keep the leaf; geometry skips iconic windows. Relayout the rest.
                        MarkDirtyFor(hwnd);
                        break;

                    case NativeMethods.EVENT_SYSTEM_MINIMIZEEND:
                        // Restored: re-apply its slot (fights nothing — the window asked for it).
                        MarkDirtyFor(hwnd);
                        break;

                    case NativeMethods.EVENT_SYSTEM_MOVESIZEEND:
                        MarkDirtyFor(hwnd);
                        break;
                }
            }
            catch (Exception ex) { ConfigService.Log("Tiling winEvent: " + ex.Message); }
        }

        private void OnWindowGone(IntPtr hwnd)
        {
            _ignored.Remove(hwnd);
            _recentlyPositioned.Remove(hwnd);
            _scratchpad.Remove(hwnd);
            if (Release(hwnd)) MarkAllDirty();
        }

        private void OnFocus(IntPtr hwnd)
        {
            if (IsOurs(hwnd) || !WindowTracker.IsManageable(hwnd)) return;

            var leaf = FindTiled(hwnd);
            if (leaf != null)
            {
                var area = Areas.Values.FirstOrDefault(a => a.Tree.FindLeaf(hwnd) != null);
                if (area != null) area.Focused = hwnd;
                UpdateIndicator(hwnd);
            }
            else if (FindFloating(hwnd) != null)
            {
                UpdateIndicator(hwnd);
            }
        }

        private void OnWorkspaceSwitched()
        {
            if (!Enabled) return;
            MarkAllDirty();
            // Windows that became visible with the desktop (previously cloaked) get adopted.
            DispatcherBeginInvoke(() =>
            {
                foreach (var w in WindowTracker.EnumerateTileCandidates())
                    TryAdopt(w.Hwnd);
                MarkAllDirty();
            });
        }

        /// <summary>
        /// Called right before a workspace switch: moves the currently visible windows
        /// into the target workspace's trees so the layout they see is the one they had.
        /// This is what makes a workspace "own" its windows Hyprland-style.
        /// </summary>
        public void PreAdoptForSwitch(int targetWorkspace)
        {
            if (!Enabled || targetWorkspace < 1) return;
            var current = ActiveWorkspace;
            if (targetWorkspace == current) return;

            foreach (var w in WindowTracker.EnumerateTileCandidates())
            {
                var hwnd = w.Hwnd;
                if (_workspaces.IsWindowPinned(hwnd)) continue;   // pinned windows belong to every desktop

                var leaf = FindTiled(hwnd);
                if (leaf != null)
                {
                    var from = Areas.Values.First(a => a.Tree.FindLeaf(hwnd) != null);
                    if (from.Workspace == targetWorkspace) continue;

                    from.Tree.Remove(leaf);
                    if (from.Focused == hwnd)
                        from.Focused = from.Tree.Root?.LeavesInOrder().LastOrDefault()?.Hwnd ?? IntPtr.Zero;
                    MarkDirty(from);
                }
                else if (FindFloating(hwnd) != null || _ignored.Contains(hwnd))
                    continue;   // floaters and ignored windows keep their desktop-pin behavior
                // else: visible-but-unmanaged (opened on another desktop) → adopt into target

                // Insert into the target workspace's area for this window's monitor.
                var mon = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
                var target = Areas.Values.FirstOrDefault(a => a.Workspace == targetWorkspace && a.Monitor == mon)
                          ?? Areas.Values.FirstOrDefault(a => a.Workspace == targetWorkspace);
                if (target == null) continue;

                var insertAt = target.Tree.FindLeaf(target.Focused) ?? target.Tree.Root?.LeavesInOrder().LastOrDefault();
                target.Tree.Insert(hwnd, insertAt, Preselect.None, -1, -1);
                target.Focused = hwnd;
                MarkDirty(target);
            }
        }

        private void DispatcherBeginInvoke(Action a) =>
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(a);

        // ------------------------------------------------------------------ dirty/apply

        private void MarkDirty(TiledArea area) => _dirty.Add((area.Workspace, area.Device));

        private void MarkAllDirty()
        {
            foreach (var a in Areas.Values) MarkDirty(a);
        }

        private void MarkDirtyFor(IntPtr hwnd)
        {
            foreach (var a in Areas.Values)
                if (a.Tree.FindLeaf(hwnd) != null || a.Floating.Any(f => f.Hwnd == hwnd))
                    MarkDirty(a);
        }

        private void ApplyDirty()
        {
            if (!Enabled || _dirty.Count == 0) return;
            foreach (var key in _dirty.ToList())
            {
                _dirty.Remove(key);
                if (!Areas.TryGetValue(key, out var area)) continue;
                if (area.Workspace != ActiveWorkspace) continue;   // off-screen desktops get applied on switch
                Apply(area);
            }
        }

        /// <summary>Positions every tiled window of the area and clamps floaters into the work area.</summary>
        private void Apply(TiledArea area)
        {
            var rects = area.Tree.ComputeLayout();
            var now = DateTime.UtcNow;

            foreach (var leaf in area.Tree.Root?.LeavesInOrder().ToList() ?? new List<LeafNode>())
            {
                if (NativeMethods.IsIconic(leaf.Hwnd)) continue;
                if (!rects.TryGetValue(leaf.Hwnd, out var r)) continue;

                // Single maximized window: let it stay maximized instead of fighting it.
                if (NativeMethods.IsZoomed(leaf.Hwnd) && rects.Count == 1) continue;

                if (NeedsMove(leaf.Hwnd, r))
                {
                    NativeMethods.SetWindowPos(leaf.Hwnd, IntPtr.Zero,
                        r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                        NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                    _recentlyPositioned[leaf.Hwnd] = now;
                }
            }

            foreach (var f in area.Floating)
            {
                if (f.WasMaximized || NativeMethods.IsIconic(f.Hwnd)) continue;
                var work = area.WorkArea;
                int w = f.Rect.Right - f.Rect.Left, h = f.Rect.Bottom - f.Rect.Top;
                w = Math.Min(w, Math.Max(200, work.Right - work.Left));
                h = Math.Min(h, Math.Max(150, work.Bottom - work.Top));
                int x = Math.Clamp(f.Rect.Left, work.Left - w / 2, Math.Max(work.Left, work.Right - w / 2));
                int y = Math.Clamp(f.Rect.Top, work.Top, Math.Max(work.Top, work.Bottom - 60));
                if (NeedsMove(f.Hwnd, new RECT { Left = x, Top = y, Right = x + w, Bottom = y + h }))
                {
                    NativeMethods.SetWindowPos(f.Hwnd, IntPtr.Zero, x, y, w, h,
                        NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                    f.Rect = new RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
                }
            }
        }

        private static bool NeedsMove(IntPtr hwnd, RECT target)
        {
            if (!NativeMethods.GetWindowRect(hwnd, out var cur)) return false;
            return cur.Left != target.Left || cur.Top != target.Top ||
                   cur.Right != target.Right || cur.Bottom != target.Bottom;
        }

        // ------------------------------------------------------------------ window lookup

        public LeafNode FindTiled(IntPtr hwnd) =>
            Areas.Values.Select(a => a.Tree.FindLeaf(hwnd)).FirstOrDefault(l => l != null);

        public FloatingEntry FindFloating(IntPtr hwnd) =>
            Areas.Values.SelectMany(a => a.Floating).FirstOrDefault(f => f.Hwnd == hwnd);

        private TiledArea AreaOf(IntPtr hwnd) =>
            Areas.Values.FirstOrDefault(a => a.Tree.FindLeaf(hwnd) != null)
            ?? Areas.Values.FirstOrDefault(a => a.Floating.Any(f => f.Hwnd == hwnd));

        // ------------------------------------------------------------------ rules

        /// <summary>Returns "Float", "Ignore", "Tile" or null (no rule hit).</summary>
        public string MatchTilingRule(TrackedWindow w)
        {
            foreach (var rule in Cfg.TilingRules)
            {
                if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Match)) continue;
                bool hit = rule.MatchType switch
                {
                    "Executable" => w.ExeName.Equals(rule.Match, StringComparison.OrdinalIgnoreCase)
                                    || w.ExePath.EndsWith(rule.Match, StringComparison.OrdinalIgnoreCase),
                    "Process" => System.IO.Path.GetFileNameWithoutExtension(w.ExeName).Equals(
                                    System.IO.Path.GetFileNameWithoutExtension(rule.Match), StringComparison.OrdinalIgnoreCase),
                    "Title" => w.Title.Contains(rule.Match, StringComparison.OrdinalIgnoreCase),
                    "Class" => w.ClassName.Equals(rule.Match, StringComparison.OrdinalIgnoreCase),
                    "Regex" => SafeRegex(rule.Match, w.Title) || SafeRegex(rule.Match, w.ExeName),
                    _ => false
                };
                if (hit)
                {
                    return rule.Action switch
                    {
                        "Float" => "Float",
                        "Ignore" => "Ignore",
                        _ => "Tile"
                    };
                }
            }
            return null;
        }

        private static bool SafeRegex(string pattern, string input)
        {
            try { return input.Length > 0 && System.Text.RegularExpressions.Regex.IsMatch(
                input, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50)); }
            catch { return false; }
        }

        // ------------------------------------------------------------------ user commands

        private (int x, int y) CursorPos()
        {
            NativeMethods.GetCursorPos(out var pt);
            return (pt.X, pt.Y);
        }

        private Preselect _pendingPreselect = Preselect.None;
        private bool _hasPreselect;
        private DateTime _preselectAt;

        private Preselect TakePreselect()
        {
            if (!_hasPreselect) return Cfg.PersistentPreselect switch
            {
                "Left" => Preselect.Left, "Right" => Preselect.Right,
                "Up" => Preselect.Up, "Down" => Preselect.Down,
                _ => Preselect.None
            };
            var p = _pendingPreselect;
            if (Cfg.PersistentPreselect == "None" && (DateTime.UtcNow - _preselectAt).TotalSeconds > 30)
            { _hasPreselect = false; return Preselect.None; }
            if (Cfg.PersistentPreselect == "None") _hasPreselect = false;   // one-shot (Hyprland default)
            return p;
        }

        public void PreselectSplit(Direction dir)
        {
            _pendingPreselect = dir switch
            {
                Direction.Left => Preselect.Left,
                Direction.Right => Preselect.Right,
                Direction.Up => Preselect.Up,
                Direction.Down => Preselect.Down,
                _ => Preselect.None
            };
            _hasPreselect = true;
            _preselectAt = DateTime.UtcNow;
        }

        public void FocusDirection(Direction dir)
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            var leaf = FindTiled(hwnd);
            if (leaf == null) return;
            var area = AreaOf(hwnd);
            var next = LayoutTree.NeighborInDirection(area.Tree, leaf, dir, Cfg.FocusWrap);
            if (next == null) return;
            NativeMethods.SetForegroundWindow(next.Hwnd);
            area.Focused = next.Hwnd;
        }

        public void MoveDirection(Direction dir)
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            var leaf = FindTiled(hwnd);
            if (leaf == null) return;
            var area = AreaOf(hwnd);

            var neighbor = LayoutTree.NeighborInDirection(area.Tree, leaf, dir, wrap: false);
            if (neighbor != null)
            {
                LayoutTree.Swap(leaf, neighbor);
                if (area.Focused == hwnd) area.Focused = neighbor.Hwnd;  // focus follows the payload
                MarkDirty(area);
                return;
            }

            // Nothing in that direction: pull the nearest window on a neighboring monitor? No —
            // instead flip the enclosing split like Hyprland's expand-and-move fallback.
            if (area.Tree.ToggleSplit(leaf)) MarkDirty(area);
        }

        public void SwapDirection(Direction dir) => MoveDirection(dir);   // Hyprland swap == move onto neighbor

        public void ResizeDirection(Direction dir)
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            var leaf = FindTiled(hwnd);
            if (leaf == null) return;
            var area = AreaOf(hwnd);
            if (area.Tree.Resize(leaf, dir, Cfg.ResizeStep)) MarkDirty(area);
        }

        public void ToggleSplit()
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            var leaf = FindTiled(hwnd);
            if (leaf == null) return;
            var area = AreaOf(hwnd);
            if (area.Tree.ToggleSplit(leaf)) MarkDirty(area);
        }

        public void TogglePseudotile()
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            var leaf = FindTiled(hwnd);
            if (leaf == null) return;
            leaf.Pseudotile = !leaf.Pseudotile;
            MarkDirty(AreaOf(hwnd));
        }

        public void ToggleFloat()
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            if (FindTiled(hwnd) is { } leaf)
            {
                var area = AreaOf(hwnd);
                var rects = area.Tree.ComputeLayout();
                rects.TryGetValue(hwnd, out var rect);
                area.Tree.Remove(leaf);
                area.Floating.Add(new FloatingEntry
                {
                    Hwnd = hwnd,
                    Rect = rect.Right > rect.Left ? rect : leaf.LastRect
                });
                if (area.Focused == hwnd) area.Focused = area.Tree.Root?.LeavesInOrder().LastOrDefault()?.Hwnd ?? IntPtr.Zero;
                MarkDirty(area);
                ConfigService.Log($"Tiling: floated 0x{hwnd.ToInt64():X}");
            }
            else if (FindFloating(hwnd) is { } f)
            {
                var area = AreaOf(hwnd);
                area.Floating.Remove(f);
                var insertAt = area.Tree.FindLeaf(area.Focused) ?? area.Tree.Root?.LeavesInOrder().LastOrDefault();
                area.Tree.Insert(hwnd, insertAt, TakePreselect(), -1, -1);
                area.Focused = hwnd;
                MarkDirty(area);
                ConfigService.Log($"Tiling: tiled 0x{hwnd.ToInt64():X}");
            }
        }

        private void FloatWindow(IntPtr hwnd, bool keepRect, bool wasMaximized = false)
        {
            var area = AreaFor(hwnd);
            if (area == null) return;
            if (FindFloating(hwnd) != null) return;
            NativeMethods.GetWindowRect(hwnd, out var r);
            area.Floating.Add(new FloatingEntry { Hwnd = hwnd, Rect = r, WasMaximized = wasMaximized });
            if (!wasMaximized) MarkDirty(area);
        }

        /// <summary>Removes a window from tiling state entirely. True if it was managed.</summary>
        public bool Release(IntPtr hwnd)
        {
            bool removed = false;
            var leaf = FindTiled(hwnd);
            if (leaf != null)
            {
                var area = Areas.Values.First(a => a.Tree.FindLeaf(hwnd) != null);
                area.Tree.Remove(leaf);
                if (area.Focused == hwnd)
                    area.Focused = area.Tree.Root?.LeavesInOrder().LastOrDefault()?.Hwnd ?? IntPtr.Zero;
                MarkDirty(area);
                removed = true;
            }
            var floater = FindFloating(hwnd);
            if (floater != null)
            {
                Areas.Values.First(a => a.Floating.Contains(floater)).Floating.Remove(floater);
                removed = true;
            }
            return removed;
        }

        /// <summary>Send the focused window to workspace N (1-based).</summary>
        public void SendFocusedToWorkspace(int workspace)
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;
            SendToWorkspace(hwnd, workspace);
        }

        public void SendToWorkspace(IntPtr hwnd, int workspace)
        {
            if (!Enabled) { _workspaces.SendForegroundToWorkspace(workspace); return; }
            if (workspace < 1 || workspace > 9) return;

            Release(hwnd);
            _workspaces.MoveWindowToDesktop(hwnd, workspace);

            // If the target is the active workspace, adopt it right back into the tree.
            if (workspace == ActiveWorkspace) TryAdopt(hwnd);
            if (Cfg.FollowMovedWindow) _workspaces.SwitchTo(workspace);
        }

        // ------------------------------------------------------------------ scratchpad

        public void ToggleScratchpad()
        {
            if (!Cfg.EnableScratchpad) return;
            if (_scratchpad.Count == 0 || !_scratchpadVisible)
            {
                if (_scratchpad.Count == 0) return;
                _scratchpadVisible = true;
                var area = Areas.Values.FirstOrDefault(a => a.Workspace == ActiveWorkspace)
                        ?? Areas.Values.FirstOrDefault();
                if (area == null) return;
                var work = area.WorkArea;
                foreach (var hwnd in _scratchpad)
                {
                    // Recenter: scratchpad shows as a ~60% window in the middle of the work area.
                    NativeMethods.GetWindowRect(hwnd, out var r);
                    int w = r.Right - r.Left, h = r.Bottom - r.Top;
                    w = Math.Min(w, (work.Right - work.Left) * 6 / 10);
                    h = Math.Min(h, (work.Bottom - work.Top) * 6 / 10);
                    NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST,
                        work.Left + ((work.Right - work.Left) - w) / 2,
                        work.Top + ((work.Bottom - work.Top) - h) / 2, w, h,
                        NativeMethods.SWP_SHOWWINDOW);
                    NativeMethods.SetForegroundWindow(hwnd);
                }
            }
            else
            {
                _scratchpadVisible = false;
                foreach (var hwnd in _scratchpad) NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
            }
        }

        public void ToggleScratchpadWindow()
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;
            if (_scratchpad.Remove(hwnd)) { NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW); }
            else { Release(hwnd); _scratchpad.Add(hwnd); NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE); }
        }

        // ------------------------------------------------------------------ active indicator

        private IntPtr _indicatorHwnd;

        private void UpdateIndicator(IntPtr hwnd)
        {
            if (!Cfg.ActiveIndicator) return;
            if (_indicatorHwnd != IntPtr.Zero && _indicatorHwnd != hwnd)
                SetBorderColor(_indicatorHwnd, null);
            SetBorderColor(hwnd, Cfg.IndicatorColor);
            _indicatorHwnd = hwnd;
        }

        private void ClearIndicator()
        {
            if (_indicatorHwnd != IntPtr.Zero) SetBorderColor(_indicatorHwnd, null);
            _indicatorHwnd = IntPtr.Zero;
        }

        /// <summary>Win11 DWM accent border; on Win10 the call fails silently and this is a no-op.</summary>
        private static void SetBorderColor(IntPtr hwnd, string hex)
        {
            try
            {
                uint color;
                if (hex == null) color = 0xFFFFFFFE;              // DWMWA_COLOR_DEFAULT
                else
                {
                    var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                    color = 0xFF000000u | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
                }
                DwmSetWindowAttribute(hwnd, 34 /* DWMWA_BORDER_COLOR */, ref color, 4);
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint value, int size);

        // ------------------------------------------------------------------ debug

        public string DumpState()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"tiling enabled={Enabled} workspace={ActiveWorkspace} areas={Areas.Count}");
            foreach (var a in Areas.Values.Where(a => a.Workspace == ActiveWorkspace))
            {
                sb.AppendLine($"== ws{a.Workspace}/{a.Device} tiled={a.Tree.Root?.LeafCount ?? 0} floating={a.Floating.Count}");
                sb.Append(a.Tree.RenderTree());
                foreach (var f in a.Floating)
                    sb.AppendLine($"  float 0x{f.Hwnd.ToInt64():X} rect={f.Rect.Left},{f.Rect.Top} {f.Rect.Right - f.Rect.Left}x{f.Rect.Bottom - f.Rect.Top}");
            }
            sb.AppendLine($"ignored={_ignored.Count} scratchpad={_scratchpad.Count} visible={!_scratchpadVisible}");
            return sb.ToString();
        }
    }
}
