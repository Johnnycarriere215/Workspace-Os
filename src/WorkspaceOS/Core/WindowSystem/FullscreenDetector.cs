using System;
using System.Collections.Generic;
using WorkspaceOS.Core.Interop;

namespace WorkspaceOS.Core.WindowSystem
{
    /// <summary>
    /// Pure fullscreen-window rules. Kept free of Win32 calls so the behavior can
    /// be tested off-Windows alongside the rest of the core tests.
    /// </summary>
    public static class FullscreenDetector
    {
        private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Shell_TrayWnd",
            "Shell_SecondaryTrayWnd",
            "Progman",
            "WorkerW",
            "Button"
        };

        /// <summary>True when the window rectangle covers the whole monitor.</summary>
        public static bool CoversMonitor(RECT window, RECT monitor, int tolerance = 2)
        {
            tolerance = Math.Max(0, tolerance);
            return window.Left <= monitor.Left + tolerance
                && window.Top <= monitor.Top + tolerance
                && window.Right >= monitor.Right - tolerance
                && window.Bottom >= monitor.Bottom - tolerance;
        }

        /// <summary>
        /// A normal maximized window keeps its native frame (caption and/or
        /// sizing border), even when its rectangle covers the monitor. Chrome
        /// and Edge may keep the zoomed bit set while YouTube's HTML5 player
        /// owns the screen, so a zoomed frame is rejected but a zoomed
        /// frameless window is accepted as fullscreen. Unzoomed windows keep
        /// the previous behavior for compatibility with borderless games.
        /// Shell furniture, hidden/minimized windows and our own windows are
        /// ignored.
        /// </summary>
        public static bool IsFullscreenWindow(
            RECT window,
            RECT monitor,
            bool visible,
            bool iconic,
            bool zoomed,
            uint processId,
            int ownProcessId,
            string windowClass,
            bool hasFrame = true)
        {
            if (!visible || iconic) return false;
            if (zoomed && hasFrame) return false;
            if (processId == 0) return false;
            if (ownProcessId > 0 && processId == (uint)ownProcessId) return false;
            if (string.IsNullOrEmpty(windowClass) || ShellClasses.Contains(windowClass)) return false;

            return CoversMonitor(window, monitor);
        }
    }
}
