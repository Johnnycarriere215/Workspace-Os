using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Interop;

namespace WorkspaceOS.Core.Tiling
{
    /// <summary>Side-by-side (split along the width) or top-and-bottom (split along the height).</summary>
    public enum SplitOrientation { Horizontal, Vertical }

    public enum Direction { Left, Right, Up, Down }

    /// <summary>Hyprland-style split preselection: where the NEXT inserted window goes.</summary>
    public enum Preselect { None, Left, Right, Up, Down }

    public abstract class LayoutNode
    {
        public SplitNode Parent;

        /// <summary>True when this node is its parent's first child (left/top).</summary>
        public bool IsChildA => Parent?.ChildA == this;

        public abstract IEnumerable<LeafNode> Leaves { get; }

        public int LeafCount => Leaves.Count();

        /// <summary>Depth-first leaf list.</summary>
        public IEnumerable<LeafNode> LeavesInOrder()
        {
            switch (this)
            {
                case LeafNode leaf:
                    yield return leaf;
                    break;
                case SplitNode split:
                    foreach (var l in split.ChildA.LeavesInOrder()) yield return l;
                    foreach (var l in split.ChildB.LeavesInOrder()) yield return l;
                    break;
            }
        }
    }

    /// <summary>Internal BSP node. ChildA = left/top, ChildB = right/bottom.</summary>
    public sealed class SplitNode : LayoutNode
    {
        public LayoutNode ChildA;
        public LayoutNode ChildB;

        public SplitOrientation Orientation = SplitOrientation.Horizontal;

        /// <summary>Fraction of the area given to ChildA (0.5 = even split).</summary>
        public double Ratio = 0.5;

        /// <summary>Hyprland dwindle preserve_split: the orientation of this split is locked.</summary>
        public bool Preserved;

        public override IEnumerable<LeafNode> Leaves => ChildA.Leaves.Concat(ChildB.Leaves);
    }

    /// <summary>A managed (tiled) window.</summary>
    public sealed class LeafNode : LayoutNode
    {
        public IntPtr Hwnd;

        /// <summary>Pseudotile: keep a preferred size centered inside the tiled slot.</summary>
        public bool Pseudotile;

        /// <summary>Pseudotile preferred size as a fraction of the slot (0.1–1.0).</summary>
        public double PseudoWidth = 0.65, PseudoHeight = 0.7;

        /// <summary>Last tiled rectangle — used as the floating rectangle when untiled.</summary>
        public RECT LastRect;

        public override IEnumerable<LeafNode> Leaves { get { yield return this; } }
    }

    /// <summary>
    /// One workspace+monitor's binary split tree plus the geometry math.
    /// Pure model — no Win32 calls, no UI. The engine drives it.
    /// </summary>
    public sealed class LayoutTree
    {
        public LayoutNode Root;
        public IntPtr Monitor;
        public string Device = "";
        public RECT WorkArea;

        private readonly TilingConfig _cfg;

        public LayoutTree(TilingConfig cfg, IntPtr monitor, string device, RECT workArea)
        {
            _cfg = cfg;
            Monitor = monitor;
            Device = device;
            WorkArea = workArea;
        }

        public TilingConfig Config => _cfg;

        // ------------------------------------------------------------------ insertion

