using System;
using System.Collections.Generic;
using System.Linq;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Interop;
using WorkspaceOS.Core.Tiling;

namespace WorkspaceOS.Linux
{
    /// <summary>
    /// The Windows TilingEngine, ported to X11. Same Dwindle layout core
    /// (LayoutTree), same TilingConfig semantics: one BSP tree per
    /// (desktop × monitor), gaps, pseudotile, floating, preselection,
    /// directional focus/move/resize. Windows are adopted per desktop and
    /// re-tiled on switch, exactly like the engine's per-workspace trees.
    /// </summary>
    internal sealed class LinuxTilingEngine
    {
        private readonly ConfigService _configs;
        private readonly X11Backend _x;

        private readonly Dictionary<int, LayoutTree> _trees = new();  // desktop -> tree
        private readonly HashSet<long> _floating = new();
        private readonly HashSet<long> _ignored = new();
        private Preselect _preselect = Preselect.None;

        public LinuxTilingEngine(ConfigService configs, X11Backend x)
        {
            _configs = configs;
            _x = x;
        }

        public bool Enabled => _configs.Config.Tiling.EnableTiling;
        private TilingConfig Cfg => _configs.Config.Tiling;

        /// <summary>Usable area for tiled windows: full virtual root minus a bar strip.</summary>
        private RECT WorkArea()
        {
            var r = new RECT();
            var outp = XTool.Run("xprop", "-root -notype -f _NET_WORKAREA '32x' '$0\\n' _NET_WORKAREA", quiet: true);
            // Format: "_NET_WORKAREA: 0, 0, 1920, 1040"
            var m = outp.Split(':');
            if (m.Length == 2)
            {
                var v = m[1].Trim().Split(',').Select(s => s.Trim()).ToArray();
                if (v.Length >= 4 && int.TryParse(v[0], out var px) && int.TryParse(v[1], out var py)
                    && int.TryParse(v[2], out var pw) && int.TryParse(v[3], out var ph))
                {
                    r = new RECT { Left = px, Top = py, Right = px + pw, Bottom = py + ph };
                }
            }
            if (r.Right <= r.Left)   // fallback: root geometry
            {
                var root = XTool.Run("xwininfo", "-root -stats", quiet: true);
                int w = 1920, h = 1080;
                foreach (var l in root.Split('\n'))
                {
                    var t = l.Trim();
                    if (t.StartsWith("Width:")) int.TryParse(t.Split(':')[1].Trim(), out w);
                    else if (t.StartsWith("Height:")) int.TryParse(t.Split(':')[1].Trim(), out h);
                }
                r = new RECT { Left = 0, Top = 0, Right = w, Bottom = h };
            }
            return r;
        }

        // ------------------------------------------------------------- adoption

        /// <summary>Rebuilds the current desktop's tree from the live window list.</summary>
        public void AdoptCurrentDesktop(List<XWindow> windows = null)
        {
            if (!Enabled) return;
            int desktop = _x.CurrentDesktop();
            windows ??= _x.ListWindows();
            var area = WorkArea();

            var tree = new LayoutTree(Cfg, IntPtr.Zero, $"desktop-{desktop}", area);
            _trees[desktop] = tree;

            foreach (var w in windows.Where(ShouldTile).OrderBy(w => w.Id.ToInt64()))
            {
                var leaf = tree.Insert(w.Id, null, Preselect.None, -1, -1);
                leaf.LastRect = new RECT { Left = w.X, Top = w.Y, Right = w.X + w.W, Bottom = w.Y + w.H };
            }
            Apply(desktop, tree);
        }

        private bool ShouldTile(XWindow w)
        {
            if (w == null || w.Id == IntPtr.Zero) return false;
            if (w.Hidden || w.Desktop >= 0 && w.Desktop != _x.CurrentDesktop()) return false;
            if (w.MaximizedVert || w.MaximizedHorz) return false;
            if (_floating.Contains(w.Id.ToInt64()) || _ignored.Contains(w.Id.ToInt64())) return false;
            if (Math.Max(w.W, w.H) > 0 && Math.Min(w.W, w.H) < Cfg.MinWindowSize) return false;
            if (string.Equals(w.WmClass, "workspaceos", StringComparison.OrdinalIgnoreCase)) return false;

            // Tiling rules use the same match model as the Windows engine.
            foreach (var rule in Cfg.TilingRules)
            {
                if (rule == null || !rule.Enabled || string.IsNullOrWhiteSpace(rule.Match)) continue;
                string input = rule.MatchType switch
                {
                    "Title" => w.Title ?? "",
                    "Class" => w.WmClass ?? "",
                    "Process" => w.WmClass ?? "",
                    _ => w.WmClass ?? "",
                };
                bool matched = rule.MatchType == "Regex"
                    ? MatchesRegex(w.WmClass, w.Title, rule.Match)
                    : input.Contains(rule.Match, StringComparison.OrdinalIgnoreCase);
                if (matched)
                {
                    switch (rule.Action)
                    {
                        case "Ignore": return false;
                        case "Float": return false;
                        case "Tile": return true;
                    }
                }
            }
            return true;
        }

        private static bool MatchesRegex(string wmClass, string title, string pattern)
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

        // ------------------------------------------------------------- apply

