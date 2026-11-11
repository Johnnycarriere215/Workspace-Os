using System;
using System.Collections.Generic;
using System.Windows.Threading;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Interop;

namespace WorkspaceOS.Core.WindowSystem
{
    /// <summary>
    /// Focus-follows-mouse: after the pointer rests on an ordinary top-level
    /// window for a short moment, bring that window forward. This makes switching
    /// a desktop and then typing land in the window actually under the mouse.
    ///
    /// It deliberately stays out of games: a fullscreen app suppresses the
    /// service, and mouse drags/resizes are ignored. The bar, taskbar, desktop,
    /// menus, tool windows, cloaked windows and WorkspaceOS's own windows are
    /// never focus targets.
    /// </summary>
    public sealed class HoverFocusService : IDisposable
    {
        private const int PollMilliseconds = 50;
        private const int MouseMovementTolerance = 3;
        private const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_MBUTTON = 0x04;

        // Short enough that a normal "move to the window, then Ctrl+T" gesture
        // reaches the new window, long enough to ignore a pointer crossing it.
        private static readonly TimeSpan Dwell = TimeSpan.FromMilliseconds(120);
        private static readonly TimeSpan WorkspaceFocusWindow = TimeSpan.FromMilliseconds(800);

        private readonly ConfigService _configs;
        private readonly DispatcherTimer _timer;
        private readonly HoverFocusTracker _tracker = new();
        private DateTime _workspaceFocusUntil;
        private bool _disposed;

        public HoverFocusService(ConfigService configs)
        {
            _configs = configs;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PollMilliseconds) };
            _timer.Tick += (_, _) => Tick();
        }

        public void Start() => _timer.Start();

        /// <summary>
        /// A desktop switch changes which window is physically under the pointer
        /// without moving the pointer. Focus that window on the first poll after
        /// the switch, so an immediate Ctrl+T (for example) goes to Chrome rather
        /// than to the app that had focus on the previous desktop.
        /// </summary>
        public void OnWorkspaceChanged()
        {
            if (_disposed) return;

            if (!_timer.Dispatcher.CheckAccess())
            {
                _timer.Dispatcher.BeginInvoke(new Action(OnWorkspaceChanged));
                return;
            }

            _workspaceFocusUntil = DateTime.UtcNow + WorkspaceFocusWindow;
        }

        private void Tick()
        {
            if (_disposed) return;

            if (!_configs.Config.General.FocusWindowOnHover || MouseButtonDown())
            {
                _workspaceFocusUntil = default;
                _tracker.Reset();
                return;
            }

            // A game owns the screen. Never steal its focus, even if the pointer
            // passes over another window on a second monitor.
            if (FullscreenAppService.IsForegroundFullscreen())
            {
                _workspaceFocusUntil = default;
                _tracker.Reset();
                return;
            }

            if (!TryGetTarget(out IntPtr hwnd, out POINT point))
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
                _workspaceFocusUntil = default;
                return;
            }

            bool workspaceFocus = _workspaceFocusUntil != default
                && DateTime.UtcNow <= _workspaceFocusUntil;
            if (workspaceFocus)
            {
                // The desktop switch is the explicit user action; do not make
                // them wait out the ordinary hover dwell before keyboard input
                // is routed to the window they just revealed.
                if (FocusWindow(hwnd))
                    _workspaceFocusUntil = default;
                _tracker.MarkSatisfied(hwnd, point);
                return;
            }

            if (_workspaceFocusUntil != default)
                _workspaceFocusUntil = default;

            if (!_tracker.ShouldFocus(DateTime.UtcNow, hwnd, point, Dwell, MouseMovementTolerance))
                return;

            FocusWindow(hwnd);
            _tracker.MarkSatisfied(hwnd, point);
        }

        private static bool TryGetTarget(out IntPtr hwnd, out POINT point)
        {
            hwnd = IntPtr.Zero;
            point = default;

            if (!NativeMethods.GetCursorPos(out point)) return false;

            IntPtr hit = NativeMethods.WindowFromPoint(point);
            if (hit == IntPtr.Zero) return false;

            hwnd = NativeMethods.GetAncestor(hit, NativeMethods.GA_ROOT);
            if (hwnd == IntPtr.Zero) hwnd = hit;
            return CanFocus(hwnd);
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
        /// Attaching our input queue to the current foreground queue (and the
        /// target queue) for the duration of the call is the standard way to
        /// honor an explicit user hover without flashing the taskbar. No input
        /// is synthesized.
        /// </summary>
        private static bool FocusWindow(IntPtr hwnd)
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == hwnd) return true;

            uint thisThread = NativeMethods.GetCurrentThreadId();
            uint foregroundThread = foreground == IntPtr.Zero
                ? 0
                : NativeMethods.GetWindowThreadProcessId(foreground, out _);
            uint targetThread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);

            var attached = new List<uint>();
            try
            {
                foreach (uint thread in new[] { foregroundThread, targetThread })
                {
                    if (thread == 0 || thread == thisThread || attached.Contains(thread)) continue;
                    if (NativeMethods.AttachThreadInput(thisThread, thread, true))
                        attached.Add(thread);
                }

                NativeMethods.BringWindowToTop(hwnd);
                bool active = NativeMethods.SetForegroundWindow(hwnd);
                if (active)
                {
                    // SetForegroundWindow makes the top-level window active;
                    // SetFocus also moves its keyboard focus, which is what
                    // makes Ctrl+T land in Chrome immediately after a switch.
                    NativeMethods.SetFocus(hwnd);
                    ConfigService.Log($"Hover focus: {NativeMethods.GetWindowTitle(hwnd)}");
                }
                return active;
            }
            finally
            {
                foreach (uint thread in attached)
                    NativeMethods.AttachThreadInput(thisThread, thread, false);
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
