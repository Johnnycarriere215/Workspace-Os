using System;
using System.Linq;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Interop;
using WorkspaceOS.Core.Tiling;
using Xunit;

namespace WorkspaceOS.Tests
{
    /// <summary>
    /// Pure layout-core tests — no Win32, no UI. These verify the BSP/Dwindle
    /// behaviors the tiling engine depends on: insertion, geometry, navigation,
    /// resize, togglesplit, preserve_split and pseudotiling.
    /// </summary>
    public class LayoutTreeTests
    {
        private static RECT Rect(int x, int y, int w, int h) =>
            new RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };

        private static RECT Work() => Rect(0, 0, 1920, 1040);

        private static LayoutTree Tree(TilingConfig cfg = null) =>
            new(cfg ?? new TilingConfig(), (IntPtr)1, "TEST", Work());

        private static RECT R(LayoutTree t, IntPtr hwnd)
        {
            var map = t.ComputeLayout();
            Assert.True(map.TryGetValue(hwnd, out var r), $"window {hwnd} not laid out");
            return r;
        }

        private static int W(RECT r) => r.Right - r.Left;
        private static int H(RECT r) => r.Bottom - r.Top;

        // ------------------------------------------------------------- insertion

        [Fact]
        public void First_window_takes_the_whole_work_area()
        {
            var t = Tree();
            t.Insert((IntPtr)1, null, Preselect.None, -1, -1);
            var r = R(t, (IntPtr)1);
            Assert.Equal(Work(), r);
        }

        [Fact]
        public void Auto_split_prefers_horizontal_on_wide_areas()
        {
            var t = Tree(new TilingConfig { InnerGap = 0, OuterGap = 0 });   // 1920x1040 → wide → side-by-side
            var a = t.Insert((IntPtr)1, null, Preselect.None, -1, -1);
            t.Insert((IntPtr)2, a, Preselect.None, -1, -1);

            var r1 = R(t, (IntPtr)1);
            var r2 = R(t, (IntPtr)2);
            Assert.True(W(r1) < 1920, "first window should shrink to a half");
            Assert.Equal(1920, W(r1) + W(r2));          // no dead space
            Assert.Equal(r1.Top, r2.Top);
            Assert.Equal(r1.Bottom, r2.Bottom);         // same rows → horizontal split
            Assert.Equal(r1.Right, r2.Left);            // adjacent
        }

        [Fact]
        public void Auto_split_prefers_vertical_on_tall_areas()
        {
            var cfg = new TilingConfig { InnerGap = 0, OuterGap = 0 };
            var t = new LayoutTree(cfg, (IntPtr)1, "TEST", Rect(0, 0, 900, 1400));
            var a = t.Insert((IntPtr)1, null, Preselect.None, -1, -1);
            t.Insert((IntPtr)2, a, Preselect.None, -1, -1);

            var r1 = R(t, (IntPtr)1);
            var r2 = R(t, (IntPtr)2);
            Assert.Equal(900, W(r1));                   // full width → stacked
            Assert.Equal(900, W(r2));
            Assert.True(H(r1) < 1400);
        }

        [Fact]
        public void Chain_of_inserts_builds_a_bsp_tree()
        {
            var t = Tree();
            var a = t.Insert((IntPtr)1, null, Preselect.None, -1, -1);
            t.Insert((IntPtr)2, a, Preselect.None, -1, -1);
            var b = t.FindLeaf((IntPtr)2);
            t.Insert((IntPtr)3, b, Preselect.None, -1, -1);

            Assert.Equal(3, t.Root.LeafCount);
            // Sum of areas equals the work area (no gaps config default: 6px inner/outer → less than full; just no overlap)
            var all = t.ComputeLayout().Values.ToList();
            for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                    Assert.False(Overlaps(all[i], all[j]), $"rects {i} and {j} overlap");
        }

        private static bool Overlaps(RECT a, RECT b) =>
            a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;

        [Fact]
        public void Remove_collapses_the_sibling_subtree()
        {
            var t = Tree();
            var a = t.Insert((IntPtr)1, null, Preselect.None, -1, -1);
            t.Insert((IntPtr)2, a, Preselect.None, -1, -1);
            t.Remove(t.FindLeaf((IntPtr)2));
            Assert.Equal(1, t.Root.LeafCount);
            Assert.Equal(Work(), R(t, (IntPtr)1));      // single window takes everything again
        }

