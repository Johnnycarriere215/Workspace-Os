using System;
using System.Windows.Forms;
using WorkspaceOS.Core.Interop;

namespace WorkspaceOS.Core.WindowSystem
{
    /// <summary>
    /// Free-floating window commands. Deliberately NOT a tiling engine:
    /// commands nudge, maximize, restore, center or fullscreen the focused
    /// window — they never impose a layout on other windows.
    /// </summary>
    public static class WindowCommands
    {
        private const int NudgeStep = 60;

        private static IntPtr Target()
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            return WindowTracker.IsManageable(hwnd) ? hwnd : IntPtr.Zero;
        }

        public static void Move(int dx, int dy)
        {
            var hwnd = Target();
            if (hwnd == IntPtr.Zero || NativeMethods.IsZoomed(hwnd)) return;
            if (!NativeMethods.GetWindowRect(hwnd, out var r)) return;
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
                r.Left + dx * NudgeStep, r.Top + dy * NudgeStep, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }

        /// <summary>
        /// Close the focused window politely: send WM_CLOSE so apps get their
        /// save/exit path (same as clicking the title-bar X). Unsaved work is
        /// never silently discarded. Windows without a close button (no
        /// WS_SYSMENU) are ignored — sending WM_CLOSE there can force-kill
        /// dialogs and other shell furniture.
        /// </summary>
        public static void Close()
        {
            var hwnd = Target();
            if (hwnd == IntPtr.Zero) return;
            long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE).ToInt64();
            if ((style & NativeMethods.WS_SYSMENU) == 0) return;
            NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        public static void Maximize()
        {
            var hwnd = Target();
            if (hwnd != IntPtr.Zero) NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MAXIMIZE);
        }

        public static void Restore()
        {
            var hwnd = Target();
            if (hwnd != IntPtr.Zero) NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        }

        public static void Center()
        {
            var hwnd = Target();
            if (hwnd == IntPtr.Zero) return;
            if (NativeMethods.IsZoomed(hwnd)) NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            if (!NativeMethods.GetWindowRect(hwnd, out var r)) return;
            var screen = Screen.FromHandle(hwnd).WorkingArea;
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
                screen.Left + (screen.Width - w) / 2,
                screen.Top + (screen.Height - h) / 2, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }

        /// <summary>Borderless fullscreen toggle for the focused window.</summary>
        public static void ToggleFullscreen()
        {
            var hwnd = Target();
            if (hwnd == IntPtr.Zero) return;

            if (_fullscreenPrev.TryGetValue(hwnd, out var prev))
            {
                // restore
                var pl = prev;
                NativeMethods.SetWindowPlacement(hwnd, ref pl);
                _fullscreenPrev.Remove(hwnd);
                return;
            }

            var placement = new WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf<WINDOWPLACEMENT>() };
            NativeMethods.GetWindowPlacement(hwnd, ref placement);
            _fullscreenPrev[hwnd] = placement;

            var bounds = Screen.FromHandle(hwnd).Bounds;
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                NativeMethods.SWP_NOZORDER);
        }

        private static readonly System.Collections.Generic.Dictionary<IntPtr, WINDOWPLACEMENT> _fullscreenPrev = new();
    }
}