        /// <summary>Pushes the computed rectangles of a desktop's tree to X11.</summary>
        public void Apply(int desktop, LayoutTree tree = null)
        {
            if (!Enabled) return;
            tree ??= _trees.TryGetValue(desktop, out var t) ? t : null;
            if (tree?.Root == null) return;

            foreach (var (hwnd, rect) in tree.ComputeLayout())
                _x.MoveResize(hwnd, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        public void RetileAll()
        {
            if (!Enabled) return;
            AdoptCurrentDesktop();
        }

        // ------------------------------------------------------------- commands

        private LayoutTree CurrentTree()
        {
            int d = _x.CurrentDesktop();
            if (_trees.TryGetValue(d, out var t) && t.Root != null) return t;
            AdoptCurrentDesktop();
            return _trees.TryGetValue(d, out t) ? t : null;
        }

        private LeafNode LeafOf(long id, LayoutTree tree) =>
            tree?.Root?.LeavesInOrder().FirstOrDefault(l => l.Hwnd.ToInt64() == id);

        public void FocusDirection(Direction dir)
        {
            if (!Enabled) return;
            var tree = CurrentTree();
            var focused = _x.FocusedWindow();
            if (tree == null || focused == IntPtr.Zero) return;
            var from = LeafOf(focused.ToInt64(), tree) ?? tree.Root?.LeavesInOrder().FirstOrDefault();
            if (from == null) return;

            var target = LayoutTree.NeighborInDirection(tree, from, dir, Cfg.FocusWrap);
            if (target != null) _x.Activate(target.Hwnd);
        }

        public void MoveDirection(Direction dir)
        {
            if (!Enabled) return;
            var tree = CurrentTree();
            var focused = _x.FocusedWindow();
            if (tree == null || focused == IntPtr.Zero) return;
            var from = LeafOf(focused.ToInt64(), tree);
            if (from == null) return;

            var target = LayoutTree.NeighborInDirection(tree, from, dir, wrap: false);
            if (target == null) return;   // at the edge — nothing to swap with
            LayoutTree.Swap(from, target);
            Apply(_x.CurrentDesktop(), tree);
        }

        public void ResizeDirection(Direction dir)
        {
            if (!Enabled) return;
            var tree = CurrentTree();
            var focused = _x.FocusedWindow();
            if (tree == null || focused == IntPtr.Zero) return;
            var from = LeafOf(focused.ToInt64(), tree);
            if (from == null) return;
            tree.Resize(from, dir, Cfg.ResizeStep);
            Apply(_x.CurrentDesktop(), tree);
        }

        public void ToggleFloat()
        {
            if (!Enabled) return;
            var id = _x.FocusedWindow();
            if (id == IntPtr.Zero) return;
            long key = id.ToInt64();

            if (_floating.Remove(key))
            {
                AdoptCurrentDesktop();          // re-adopt back into the tree
            }
            else
            {
                _floating.Add(key);
                if (_trees.TryGetValue(_x.CurrentDesktop(), out var tree))
                {
                    var leaf = LeafOf(key, tree);
                    if (leaf != null) { tree.Remove(leaf); Apply(_x.CurrentDesktop(), tree); }
                }
            }
        }

        public void ToggleSplit()
        {
            if (!Enabled) return;
            var tree = CurrentTree();
            var focused = _x.FocusedWindow();
            if (tree == null || focused == IntPtr.Zero) return;
            var from = LeafOf(focused.ToInt64(), tree);
            if (from != null) { tree.ToggleSplit(from); Apply(_x.CurrentDesktop(), tree); }
        }

        public void TogglePseudotile()
        {
            if (!Enabled) return;
            var tree = CurrentTree();
            var focused = _x.FocusedWindow();
            if (tree == null || focused == IntPtr.Zero) return;
            var from = LeafOf(focused.ToInt64(), tree);
            if (from != null)
            {
                from.Pseudotile = !from.Pseudotile;
                Apply(_x.CurrentDesktop(), tree);
            }
        }

        public void PreselectSplit(Direction dir)
        {
            if (!Enabled) return;
            _preselect = dir switch
            {
                Direction.Left => Preselect.Left,
                Direction.Right => Preselect.Right,
                Direction.Up => Preselect.Up,
                Direction.Down => Preselect.Down,
                _ => Preselect.None,
            };
        }

        /// <summary>Insert a newly managed window into the current desktop's tree.</summary>
        public void AdoptWindow(IntPtr id)
        {
            if (!Enabled) return;
            var tree = CurrentTree();
            if (tree == null) return;
            var leaf = tree.Insert(id, tree.Root?.LeavesInOrder().FirstOrDefault(), _preselect, -1, -1);
            _preselect = Preselect.None;    // one-shot, like Hyprland
            Apply(_x.CurrentDesktop(), tree);
        }

        public void RemoveWindow(IntPtr id)
        {
            foreach (var tree in _trees.Values)
            {
                var leaf = LeafOf(id.ToInt64(), tree);
                if (leaf != null) { tree.Remove(leaf); }
            }
            if (Enabled && _trees.TryGetValue(_x.CurrentDesktop(), out var t)) Apply(_x.CurrentDesktop(), t);
        }

        public void SendFocusedToWorkspace(int workspace)
        {
            if (!Enabled) return;
            var id = _x.FocusedWindow();
            if (id == IntPtr.Zero) return;
            RemoveWindow(id);
            if (_x.SendToDesktop(id, Math.Clamp(workspace - 1, 0, 35), Cfg.FollowMovedWindow))
                if (Cfg.FollowMovedWindow) AdoptCurrentDesktop();
        }

        /// <summary>Called when tiling is toggled or config changes.</summary>
        public void ApplyConfigChange()
        {
            _floating.Clear();
            _ignored.Clear();
            _trees.Clear();
            if (Enabled) AdoptCurrentDesktop();
        }
    }
}