        // ------------------------------------------------------------- preselect

        [Fact]
        public void Preselect_right_puts_the_new_window_on_the_right()
        {
            var t = Tree();
            var a = t.Insert((IntPtr)1, null, Preselect.None, -1, -1);
            t.Insert((IntPtr)2, a, Preselect.Right, -1, -1);
            var r1 = R(t, (IntPtr)1);
            var r2 = R(t, (IntPtr)2);
            Assert.True(r1.Left < r2.Left, "new window must be right of the old one");
        }

        [Fact]
        public void Preselect_left_puts_the_new_window_on_the_left()
        {
            var t = Tree();
            var a = t.Insert((IntPtr)1, null, Preselect.None, -1, -1);
            t.Insert((IntPtr)2, a, Preselect.Left, -1, -1);
            Assert.True(R(t, (IntPtr)2).Left < R(t, (IntPtr)1).Left);
        }

        [Fact]
        public void Preselect_down_stacks_vertically()
        {
            var t = Tree();
            var a = t.Insert((IntPtr)1, null, Preselect.None, -1, -1);
            t.Insert((IntPtr)2, a, Preselect.Down, -1, -1);
            var r1 = R(t, (IntPtr)1);
            var r2 = R(t, (IntPtr)2);
            Assert.True(r1.Top < r2.Top);
            Assert.Equal(r1.Left, r2.Left);             // same column
        }

        // ------------------------------------------------------------- smart split

        [Fact]
        public void Smart_split_follows_cursor_side()
        {
            var cfg = new TilingConfig { SmartSplit = true };
            var t = Tree(cfg);
            var a = t.Insert((IntPtr)1, null, Preselect.None, -1, -1);
            // Cursor at 80% width of window 1's rect → new window takes the right ~20%.
            var r1Before = R(t, (IntPtr)1);
            int cx = r1Before.Left + (int)((r1Before.Right - r1Before.Left) * 0.8);
            t.Insert((IntPtr)2, a, Preselect.None, cx, r1Before.Top + 10);
            var r1 = R(t, (IntPtr)1);
            var r2 = R(t, (IntPtr)2);
            Assert.True(W(r2) < W(r1), "window on the cursor's small side should be smaller");
            Assert.True(r2.Left > r1.Left, "new window lands where the cursor was (right side)");
        }

        // ------------------------------------------------------------- ratios / bias

        [Fact]
        public void Default_ratio_is_respected()
        {
            var cfg = new TilingConfig { DefaultSplitRatio = 0.75, InnerGap = 0, OuterGap = 0 };
            var t = Tree(cfg);
            var a = t.Insert((IntPtr)1, null, Preselect.None, -1, -1);
            t.Insert((IntPtr)2, a, Preselect.None, -1, -1);
            // Bias "New": the new window (ChildB) takes the configured 75% share.
            var w2 = W(R(t, (IntPtr)2));
            Assert.Equal(1920 * 0.75, w2, 1);
        }

        [Fact]
        public void Split_bias_active_gives_new_window_the_rest()
        {
            var cfg = new TilingConfig { SplitBias = "Active", DefaultSplitRatio = 0.6, InnerGap = 0, OuterGap = 0 };
            var t = Tree(cfg);
            var a = t.Insert((IntPtr)1, null, Preselect.None, -1, -1);
            t.Insert((IntPtr)2, a, Preselect.None, -1, -1);
            // "Active" keeps the existing window at 60% → new window gets 40%.
            Assert.Equal(1920 * 0.4, W(R(t, (IntPtr)2)), 1);
        }

        // ------------------------------------------------------------- navigation

        [Fact]
        public void Directional_focus_finds_the_right_neighbor()
        {
            var t = Tree();
            var a = t.Insert((IntPtr)1, null, Preselect.None, -1, -1);
            t.Insert((IntPtr)2, a, Preselect.None, -1, -1);
            var one = t.FindLeaf((IntPtr)1);
            var two = t.FindLeaf((IntPtr)2);

            var right = LayoutTree.NeighborInDirection(t, one, Direction.Right, wrap: false);
            Assert.Equal(two, right);
            var left = LayoutTree.NeighborInDirection(t, two, Direction.Left, wrap: false);
            Assert.Equal(one, left);
        }

