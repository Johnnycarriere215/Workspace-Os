using System;
using System.Windows.Threading;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Interop;

namespace WorkspaceOS.Core.WindowSystem
{
    /// <summary>
    /// Focus-follows-mouse: after the pointer rests on an ordinary top-level
    /// window for 250 ms, bring that window forward. This makes switching a
    /// desktop and then typing land in the window actually under the mouse.
    ///
    /// It deliberately stays out of games: a fullscreen app suppresses the
    /// service, and mouse drags/resizes are ignored. The bar, taskbar, desktop,
    /// menus, tool windows, cloaked windows and WorkspaceOS's own windows are
    /// never focus targets.
    /// </summary>
    public sealed class HoverFocusService : IDisposable
    {
        private const int PollMilliseconds = 100;
        private const int MouseMovementTolerance = 3;
        private const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_MBUTTON = 0x04;

        private static readonly TimeSpan Dwell = TimeSpan.FromMilliseconds(250);

        private readonly ConfigService _configs;
        private readonly DispatcherTimer _timer;
        private readonly HoverFocusTracker _tracker = new();
        private bool _disposed;

        public HoverFocusService(ConfigService configs)
        {
            _configs = configs;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PollMilliseconds) };
            _timer.Tick += (_, _) => Tick();
        }

        public void Start() => _timer.Start();

        private void Tick()
        {
            if (_disposed) return;

            if (!_configs.Config.General.FocusWindowOnHover || MouseButtonDown())
            {
                _tracker.Reset();
                return;
            }

            // A game owns the screen. Never steal its focus, even if the pointer
            // passes over another window on a second monitor.
            if (FullscreenAppService.IsForegroundFullscreen())
            {
                _tracker.Reset();
                return;
            }

            if (!NativeMethods.GetCursorPos(out POINT point))
            {
                _tracker.Reset();
                return;
            }

            IntPtr hit = NativeMethods.WindowFromPoint(point);
            if (hit == IntPtr.Zero)
            {
                _tracker.Reset();
                return;
            }

            IntPtr hwnd = NativeMethods.GetAncestor(hit, NativeMethods.GA_ROOT);
            if (hwnd == IntPtr.Zero) hwnd = hit;

            if (!CanFocus(hwnd))
            {
                _tracker.Reset();
                return;
            }

            if (NativeMethods.GetForegroundWindow() == hwnd)
            {
                // Already focused. Remember that instead of restarting the
                // dwell, so Alt+Tab/desktop changes are not pulled back by a
                // pointer that has not moved.
                _tracker.MarkSatisfied(hwnd, point);
                return;
            }

            if (!_tracker.ShouldFocus(DateTime.UtcNow, hwnd, point, Dwell, MouseMovementTolerance))
                return;

            FocusWindow(hwnd);
            _tracker.MarkSatisfied(hwnd, point);
        }

        private static bool CanFocus(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero
                || !NativeMethods.IsWindow(hwnd)
                || !NativeMethods.IsWindowVisible(hwnd)
                || !NativeMethods.IsWindowEnabled(hwnd)
                || NativeMethods.IsIconic(hwnd))
                return false;

            if (!WindowTracker.IsManageable(hwnd) || !WindowTracker.IsReallyVisible(hwnd))
                return false;

            long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            return (ex & (NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW)) == 0;
        }

        private static bool MouseButtonDown()
        {
            return (NativeMethods.GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0
                || (NativeMethods.GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0
                || (NativeMethods.GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;
        }

        /// <summary>
        /// SetForegroundWindow is normally limited for a background process.
        /// Attaching our input queue to the current foreground queue for the
        /// duration of the call is the standard way to honor an explicit user
        /// hover without flashing the taskbar. No input is synthesized.
        /// </summary>
        private static void FocusWindow(IntPtr hwnd)
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == hwnd) return;

            uint thisThread = NativeMethods.GetCurrentThreadId();
            uint foregroundThread = foreground == IntPtr.Zero
                ? 0
                : NativeMethods.GetWindowThreadProcessId(foreground, out _);

            bool attached = false;
            try
            {
                if (foregroundThread != 0 && foregroundThread != thisThread)
                    attached = NativeMethods.AttachThreadInput(thisThread, foregroundThread, true);

                NativeMethods.BringWindowToTop(hwnd);
                if (NativeMethods.SetForegroundWindow(hwnd))
                    ConfigService.Log($"Hover focus: {NativeMethods.GetWindowTitle(hwnd)}");
            }
            finally
            {
                if (attached)
                    NativeMethods.AttachThreadInput(thisThread, foregroundThread, false);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _tracker.Reset();
        }
    }
}