        /// <summary>
        /// Inserts a new window, replacing <paramref name="insertAt"/> (the focused leaf) with a
        /// split. Returns the new leaf, or the root leaf when the tree was empty.
        /// Orientation: pending preselect → explicit; preserve_split → inherit; else auto W/H of
        /// the *replaced window's* rect (Hyprland dwindle). Smart split uses the cursor position.
        /// </summary>
        public LeafNode Insert(IntPtr hwnd, LeafNode insertAt, Preselect preselect, int cursorX, int cursorY)
        {
            if (Root == null)
            {
                var root = new LeafNode { Hwnd = hwnd, Pseudotile = _cfg.Pseudotile };
                Root = root;
                return root;
            }
            if (insertAt == null) insertAt = Root.LeavesInOrder().FirstOrDefault();
            if (insertAt == null)
            {
                Root = null;             // defensive: a tree without leaves cannot exist; restart clean
                return Insert(hwnd, null, Preselect.None, -1, -1);
            }

            var split = new SplitNode();
            var newLeaf = new LeafNode { Hwnd = hwnd, Pseudotile = _cfg.Pseudotile };

            // Splice: insertAt's parent now points at the split; the split wraps old + new.
            var parent = insertAt.Parent;
            split.Parent = parent;
            if (parent == null) Root = split;
            else if (parent.ChildA == insertAt) parent.ChildA = split;
            else parent.ChildB = split;

            // --- orientation -------------------------------------------------
            SplitOrientation orient;
            bool newIsChildA;

            if (preselect != Preselect.None)
            {
                // Manual preselection wins: direction says both orientation and side.
                switch (preselect)
                {
                    case Preselect.Left:  orient = SplitOrientation.Horizontal; newIsChildA = true;  break;
                    case Preselect.Right: orient = SplitOrientation.Horizontal; newIsChildA = false; break;
                    case Preselect.Up:    orient = SplitOrientation.Vertical;   newIsChildA = true;  break;
                    case Preselect.Down:  orient = SplitOrientation.Vertical;   newIsChildA = false; break;
                    default:              orient = SplitOrientation.Horizontal; newIsChildA = false; break;
                }
                split.Preserved = _cfg.PreserveSplit;
            }
            else if (_cfg.DefaultSplit == "Horizontal")
            {
                orient = SplitOrientation.Horizontal; newIsChildA = false;
                split.Preserved = _cfg.PreserveSplit;
            }
            else if (_cfg.DefaultSplit == "Vertical")
            {
                orient = SplitOrientation.Vertical; newIsChildA = false;
                split.Preserved = _cfg.PreserveSplit;
            }
            else
            {
                // Auto (dwindle): decide from the replaced window's rectangle.
                RECT r = RectOf(insertAt);
                bool horizontal = (r.Right - r.Left) >= (r.Bottom - r.Top) * 1.25;   // hysteresis so square-ish stays mixed
                orient = horizontal ? SplitOrientation.Horizontal : SplitOrientation.Vertical;
                split.Preserved = _cfg.PreserveSplit;
                newIsChildA = false;
            }

            split.Orientation = orient;

            // --- ratio -------------------------------------------------------
            double ratio = Math.Clamp(_cfg.DefaultSplitRatio, _cfg.MinSplitRatio, _cfg.MaxSplitRatio);

            if (_cfg.SmartSplit && preselect == Preselect.None && cursorX >= 0)
            {
                // Split where the cursor entered: ChildA keeps the portion on its side of
                // the cursor (left/top for ChildA-first splits, which auto mode always uses).
                RECT r = RectOf(insertAt);
                int w = Math.Max(1, r.Right - r.Left), h = Math.Max(1, r.Bottom - r.Top);
                double frac = orient == SplitOrientation.Horizontal
                    ? (cursorX - r.Left) / (double)w
                    : (cursorY - r.Top) / (double)h;
                ratio = Math.Clamp(frac, _cfg.MinSplitRatio, _cfg.MaxSplitRatio);
            }
            else if (_cfg.SplitBias == "Active")
            {
                // Existing window keeps the configured share; the new window takes the rest.
                ratio = newIsChildA ? 1.0 - ratio : ratio;
            }
            // Bias "New": the new window takes the configured share.
            else if (!newIsChildA)
            {
                ratio = 1.0 - ratio;
            }

            split.Ratio = Math.Clamp(ratio, _cfg.MinSplitRatio, _cfg.MaxSplitRatio);
            split.ChildA = newIsChildA ? newLeaf : insertAt;
            split.ChildB = newIsChildA ? insertAt : newLeaf;
            insertAt.Parent = split;
            newLeaf.Parent = split;
            return newLeaf;
        }

        /// <summary>Best rectangle currently assigned to a leaf (LastRect if no layout computed).</summary>
        private RECT RectOf(LeafNode leaf)
        {
            if (leaf.LastRect.Right > leaf.LastRect.Left && leaf.LastRect.Bottom > leaf.LastRect.Top)
                return leaf.LastRect;
            return WorkArea;
        }

        // ------------------------------------------------------------------ removal / swap

