using System;
using WorkspaceOS.Core.Interop;
using WorkspaceOS.Core.WindowSystem;
using Xunit;

namespace WorkspaceOS.Tests
{
    public class HoverFocusTrackerTests
    {
        private static readonly IntPtr WindowA = new(101);
        private static readonly IntPtr WindowB = new(202);
        private static readonly TimeSpan Dwell = TimeSpan.FromMilliseconds(250);
        private static readonly DateTime Start = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void WaitsForTheDwellBeforeFocusing()
        {
            var tracker = new HoverFocusTracker();
            var point = new POINT { X = 100, Y = 100 };

            Assert.False(tracker.ShouldFocus(Start, WindowA, point, Dwell));
            Assert.False(tracker.ShouldFocus(Start.AddMilliseconds(200), WindowA, point, Dwell));
            Assert.True(tracker.ShouldFocus(Start.AddMilliseconds(250), WindowA, point, Dwell));
        }

        [Fact]
        public void FocusesOnlyOncePerHover()
        {
            var tracker = new HoverFocusTracker();
            var point = new POINT { X = 100, Y = 100 };

            tracker.ShouldFocus(Start, WindowA, point, Dwell);
            Assert.True(tracker.ShouldFocus(Start.AddMilliseconds(300), WindowA, point, Dwell));
            Assert.False(tracker.ShouldFocus(Start.AddMilliseconds(500), WindowA, point, Dwell));
        }

        [Fact]
        public void MovementRestartsTheDwell()
        {
            var tracker = new HoverFocusTracker();
            var point = new POINT { X = 100, Y = 100 };

            tracker.ShouldFocus(Start, WindowA, point, Dwell);
            Assert.False(tracker.ShouldFocus(Start.AddMilliseconds(200), WindowA,
                new POINT { X = 110, Y = 100 }, Dwell));
            Assert.False(tracker.ShouldFocus(Start.AddMilliseconds(300), WindowA,
                new POINT { X = 110, Y = 100 }, Dwell));
            Assert.True(tracker.ShouldFocus(Start.AddMilliseconds(450), WindowA,
                new POINT { X = 110, Y = 100 }, Dwell));
        }

        [Fact]
        public void MovingToAnotherWindowRestartsTheDwell()
        {
            var tracker = new HoverFocusTracker();
            var point = new POINT { X = 100, Y = 100 };

            tracker.ShouldFocus(Start, WindowA, point, Dwell);
            Assert.False(tracker.ShouldFocus(Start.AddMilliseconds(300), WindowB, point, Dwell));
            Assert.True(tracker.ShouldFocus(Start.AddMilliseconds(550), WindowB, point, Dwell));
        }

        [Fact]
        public void AlreadyFocusedWindowIsNotRefocusedWhileStationary()
        {
            var tracker = new HoverFocusTracker();
            var point = new POINT { X = 100, Y = 100 };

            tracker.MarkSatisfied(WindowA, point);
            Assert.False(tracker.ShouldFocus(Start.AddMilliseconds(300), WindowA, point, Dwell));
        }

        [Fact]
        public void LeavingTheWindowReArmsAFormerlyFocusedWindow()
        {
            var tracker = new HoverFocusTracker();
            var point = new POINT { X = 100, Y = 100 };

            tracker.MarkSatisfied(WindowA, point);
            tracker.Reset();
            Assert.False(tracker.ShouldFocus(Start.AddMilliseconds(300), WindowA, point, Dwell));
            Assert.True(tracker.ShouldFocus(Start.AddMilliseconds(550), WindowA, point, Dwell));
        }

        [Fact]
        public void LeavingTheWindowResetsTheHover()
        {
            var tracker = new HoverFocusTracker();
            var point = new POINT { X = 100, Y = 100 };

            tracker.ShouldFocus(Start, WindowA, point, Dwell);
            Assert.False(tracker.ShouldFocus(Start.AddMilliseconds(300), IntPtr.Zero, point, Dwell));
            Assert.False(tracker.ShouldFocus(Start.AddMilliseconds(400), WindowA, point, Dwell));
        }
    }
}
