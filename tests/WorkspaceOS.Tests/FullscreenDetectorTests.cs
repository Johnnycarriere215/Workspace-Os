using WorkspaceOS.Core.Interop;
using WorkspaceOS.Core.WindowSystem;
using Xunit;

namespace WorkspaceOS.Tests
{
    public class FullscreenDetectorTests
    {
        private static readonly RECT Monitor = new()
        {
            Left = 0,
            Top = 0,
            Right = 1920,
            Bottom = 1080
        };

        [Fact]
        public void CoversMonitorAcceptsAOnePixelInset()
        {
            var window = new RECT { Left = 1, Top = 1, Right = 1919, Bottom = 1079 };
            Assert.True(FullscreenDetector.CoversMonitor(window, Monitor));
        }

        [Fact]
        public void CoversMonitorRejectsAWindowThatStopsAtTheWorkArea()
        {
            // A normal maximized app leaves the taskbar/bar outside its work area.
            var window = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };
            Assert.False(FullscreenDetector.CoversMonitor(window, Monitor));
        }

        [Fact]
        public void MaximizedWindowsAreNotTreatedAsGames()
        {
            var window = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
            Assert.False(FullscreenDetector.IsFullscreenWindow(
                window, Monitor, visible: true, iconic: false, zoomed: true,
                processId: 42, ownProcessId: 7, windowClass: "GameWindow"));
        }

        [Fact]
        public void BorderlessWindowIsTreatedAsFullscreen()
        {
            var window = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
            Assert.True(FullscreenDetector.IsFullscreenWindow(
                window, Monitor, visible: true, iconic: false, zoomed: false,
                processId: 42, ownProcessId: 7, windowClass: "GameWindow"));
        }

        [Fact]
        public void ShellAndOwnWindowsAreIgnored()
        {
            var window = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
            Assert.False(FullscreenDetector.IsFullscreenWindow(
                window, Monitor, visible: true, iconic: false, zoomed: false,
                processId: 42, ownProcessId: 7, windowClass: "Shell_TrayWnd"));
            Assert.False(FullscreenDetector.IsFullscreenWindow(
                window, Monitor, visible: true, iconic: false, zoomed: false,
                processId: 42, ownProcessId: 42, windowClass: "WorkspaceOS"));
        }

        [Fact]
        public void HiddenMinimizedOrPartialWindowsAreIgnored()
        {
            var full = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
            Assert.False(FullscreenDetector.IsFullscreenWindow(
                full, Monitor, visible: false, iconic: false, zoomed: false,
                processId: 42, ownProcessId: 7, windowClass: "GameWindow"));
            Assert.False(FullscreenDetector.IsFullscreenWindow(
                full, Monitor, visible: true, iconic: true, zoomed: false,
                processId: 42, ownProcessId: 7, windowClass: "GameWindow"));
            Assert.False(FullscreenDetector.IsFullscreenWindow(
                new RECT { Left = 0, Top = 0, Right = 1280, Bottom = 720 },
                Monitor, visible: true, iconic: false, zoomed: false,
                processId: 42, ownProcessId: 7, windowClass: "GameWindow"));
        }
    }
}