        /// <summary>Removes a leaf; the sibling subtree takes its place (standard BSP collapse).</summary>
        public void Remove(LeafNode leaf)
        {
            var parent = leaf.Parent;
            if (parent == null) { Root = null; return; }

            var survivor = parent.ChildA == leaf ? parent.ChildB : parent.ChildA;
            survivor.Parent = parent.Parent;
            if (parent.Parent == null) Root = survivor;
            else if (parent.Parent.ChildA == parent) parent.Parent.ChildA = survivor;
            else parent.Parent.ChildB = survivor;
        }

        /// <summary>Swaps the window payloads of two leaves (tree shape unchanged).</summary>
        public static void Swap(LeafNode a, LeafNode b)
        {
            (a.Hwnd, b.Hwnd) = (b.Hwnd, a.Hwnd);
            (a.Pseudotile, b.Pseudotile) = (b.Pseudotile, a.Pseudotile);
        }

        /// <summary>Reparents <paramref name="leaf"/> under <paramref name="target"/> (used by move-between-monitors).</summary>
        public LeafNode Adopt(IntPtr hwnd, LeafNode target, Preselect preselect)
            => Insert(hwnd, target, preselect, -1, -1);

        public LeafNode FindLeaf(IntPtr hwnd) =>
            Root?.LeavesInOrder().FirstOrDefault(l => l.Hwnd == hwnd);

        // ------------------------------------------------------------------ geometry

        /// <summary>
        /// Computes leaf rectangles. Outer gap frames the whole layout; inner gaps straddle
        /// every divider. Smart gaps collapse all gaps when exactly one window is tiled.
        /// </summary>
        public Dictionary<IntPtr, RECT> ComputeLayout()
        {
            var result = new Dictionary<IntPtr, RECT>();
            if (Root == null) return result;

            bool smart = _cfg.SmartGaps && Root.LeafCount == 1;
            int outer = smart ? 0 : Math.Max(0, _cfg.OuterGap);
            int inner = smart ? 0 : Math.Max(0, _cfg.InnerGap);

            var area = new RECT
            {
                Left = WorkArea.Left + outer,
                Top = WorkArea.Top + outer,
                Right = WorkArea.Right - outer,
                Bottom = WorkArea.Bottom - outer
            };

            // Optional centered single-window mode (config: CenterSingleWindow + max width).
            // Applies regardless of smart gaps — centering is an explicit user choice.
            if (Root is LeafNode && _cfg.CenterSingleWindow)
            {
                int maxPct = _cfg.SingleWindowMaxWidthPct;
                if (maxPct > 0)
                {
                    maxPct = Math.Clamp(maxPct, 10, 100);
                    int maxW = (area.Right - area.Left) * maxPct / 100;
                    int w = Math.Min(area.Right - area.Left, maxW);
                    int left = area.Left + ((area.Right - area.Left) - w) / 2;
                    result[((LeafNode)Root).Hwnd] = new RECT { Left = left, Top = area.Top, Right = left + w, Bottom = area.Bottom };
                    ((LeafNode)Root).LastRect = result[((LeafNode)Root).Hwnd];
                    return result;
                }
            }

            Assign(Root, area, inner, result);
            return result;
        }

        private static void Assign(LayoutNode node, RECT area, int inner, Dictionary<IntPtr, RECT> result)
        {
            switch (node)
            {
                case LeafNode leaf:
                    result[leaf.Hwnd] = leaf.Pseudotile ? PseudoRect(area, leaf) : area;
                    leaf.LastRect = area;
                    break;

                case SplitNode s:
                    int span = s.Orientation == SplitOrientation.Horizontal
                        ? area.Right - area.Left
                        : area.Bottom - area.Top;
                    int main = (int)Math.Round(span * Math.Clamp(s.Ratio, 0.01, 0.99));
                    int g = Math.Min(inner, Math.Max(0, span / 8));   // don't let gaps eat tiny spans

                    RECT a, b;
                    if (s.Orientation == SplitOrientation.Horizontal)
                    {
                        a = new RECT { Left = area.Left, Top = area.Top, Right = area.Left + main - g / 2, Bottom = area.Bottom };
                        b = new RECT { Left = area.Left + main + (g - g / 2), Top = area.Top, Right = area.Right, Bottom = area.Bottom };
                    }
                    else
                    {
                        a = new RECT { Left = area.Left, Top = area.Top, Right = area.Right, Bottom = area.Top + main - g / 2 };
                        b = new RECT { Left = area.Left, Top = area.Top + main + (g - g / 2), Right = area.Right, Bottom = area.Bottom };
                    }
                    Assign(s.ChildA, a, inner, result);
                    Assign(s.ChildB, b, inner, result);
                    break;
            }
        }