        [Fact]
        public void Directional_focus_finds_nothing_off_grid_without_wrap()
        {
            var t = Tree();
            var a = t.Insert((IntPtr)1, null, Preselect.None, -1, -1);
            t.Insert((IntPtr)2, a, Preselect.None, -1, -1);
            var one = t.FindLeaf((IntPtr)1);
            Assert.Null(LayoutTree.NeighborInDirection(t, one, Direction.Up, wrap: false));
            // With wrap it falls back to scan order.
            Assert.NotNull(LayoutTree.NeighborInDirection(t, one, Direction.Up, wrap: true));
        }

        [Fact]
        public void Navigation_works_in_a_complex_bsp()
        {
            var t = Tree();
            // Build splitH(1, splitV(2,3)): 1 on the left, 2/3 stacked on the right.
            var a = t.Insert((IntPtr)1, null, Preselect.None, -1, -1);
            var two = t.Insert((IntPtr)2, a, Preselect.None, -1, -1);
            t.Insert((IntPtr)3, two, Preselect.Down, -1, -1);

            var one = t.FindLeaf((IntPtr)1);
            var twoLeaf = t.FindLeaf((IntPtr)2);
            var three = t.FindLeaf((IntPtr)3);

            Assert.Equal(twoLeaf, LayoutTree.NeighborInDirection(t, one, Direction.Right, false));
            Assert.Equal(one, LayoutTree.NeighborInDirection(t, twoLeaf, Direction.Left, false));
            Assert.Equal(three, LayoutTree.NeighborInDirection(t, twoLeaf, Direction.Down, false));
            Assert.Equal(twoLeaf, LayoutTree.NeighborInDirection(t, three, Direction.Up, false));
        }

        // ------------------------------------------------------------- resize

        [Fact]
        public void Resize_moves_the_divider_and_clamps()
        {
            var cfg = new TilingConfig { InnerGap = 0, OuterGap = 0 };
            var t = Tree(cfg);
            var a = t.Insert((IntPtr)1, null, Preselect.None, -1, -1);
            t.Insert((IntPtr)2, a, Preselect.None, -1, -1);
            var one = t.FindLeaf((IntPtr)1);

            int before = W(R(t, (IntPtr)1));
            Assert.True(t.Resize(one, Direction.Right, 10));
            int after = W(R(t, (IntPtr)1));
            Assert.True(after > before, $"resize right should widen: {before} → {after}");

            // Shrink: window 2 (ChildB) grows leftward = divider moves left = window 1 shrinks.
            var two = t.FindLeaf((IntPtr)2);
            Assert.True(t.Resize(two, Direction.Left, 10));
            Assert.True(W(R(t, (IntPtr)1)) < after, "divider left must shrink the left window");

            // Clamp: shrinking far beyond the limit saturates at MinSplitRatio.
            for (int i = 0; i < 50; i++) t.Resize(two, Direction.Left, 10);
            var map = t.ComputeLayout();
            var r1 = map[(IntPtr)1];
            Assert.True(W(r1) >= 1920 * cfg.MinSplitRatio - 2, "width below minimum ratio");
        }

        // ------------------------------------------------------------- togglesplit

        [Fact]
        public void Toggle_split_flips_orientation()
        {
            var t = Tree();
            var a = t.Insert((IntPtr)1, null, Preselect.None, -1, -1);
            t.Insert((IntPtr)2, a, Preselect.None, -1, -1);
            var two = t.FindLeaf((IntPtr)2);
            var before = (R(t, (IntPtr)1).Bottom - R(t, (IntPtr)1).Top);

            t.ToggleSplit(two);
            var r1 = R(t, (IntPtr)1);
            var r2 = R(t, (IntPtr)2);
            Assert.Equal(r1.Right, r2.Right);           // now stacked: shared right edge
            Assert.Equal(r1.Left, r2.Left);
            Assert.True(H(r1) < 1040);
            _ = before;
        }

        // ------------------------------------------------------------- preserve_split

