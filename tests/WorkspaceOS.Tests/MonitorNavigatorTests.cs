using System.Collections.Generic;
using WorkspaceOS.Core.Interop;
using WorkspaceOS.Core.WindowSystem;
using Xunit;

namespace WorkspaceOS.Tests
{
    public class MonitorNavigatorTests
    {
        private static RECT Rect(int left, int top, int right, int bottom) =>
            new RECT { Left = left, Top = top, Right = right, Bottom = bottom };

        private static readonly List<RECT> SideBySide = new()
        {
            Rect(0, 0, 1920, 1080),       // primary
            Rect(1920, 0, 3840, 1080),    // right
            Rect(-1920, 0, 0, 1080),      // left
        };

        [Fact]
        public void PicksHorizontalNeighbor()
        {
            Assert.Equal(1, MonitorNavigator.Pick(SideBySide[0], SideBySide, WindowDirection.Right));
            Assert.Equal(2, MonitorNavigator.Pick(SideBySide[0], SideBySide, WindowDirection.Left));
            Assert.Equal(0, MonitorNavigator.Pick(SideBySide[1], SideBySide, WindowDirection.Left));
        }

        [Fact]
        public void ReturnsNothingWhenDirectionIsEmpty()
        {
            Assert.Equal(-1, MonitorNavigator.Pick(SideBySide[0], SideBySide, WindowDirection.Up));
            Assert.Equal(-1, MonitorNavigator.Pick(SideBySide[0], SideBySide, WindowDirection.Down));
        }

        [Fact]
        public void PicksVerticalNeighbor()
        {
            var stacked = new List<RECT>
            {
                Rect(0, 0, 1920, 1080),
                Rect(0, 1080, 1920, 2160),
            };

            Assert.Equal(1, MonitorNavigator.Pick(stacked[0], stacked, WindowDirection.Down));
            Assert.Equal(0, MonitorNavigator.Pick(stacked[1], stacked, WindowDirection.Up));
        }

        [Fact]
        public void PrefersSameRowOverDiagonal()
        {
            var monitors = new List<RECT>
            {
                Rect(0, 0, 1920, 1080),
                Rect(1920, 0, 3840, 1080),       // directly right
                Rect(1920, 1100, 3840, 2180),    // right + below
            };

            Assert.Equal(1, MonitorNavigator.Pick(monitors[0], monitors, WindowDirection.Right));
        }
    }
}