        private static RECT PseudoRect(RECT slot, LeafNode leaf)
        {
            int w = Math.Max(1, slot.Right - slot.Left), h = Math.Max(1, slot.Bottom - slot.Top);
            int pw = (int)Math.Clamp(w * Math.Clamp(leaf.PseudoWidth, 0.1, 1.0), 1, w);
            int ph = (int)Math.Clamp(h * Math.Clamp(leaf.PseudoHeight, 0.1, 1.0), 1, h);
            int cx = slot.Left + (w - pw) / 2, cy = slot.Top + (h - ph) / 2;
            return new RECT { Left = cx, Top = cy, Right = cx + pw, Bottom = cy + ph };
        }

        // ------------------------------------------------------------------ navigation

        /// <summary>
        /// Nearest leaf whose rect lies in the requested direction from <paramref name="from"/>.
        /// Candidates are scored by perpendicular overlap (must be &gt; 0) then axis distance —
        /// this behaves correctly on arbitrary BSP shapes, not just grids. Returns null when
        /// nothing is in that direction (wrap handled by the caller).
        /// </summary>
        public static LeafNode NeighborInDirection(LayoutTree tree, LeafNode from, Direction dir, bool wrap)
        {
            if (tree?.Root == null || from == null) return null;
            var rects = tree.ComputeLayout();
            RECT r = rects.TryGetValue(from.Hwnd, out var rr) ? rr : from.LastRect;
            return NeighborInDirection(tree, from, r, rects, dir, wrap);
        }

        public static LeafNode NeighborInDirection(
            LayoutTree tree, LeafNode from, RECT fromRect,
            Dictionary<IntPtr, RECT> rects, Direction dir, bool wrap)
        {
            if (tree?.Root == null || from == null) return null;
            rects ??= tree.ComputeLayout();

            LeafNode best = null;
            double bestScore = double.MaxValue;

            foreach (var leaf in tree.Root.LeavesInOrder())
            {
                if (leaf == from) continue;
                if (!rects.TryGetValue(leaf.Hwnd, out var r)) continue;

                bool inDir = dir switch
                {
                    Direction.Left  => r.Right <= fromRect.Left + 2,
                    Direction.Right => r.Left >= fromRect.Right - 2,
                    Direction.Up    => r.Bottom <= fromRect.Top + 2,
                    Direction.Down  => r.Top >= fromRect.Bottom - 2,
                    _ => false
                };
                if (!inDir) continue;

                // Perpendicular overlap (required), then distance along the axis.
                double overlap, dist;
                switch (dir)
                {
                    case Direction.Left:
                        overlap = Overlap(fromRect.Top, fromRect.Bottom, r.Top, r.Bottom);
                        dist = fromRect.Left - r.Right; break;
                    case Direction.Right:
                        overlap = Overlap(fromRect.Top, fromRect.Bottom, r.Top, r.Bottom);
                        dist = r.Left - fromRect.Right; break;
                    case Direction.Up:
                        overlap = Overlap(fromRect.Left, fromRect.Right, r.Left, r.Right);
                        dist = fromRect.Top - r.Bottom; break;
                    default: // Down
                        overlap = Overlap(fromRect.Left, fromRect.Right, r.Left, r.Right);
                        dist = r.Top - fromRect.Bottom; break;
                }
                if (overlap <= 0) continue;
                double score = dist - overlap * 0.05;   // mostly distance, overlap as tie-breaker
                if (score < bestScore) { bestScore = score; best = leaf; }
            }

            if (best == null && wrap)
                best = WrapNeighbor(tree, from, dir);
            return best;
        }