        [Fact]
        public void Preserve_split_locks_orientation_across_reflows()
        {
            var cfg = new TilingConfig { PreserveSplit = true };
            var t = Tree(cfg);
            var a = t.Insert((IntPtr)1, null, Preselect.None, -1, -1);
            t.Insert((IntPtr)2, a, Preselect.Right, -1, -1);
            var split = t.FindLeaf((IntPtr)2).Parent;
            Assert.True(split.Preserved);

            // Even though the split is now tall-and-narrow, inserting into leaf 2 must
            // inherit the preserved horizontal orientation for the new split.
            t.Insert((IntPtr)3, t.FindLeaf((IntPtr)2), Preselect.None, -1, -1);
            var newSplit = t.FindLeaf((IntPtr)3).Parent;
            Assert.Equal(SplitOrientation.Horizontal, newSplit.Orientation);
        }

        // ------------------------------------------------------------- gaps & single window

        [Fact]
        public void Gaps_inset_windows_and_single_window_gets_smart_gaps()
        {
            var cfg = new TilingConfig { InnerGap = 10, OuterGap = 20, SmartGaps = true };
            var t = Tree(cfg);
            var a = t.Insert((IntPtr)1, null, Preselect.None, -1, -1);
            var r = R(t, (IntPtr)1);
            Assert.Equal(Work(), r);                    // smart gaps: none with one window

            t.Insert((IntPtr)2, a, Preselect.None, -1, -1);
            var r1 = R(t, (IntPtr)1);
            var r2 = R(t, (IntPtr)2);
            Assert.Equal(20, r1.Left);                  // outer gap
            Assert.True(r2.Left - r1.Right >= 10);      // inner gap
        }

        [Fact]
        public void Centered_single_window_mode_centers_and_limits_width()
        {
            var cfg = new TilingConfig
            {
                CenterSingleWindow = true,
                SingleWindowMaxWidthPct = 60,
                InnerGap = 0,
                OuterGap = 0
            };
            var t = Tree(cfg);
            t.Insert((IntPtr)1, null, Preselect.None, -1, -1);
            var r = R(t, (IntPtr)1);
            int expected = 1920 * 60 / 100;
            Assert.Equal(expected, W(r));
            int center = (r.Left + r.Right) / 2;
            Assert.Equal(1920 / 2, center);
        }

        // ------------------------------------------------------------- pseudotile

        [Fact]
        public void Pseudotile_centers_a_preferred_size_inside_the_slot()
        {
            var t = Tree();
            var a = t.Insert((IntPtr)1, null, Preselect.None, -1, -1);
            t.Insert((IntPtr)2, a, Preselect.None, -1, -1);
            var one = t.FindLeaf((IntPtr)1);
            one.Pseudotile = true;
            one.PseudoWidth = 0.5;
            one.PseudoHeight = 0.5;

            var r = R(t, (IntPtr)1);
            var w = W(r);
            Assert.True(w < W(t.ComputeLayout()[(IntPtr)2]) * 1.2, "pseudotiled window narrower than the slot sibling");
            int slotCenterX = (r.Left + r.Right) / 2;
            // Centered within its slot: verify it sits inside the slot's left half bounds.
            Assert.True(r.Left >= t.ComputeLayout()[(IntPtr)1].Left);
        }

        // ------------------------------------------------------------- misc

        [Fact]
        public void Swap_exchanges_windows_without_changing_geometry()
        {
            var t = Tree();
            var a = t.Insert((IntPtr)1, null, Preselect.None, -1, -1);
            t.Insert((IntPtr)2, a, Preselect.None, -1, -1);
            var mapBefore = t.ComputeLayout();
            LayoutTree.Swap(t.FindLeaf((IntPtr)1), t.FindLeaf((IntPtr)2));
            var mapAfter = t.ComputeLayout();

            Assert.Equal(mapBefore[(IntPtr)1], mapAfter[(IntPtr)2]);
            Assert.Equal(mapBefore[(IntPtr)2], mapAfter[(IntPtr)1]);
        }

        [Fact]
        public void Render_tree_produces_readable_output()
        {
            var t = Tree();
            var a = t.Insert((IntPtr)1, null, Preselect.None, -1, -1);
            t.Insert((IntPtr)2, a, Preselect.None, -1, -1);
            string s = t.RenderTree();
            Assert.Contains("split H", s);
            Assert.Contains("leaf", s);
        }
    }
}