        private static double Overlap(int a1, int a2, int b1, int b2) =>
            Math.Min(a2, b2) - Math.Max(a1, b1);

        /// <summary>Fallback "wrap": first/last leaf in the direction's scan order.</summary>
        private static LeafNode WrapNeighbor(LayoutTree tree, LeafNode from, Direction dir)
        {
            var leaves = tree.Root.LeavesInOrder().ToList();
            int i = leaves.IndexOf(from);
            if (i < 0) return null;
            return dir switch
            {
                Direction.Left => i > 0 ? leaves[i - 1] : leaves[^1],
                Direction.Up => i > 0 ? leaves[i - 1] : leaves[^1],
                Direction.Right => i < leaves.Count - 1 ? leaves[i + 1] : leaves[0],
                Direction.Down => i < leaves.Count - 1 ? leaves[i + 1] : leaves[0],
                _ => null
            };
        }

        // ------------------------------------------------------------------ resize

        /// <summary>
        /// Moves the split ratio of the nearest ancestor that controls the leaf's edge on the
        /// given side. Grow semantics: the edge facing <paramref name="dir"/> moves outward.
        /// Returns false when no ancestor can move in that direction.
        /// </summary>
        public bool Resize(LeafNode leaf, Direction dir, double stepPct)
        {
            LayoutNode node = leaf;   // climbs through split nodes, so not typed as LeafNode
            var parent = node.Parent;
            while (parent != null)
            {
                bool isChildA = parent.ChildA == node;
                bool matches = dir switch
                {
                    // Horizontal splits move their divider left/right.
                    Direction.Right => parent.Orientation == SplitOrientation.Horizontal && isChildA,
                    Direction.Left  => parent.Orientation == SplitOrientation.Horizontal && !isChildA,
                    // Vertical splits move it up/down.
                    Direction.Down  => parent.Orientation == SplitOrientation.Vertical && isChildA,
                    Direction.Up    => parent.Orientation == SplitOrientation.Vertical && !isChildA,
                    _ => false
                };
                if (matches)
                {
                    // ChildA growing means +delta; ChildB growing (divider toward ChildA)
                    // means -delta — the ratio always expresses ChildA's share.
                    double sign = parent.ChildA == node ? 1.0 : -1.0;
                    double delta = Math.Max(0.01, stepPct / 100.0) * sign;
                    double old = parent.Ratio;
                    parent.Ratio = Math.Clamp(parent.Ratio + delta, _cfg.MinSplitRatio, _cfg.MaxSplitRatio);
                    return Math.Abs(parent.Ratio - old) > 0.0001;
                }
                node = parent;
                parent = node.Parent;
            }
            return false;
        }

        /// <summary>Hyprland togglesplit: flip the orientation of the split directly above the leaf.</summary>
        public bool ToggleSplit(LeafNode leaf)
        {
            var p = leaf?.Parent;
            if (p == null) return false;
            p.Orientation = p.Orientation == SplitOrientation.Horizontal
                ? SplitOrientation.Vertical : SplitOrientation.Horizontal;
            p.Preserved = true; // an explicit toggle is an intentional, permanent split
            return true;
        }

        // ------------------------------------------------------------------ debug

        public string RenderTree()
        {
            var sb = new StringBuilder();
            Render(Root, 0, sb);
            return sb.ToString();
        }

        private static void Render(LayoutNode node, int depth, StringBuilder sb)
        {
            var pad = new string(' ', depth * 2);
            switch (node)
            {
                case null: sb.AppendLine(pad + "(empty)"); break;
                case LeafNode leaf:
                    sb.AppendLine(pad + $"leaf {leaf.Hwnd}{(leaf.Pseudotile ? " pseudo" : "")}");
                    break;
                case SplitNode s:
                    sb.AppendLine(pad + $"split {(s.Orientation == SplitOrientation.Horizontal ? "H" : "V")}" +
                                  $" ratio={s.Ratio:0.00}{(s.Preserved ? " [preserved]" : "")}");
                    Render(s.ChildA, depth + 1, sb);
                    Render(s.ChildB, depth + 1, sb);
                    break;
            }
        }
    }
}
